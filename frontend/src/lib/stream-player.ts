import type { CameraStream } from './types';

interface StreamPlayerCallbacks {
  onPlaying: () => void;
  onError: (message: string) => void;
  onEnded?: () => void;
  onAudioAvailable?: (available: boolean) => void;
}

interface StreamMessage {
  type?: string;
  codec?: string;
  audioCodec?: string | null;
  message?: string;
}

const BOX_HEADER_LENGTH = 8;

// Fragments are emitted at video keyframes, so a healthy source's gap can equal
// its GOP. Keep this below the server's 30s live idle watchdog while still
// covering long Hikvision GOPs.
const MEDIA_STALL_TIMEOUT_MS = 20_000;

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
  private hasAudio = false;
  private audioRequested = true;
  private buildUrl: ((audio: boolean) => string) | null = null;
  private mediaWatchdog: number | null = null;
  private queue: ArrayBuffer[] = [];
  private pendingMoof: ArrayBuffer | null = null;
  private appending = false;
  private stopped = false;
  private failed = false;

  constructor(
    private readonly video: HTMLVideoElement,
    private readonly callbacks: StreamPlayerCallbacks,
  ) {}

  start(cameraId: string, stream: CameraStream): void {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    this.buildUrl = (audio) =>
      `${protocol}//${window.location.host}/api/cameras/${encodeURIComponent(cameraId)}/stream` +
      `?stream=${encodeURIComponent(stream)}${audio ? '' : '&audio=0'}`;
    this.begin();
  }

  startPlayback(cameraId: string, start: string, end: string): void {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    this.buildUrl = (audio) =>
      `${protocol}//${window.location.host}/api/cameras/${encodeURIComponent(cameraId)}/recordings/stream` +
      `?start=${encodeURIComponent(start)}&end=${encodeURIComponent(end)}` +
      `${audio ? '' : '&audio=0'}`;
    this.begin();
  }

  private begin(): void {
    this.audioRequested = true;
    const url = this.buildUrl?.(true);
    if (!url) {
      this.fail('The stream URL could not be built.');
      return;
    }

    this.connect(url);
  }

  setAudioEnabled(enabled: boolean): void {
    if (this.stopped || this.failed) {
      return;
    }

    const next = enabled && this.hasAudio;
    this.video.muted = !next;

    if (next && this.video.paused) {
      void this.video.play().catch(() => {
        // The viewer can press play again if the browser rejects the resume.
      });
    }
  }

  private connect(url: string): void {
    this.clearMediaWatchdog();
    this.stopped = false;
    this.failed = false;
    this.hasAudio = false;
    this.video.muted = true;
    this.video.playsInline = true;
    this.video.removeEventListener('playing', this.handlePlaying);
    this.video.addEventListener('playing', this.handlePlaying);

    if (typeof MediaSource === 'undefined') {
      this.fail('This browser does not support MP4 video streaming.');
      return;
    }

    const socket = new WebSocket(url);
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
    this.stopped = true;
    this.clearMediaWatchdog();
    this.video.removeEventListener('playing', this.handlePlaying);

    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      socket.close(1000, 'Stopped by viewer');
    }

    this.detachMediaSource();
    this.video.muted = true;
    this.audioRequested = true;
    this.buildUrl = null;
  }

  private fallbackWithoutAudio(): void {
    if (this.stopped || this.failed || !this.audioRequested) {
      return;
    }

    this.audioRequested = false;
    this.clearMediaWatchdog();
    this.detachMediaSource();
    this.callbacks.onAudioAvailable?.(false);

    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      socket.close(1000, 'Audio unavailable');
    }

    const url = this.buildUrl?.(false);
    if (!url) {
      this.fail('The stream could not be restarted without audio.');
      return;
    }

    this.connect(url);
  }

  private detachMediaSource(): void {
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
    this.hasAudio = false;
    this.queue = [];
    this.pendingMoof = null;
    this.appending = false;
  }

  private armMediaWatchdog(): void {
    this.clearMediaWatchdog();
    if (!this.hasAudio || this.stopped || this.failed) {
      return;
    }

    this.mediaWatchdog = window.setTimeout(() => {
      this.mediaWatchdog = null;
      this.fallbackWithoutAudio();
    }, MEDIA_STALL_TIMEOUT_MS);
  }

  private resetMediaWatchdog(): void {
    if (this.hasAudio) {
      this.armMediaWatchdog();
    }
  }

  private clearMediaWatchdog(): void {
    if (this.mediaWatchdog !== null) {
      window.clearTimeout(this.mediaWatchdog);
      this.mediaWatchdog = null;
    }
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
          this.setupMediaSource(message.codec, message.audioCodec ?? null);
        } else if (message.type === 'error') {
          this.fail(message.message ?? 'The camera stream failed.');
        } else if (message.type === 'ended') {
          this.finish();
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
    if (type === 'moof' || type === 'mdat') {
      this.resetMediaWatchdog();
    }

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

  private setupMediaSource(codec: string, audioCodec: string | null): void {
    if (this.mediaSource) {
      return;
    }

    if (!this.audioRequested) {
      audioCodec = null;
    }

    const codecs = audioCodec ? `${codec},${audioCodec}` : codec;
    const mime = `video/mp4; codecs="${codecs}"`;
    if (!MediaSource.isTypeSupported(mime)) {
      if (audioCodec) {
        this.fallbackWithoutAudio();
        return;
      }

      this.fail(`This browser cannot play the camera codec (${codecs}).`);
      return;
    }

    this.hasAudio = audioCodec !== null;
    this.callbacks.onAudioAvailable?.(this.hasAudio);
    if (this.hasAudio) {
      this.armMediaWatchdog();
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
            this.fail('Playback failed while buffering the stream.'),
          );
          this.sourceBuffer = sourceBuffer;
          void this.video.play().catch(() => {
            // Muted autoplay is normally allowed; the viewer already pressed Start.
          });
          this.pump();
        } catch {
          this.fail(`This browser cannot play the camera codec (${codecs}).`);
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
    if (this.stopped || this.failed) {
      return;
    }

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

  private finish(): void {
    if (this.stopped || this.failed) {
      return;
    }

    // Keep the MediaSource and <video> attached so the last buffered frames can
    // finish playing. stop() performs the final cleanup.
    this.stopped = true;
    this.clearMediaWatchdog();
    this.video.removeEventListener('playing', this.handlePlaying);

    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onerror = null;
      socket.onmessage = null;
      socket.close(1000, 'Playback ended');
    }

    if (this.mediaSource?.readyState === 'open') {
      try {
        this.mediaSource.endOfStream();
      } catch {
        // The media source may already be closed.
      }
    }

    this.callbacks.onEnded?.();
  }

  private fail(message: string): void {
    if (this.stopped || this.failed) {
      return;
    }

    this.failed = true;
    this.clearMediaWatchdog();
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
