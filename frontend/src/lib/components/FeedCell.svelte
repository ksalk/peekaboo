<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import { StreamPlayer } from '../stream-player';
  import type { Camera, CameraStream } from '../types';

  let {
    camera,
    stopSignal = 0,
    onChoose,
  }: {
    camera: Camera | null;
    stopSignal?: number;
    onChoose: () => void;
  } = $props();

  let videoElement = $state<HTMLVideoElement>();
  let status = $state<'idle' | 'connecting' | 'playing' | 'error'>('idle');
  let errorMessage = $state('');
  let player: StreamPlayer | null = null;
  let activeCameraId: string | null = null;
  let seenStopSignal = 0;
  let stream = $state<CameraStream>('sub');

  $effect(() => {
    if (stopSignal !== seenStopSignal) {
      seenStopSignal = stopSignal;
      stop();
    }
  });

  $effect(() => {
    const nextCameraId = camera?.id ?? null;

    if (activeCameraId !== null && activeCameraId !== nextCameraId) {
      stop();
    }

    activeCameraId = nextCameraId;
  });

  onMount(() => {
    const handleVisibility = () => {
      if (document.hidden) {
        stop();
      }
    };

    document.addEventListener('visibilitychange', handleVisibility);
    return () => document.removeEventListener('visibilitychange', handleVisibility);
  });

  onDestroy(stop);

  function start() {
    if (!camera || !videoElement) {
      return;
    }

    stop();
    status = 'connecting';
    errorMessage = '';
    activeCameraId = camera.id;

    const instance = new StreamPlayer(videoElement, {
      onPlaying: () => {
        if (player === instance) {
          status = 'playing';
        }
      },
      onError: (message) => {
        if (player === instance) {
          status = 'error';
          errorMessage = message;
        }
      },
    });

    player = instance;
    instance.start(camera.id, stream);
  }

  function selectStream(next: CameraStream) {
    if (stream === next) {
      return;
    }

    const wasActive = status === 'connecting' || status === 'playing';
    stream = next;

    if (wasActive) {
      start();
    }
  }

  function stop() {
    player?.stop();
    player = null;
    status = 'idle';
    errorMessage = '';
    activeCameraId = camera?.id ?? null;
  }
</script>

<article class="feed-cell" class:live={status === 'playing'}>
  <video
    bind:this={videoElement}
    class="feed-video"
    muted
    playsinline
    aria-label={camera ? `Camera feed: ${camera.name}` : undefined}
  ></video>

  {#if camera}
    <div class="feed-overlay">
      <header class="feed-toolbar">
        <span class="feed-name" title={camera.name}>{camera.name}</span>

        <div class="feed-actions">
          <div
            class="stream-toggle"
            role="group"
            aria-label={`${camera.name} stream quality`}
          >
            <button
              type="button"
              class:active={stream === 'sub'}
              aria-pressed={stream === 'sub'}
              title="Substream"
              onclick={() => selectStream('sub')}
            >
              Sub
            </button>
            <button
              type="button"
              class:active={stream === 'main'}
              aria-pressed={stream === 'main'}
              title="Main stream"
              onclick={() => selectStream('main')}
            >
              Main
            </button>
          </div>

          {#if status === 'playing'}
            <span class="live-badge"><span class="live-dot" aria-hidden="true"></span>Live</span>
          {/if}

          {#if status === 'connecting' || status === 'playing'}
            <button class="button small danger" onclick={stop}>Stop</button>
          {:else}
            <button class="button small ghost" onclick={onChoose}>Change</button>
            <button class="button small primary" onclick={start}>
              {status === 'error' ? 'Retry' : 'Start'}
            </button>
          {/if}
        </div>
      </header>

      {#if status === 'idle'}
        <button class="feed-center" onclick={onChoose}>
          <span class="feed-hint">Stream stopped</span>
          <span class="feed-subhint">Click to change camera, or press Start to view</span>
        </button>
      {:else if status === 'connecting'}
        <div class="feed-center">
          <span class="spinner" aria-hidden="true"></span>
          <span class="feed-hint">Starting stream…</span>
        </div>
      {:else if status === 'error'}
        <div class="feed-center feed-error" role="alert">
          <span class="feed-hint">Stream failed</span>
          <span class="feed-subhint">{errorMessage}</span>
        </div>
      {/if}
    </div>
  {:else}
    <button class="feed-center empty" onclick={onChoose}>
      <span class="feed-plus" aria-hidden="true">+</span>
      <span class="feed-hint">Choose camera</span>
    </button>
  {/if}
</article>
