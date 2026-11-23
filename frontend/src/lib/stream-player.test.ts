import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StreamPlayer } from './stream-player';

class FakeSourceBuffer {
  updating = false;
  buffered = { length: 0, start: () => 0, end: () => 0 } as TimeRanges;
  addEventListener = vi.fn();
  appendBuffer = vi.fn();
  remove = vi.fn();
  abort = vi.fn();
}

class FakeMediaSource {
  static isTypeSupported = vi.fn(() => true);
  readyState = 'closed';
  sourceBuffer = new FakeSourceBuffer();
  private sourceOpenListener: (() => void) | null = null;

  addEventListener(type: string, listener: () => void): void {
    if (type === 'sourceopen') {
      this.sourceOpenListener = listener;
      this.readyState = 'open';
      queueMicrotask(listener);
    }
  }

  addSourceBuffer(): SourceBuffer {
    return this.sourceBuffer as unknown as SourceBuffer;
  }

  endOfStream(): void {
    this.readyState = 'ended';
  }
}

class FakeWebSocket {
  static CONNECTING = 0;
  static instances: FakeWebSocket[] = [];
  binaryType = 'blob';
  readyState = 0;
  onopen: (() => void) | null = null;
  onmessage: ((event: MessageEvent) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: (() => void) | null = null;
  close = vi.fn();

  constructor(public readonly url: string) {
    FakeWebSocket.instances.push(this);
  }

  emit(data: string | ArrayBuffer): void {
    this.onmessage?.({ data } as MessageEvent);
  }
}

function createVideo(): HTMLVideoElement {
  return {
    muted: false,
    playsInline: false,
    paused: false,
    src: '',
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    play: vi.fn(() => Promise.resolve()),
    pause: vi.fn(),
    removeAttribute: vi.fn(),
    load: vi.fn(),
  } as unknown as HTMLVideoElement;
}

function box(type: string): ArrayBuffer {
  const data = new Uint8Array(8);
  data.set([...type].map((character) => character.charCodeAt(0)), 4);
  return data.buffer;
}

describe('StreamPlayer audio fallback', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    FakeWebSocket.instances = [];
    FakeMediaSource.isTypeSupported.mockReset().mockReturnValue(true);

    vi.stubGlobal('window', {
      location: { protocol: 'http:', host: 'localhost:8080' },
      setTimeout: (handler: TimerHandler, timeout?: number) => setTimeout(handler, timeout),
      clearTimeout: (id: number) => clearTimeout(id),
    });
    vi.stubGlobal('URL', {
      createObjectURL: vi.fn(() => 'blob:test'),
      revokeObjectURL: vi.fn(),
    });
    vi.stubGlobal('MediaSource', FakeMediaSource);
    vi.stubGlobal('WebSocket', FakeWebSocket);
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('reconnects with audio=0 when audio never produces media', async () => {
    const onAudioAvailable = vi.fn();
    const onError = vi.fn();
    const player = new StreamPlayer(createVideo(), {
      onPlaying: vi.fn(),
      onError,
      onAudioAvailable,
    });

    player.start('camera-1', 'sub');
    expect(FakeWebSocket.instances).toHaveLength(1);
    expect(FakeWebSocket.instances[0]!.url).toContain('stream=sub');
    expect(FakeWebSocket.instances[0]!.url).not.toContain('audio=0');

    FakeWebSocket.instances[0]!.emit(
      JSON.stringify({
        type: 'ready',
        codec: 'avc1.64001f',
        audioCodec: 'mp4a.40.2',
      }),
    );
    await Promise.resolve();
    expect(onAudioAvailable).toHaveBeenLastCalledWith(true);

    await vi.advanceTimersByTimeAsync(20_000);

    expect(onError).not.toHaveBeenCalled();
    expect(onAudioAvailable).toHaveBeenLastCalledWith(false);
    expect(FakeWebSocket.instances).toHaveLength(2);
    expect(FakeWebSocket.instances[1]!.url).toContain('&audio=0');
  });

  it('keeps audio when media continues to arrive', async () => {
    const onAudioAvailable = vi.fn();
    const player = new StreamPlayer(createVideo(), {
      onPlaying: vi.fn(),
      onError: vi.fn(),
      onAudioAvailable,
    });

    player.start('camera-1', 'sub');
    const socket = FakeWebSocket.instances[0]!;
    socket.emit(
      JSON.stringify({
        type: 'ready',
        codec: 'avc1.64001f',
        audioCodec: 'mp4a.40.2',
      }),
    );
    await Promise.resolve();

    socket.emit(box('moof'));
    await vi.advanceTimersByTimeAsync(19_000);
    socket.emit(box('mdat'));
    await vi.advanceTimersByTimeAsync(19_000);

    expect(FakeWebSocket.instances).toHaveLength(1);
    expect(onAudioAvailable).toHaveBeenLastCalledWith(true);

    await vi.advanceTimersByTimeAsync(2_000);
    expect(FakeWebSocket.instances).toHaveLength(2);
  });

  it('falls back when the browser rejects the combined codec', async () => {
    FakeMediaSource.isTypeSupported.mockReturnValueOnce(false);
    const onAudioAvailable = vi.fn();
    const onError = vi.fn();
    const player = new StreamPlayer(createVideo(), {
      onPlaying: vi.fn(),
      onError,
      onAudioAvailable,
    });

    player.start('camera-1', 'sub');
    FakeWebSocket.instances[0]!.emit(
      JSON.stringify({
        type: 'ready',
        codec: 'avc1.64001f',
        audioCodec: 'mp4a.40.2',
      }),
    );
    await Promise.resolve();

    expect(onError).not.toHaveBeenCalled();
    expect(onAudioAvailable).toHaveBeenLastCalledWith(false);
    expect(FakeWebSocket.instances).toHaveLength(2);
    expect(FakeWebSocket.instances[1]!.url).toContain('&audio=0');
  });
});
