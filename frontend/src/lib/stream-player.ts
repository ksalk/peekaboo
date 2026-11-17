interface StreamPlayerCallbacks {
  onPlaying: () => void;
  onError: (message: string) => void;
}

interface StreamMessage {
  type?: string;
  codec?: string;
  message?: string;
}

const BOX_HEADER_LENGTH = 8;

function boxType(buffer: ArrayBuffer): string {
  if (buffer.byteLength < BOX_HEADER_LENGTH) {
    return '';
  }

  return String.fromCharCode(...new Uint8Array(buffer, 4, 4));
}

function concatBuffers(first: ArrayBuffer, second: ArrayBuffer): ArrayBuffer {
  const combined = new Uint8Array(first.byteLength + second.byteLength);
  combined.set(new Uint8Array(first), 0);
  combined.set(new Uint8Array(second), first.byteLength);
  return combined.buffer;
}

/**
 * Plays the server's fragmented MP4 over WebSocket into a MediaSource.
 * Closing the socket is the Stop signal: the server tears down ffmpeg when the
 * last viewer for a camera disconnects.
 */
export class StreamPlayer {
  private socket: WebSocket | null = null;
  private mediaSource: MediaSource | null = null;
  private sourceBuffer: SourceBuffer | null = null;
  private objectUrl: string | null = null;
  private queue: ArrayBuffer[] = [];
  private pendingMoof: ArrayBuffer | null = null;
  private appending = false;
  private stopped = false;
  private failed = false;

  constructor(
    private readonly video: HTMLVideoElement,
    private readonly callbacks: StreamPlayerCallbacks,
  ) {}

  start(cameraId: string): void {
    this.stopped = false;
    this.failed = false;
    this.video.muted = true;
    this.video.playsInline = true;
    this.video.addEventListener('playing', this.handlePlaying);

    if (typeof MediaSource === 'undefined') {
      this.fail('This browser does not support MP4 video streaming.');
      return;
    }

    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const socket = new WebSocket(
      `${protocol}//${window.location.host}/api/cameras/${encodeURIComponent(cameraId)}/stream`,
    );
    socket.binaryType = 'arraybuffer';

    socket.onmessage = (event) => this.handleMessage(event);
    socket.onerror = () =>
      this.fail(
        socket.readyState === WebSocket.CONNECTING
          ? 'Could not connect to the camera stream.'
          : 'The camera stream ended unexpectedly.',
      );
    socket.onclose = () => {
      if (!this.stopped && !this.failed) {
        this.fail('The camera stream ended unexpectedly.');
      }
    };

    this.socket = socket;
  }

  stop(): void {
    if (this.stopped) {
      return;
    }

    this.stopped = true;
    this.video.removeEventListener('playing', this.handlePlaying);

    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      socket.close(1000, 'Stopped by viewer');
    }

    try {
      this.sourceBuffer?.abort();
    } catch {
      // The buffer may already be detached.
    }

    if (this.mediaSource?.readyState === 'open') {
      try {
        this.mediaSource.endOfStream();
      } catch {
        // The media source may already be closed.
      }
    }

    this.video.pause();
    this.video.removeAttribute('src');
    this.video.load();

    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
    }

    this.objectUrl = null;
    this.mediaSource = null;
    this.sourceBuffer = null;
    this.queue = [];
    this.pendingMoof = null;
  }

  private readonly handlePlaying = () => {
    if (!this.stopped && !this.failed) {
      this.callbacks.onPlaying();
    }
  };

  private handleMessage(event: MessageEvent): void {
    if (this.stopped) {
      return;
    }

    if (typeof event.data === 'string') {
      try {
        const message = JSON.parse(event.data) as StreamMessage;
        if (message.type === 'ready' && message.codec) {
          this.setupMediaSource(message.codec);
        } else if (message.type === 'error') {
          this.fail(message.message ?? 'The camera stream failed.');
        }
      } catch {
        this.fail('The camera stream sent an invalid response.');
      }
      return;
    }

    if (!(event.data instanceof ArrayBuffer)) {
      this.fail('The camera stream sent unexpected data.');
      return;
    }

    this.handleSegment(event.data);
  }

  private handleSegment(segment: ArrayBuffer): void {
    const type = boxType(segment);

    if (type === 'moof') {
      if (this.pendingMoof) {
        this.queue.push(this.pendingMoof);
      }

      this.pendingMoof = segment;
      return;
    }

    if (type === 'mdat' && this.pendingMoof) {
      this.queue.push(concatBuffers(this.pendingMoof, segment));
      this.pendingMoof = null;
      this.pump();
      return;
    }

    if (this.pendingMoof) {
      this.queue.push(this.pendingMoof);
      this.pendingMoof = null;
    }

    this.queue.push(segment);
    this.pump();
  }

  private setupMediaSource(codec: string): void {
    if (this.mediaSource) {
      return;
    }

    const mime = `video/mp4; codecs="${codec}"`;
    if (!MediaSource.isTypeSupported(mime)) {
      this.fail(`This browser cannot play the camera codec (${codec}).`);
      return;
    }

    const mediaSource = new MediaSource();
    this.mediaSource = mediaSource;
    this.objectUrl = URL.createObjectURL(mediaSource);
    this.video.src = this.objectUrl;

    mediaSource.addEventListener(
      'sourceopen',
      () => {
        if (this.stopped) {
          return;
        }

        try {
          const sourceBuffer = mediaSource.addSourceBuffer(mime);
          sourceBuffer.addEventListener('updateend', this.handleUpdateEnd);
          sourceBuffer.addEventListener('error', () =>
            this.fail('Playback failed while buffering video.'),
          );
          this.sourceBuffer = sourceBuffer;
          void this.video.play().catch(() => {
            // Muted autoplay is normally allowed; the viewer already pressed Start.
          });
          this.pump();
        } catch {
          this.fail(`This browser cannot play the camera codec (${codec}).`);
        }
      },
      { once: true },
    );
  }

  private readonly handleUpdateEnd = () => {
    this.appending = false;
    this.trimBuffer();
    this.pump();
  };

  private pump(): void {
    if (this.stopped || this.failed) {
      return;
    }

    const sourceBuffer = this.sourceBuffer;
    if (!sourceBuffer || sourceBuffer.updating || this.appending) {
      return;
    }

    const segment = this.queue.shift();
    if (!segment) {
      return;
    }

    this.appending = true;

    try {
      sourceBuffer.appendBuffer(segment);
    } catch {
      this.appending = false;
      this.fail('Playback failed while buffering video.');
    }
  }

  private trimBuffer(): void {
    const sourceBuffer = this.sourceBuffer;
    if (!sourceBuffer || sourceBuffer.updating || this.appending) {
      return;
    }

    const buffered = sourceBuffer.buffered;
    if (buffered.length === 0) {
      return;
    }

    const start = buffered.start(0);
    const end = buffered.end(buffered.length - 1);
    if (end - start <= 90) {
      return;
    }

    this.appending = true;

    try {
      sourceBuffer.remove(start, end - 60);
    } catch {
      this.appending = false;
    }
  }

  private fail(message: string): void {
    if (this.stopped || this.failed) {
      return;
    }

    this.failed = true;
    this.video.removeEventListener('playing', this.handlePlaying);

    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      socket.close();
    }

    this.callbacks.onError(message);
  }
}
