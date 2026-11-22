<script lang="ts">
  import { onDestroy, onMount, tick } from 'svelte';
  import { api, errorMessage } from '../api';
  import DateTimeField from '../components/DateTimeField.svelte';
  import PlaybackTimeline from '../components/PlaybackTimeline.svelte';
  import SearchStatus from '../components/SearchStatus.svelte';
  import {
    formatDate,
    formatTime,
    parseLocalDateTimeInput,
    quickRange,
    toExclusiveEnd,
    toLocalDateTimeInput,
    type QuickRangeName,
  } from '../date-time';
  import { formatDuration } from '../playback-utils';
  import { StreamPlayer } from '../stream-player';
  import type {
    AlarmRange,
    Camera,
    RecordingSearchResult,
    RecordingSegment,
  } from '../types';

  type PlayerStatus = 'idle' | 'connecting' | 'playing' | 'ended' | 'error';

  let {
    initialCameraId = null,
    initialStart = null,
    initialEnd = null,
    initialAt = null,
    initialAutoplay = false,
  }: {
    initialCameraId?: string | null;
    initialStart?: string | null;
    initialEnd?: string | null;
    initialAt?: string | null;
    initialAutoplay?: boolean;
  } = $props();

  const CAMERA_KEY = 'peekaboo.playback.camera';
  const initialRange = quickRange('today');

  let cameras = $state<Camera[]>([]);
  let camerasLoading = $state(true);
  let loadError = $state<string | null>(null);
  let loadNotice = $state<string | null>(null);
  let cameraId = $state('');
  let from = $state(toLocalDateTimeInput(initialRange.from));
  let to = $state(toLocalDateTimeInput(initialRange.to));
  let searching = $state(false);
  let searchError = $state<string | null>(null);
  let result = $state<RecordingSearchResult | null>(null);
  let motionRanges = $state<AlarmRange[]>([]);
  let motionError = $state<string | null>(null);
  let motionTruncated = $state(false);
  let motionWindow = $state<{ start: string; end: string } | null>(null);
  let motionLoading = $state(false);
  let selectedSegment = $state<RecordingSegment | null>(null);
  let playerStatus = $state<PlayerStatus>('idle');
  let playerError = $state('');
  let jumpOffset = $state(0);
  let copyState = $state<'copied' | 'failed' | null>(null);
  let videoElement = $state<HTMLVideoElement>();
  let playerCard = $state<HTMLElement>();
  let cameraSelect = $state<HTMLSelectElement>();
  let player: StreamPlayer | null = null;
  let searchController: AbortController | null = null;
  let copyTimer: number | null = null;
  let playRequest = 0;
  let autoPlayAttempted = false;
  let pendingAutoPlayAt: Date | null = null;
  let searchedFrom = $state('');
  let searchedTo = $state('');
  let hiddenResume = $state<{
    cameraId: string;
    segment: RecordingSegment;
    positionSeconds: number;
  } | null>(null);

  const canSearch = $derived(!searching && cameraId !== '' && !camerasLoading);
  const filtersDirty = $derived(
    (result !== null || motionWindow !== null) &&
      (from !== searchedFrom || to !== searchedTo),
  );
  const totalDuration = $derived(
    (result?.recordings ?? []).reduce(
      (sum, segment) => sum + segment.durationSeconds,
      0,
    ),
  );
  const totalMotionDuration = $derived(
    motionRanges.reduce((sum, range) => sum + range.durationSeconds, 0),
  );
  const jumpTarget = $derived(
    selectedSegment
      ? formatTime(
          new Date(
            new Date(selectedSegment.start).getTime() + jumpOffset * 1000,
          ).toISOString(),
        )
      : '',
  );

  onMount(() => {
    void loadCameras();
    document.addEventListener('visibilitychange', handleVisibility);

    return () => {
      document.removeEventListener('visibilitychange', handleVisibility);
      if (copyTimer !== null) {
        window.clearTimeout(copyTimer);
      }
    };
  });

  onDestroy(() => {
    searchController?.abort();
    hiddenResume = null;
    stopPlayback();
  });

  async function loadCameras() {
    camerasLoading = true;
    loadError = null;
    let runInitialSearch = false;

    try {
      cameras = await api.listCameras();
      const stored = readStoredCamera();
      const requested =
        initialCameraId && cameras.some((camera) => camera.id === initialCameraId)
          ? initialCameraId
          : null;

      if (initialCameraId && !requested) {
        loadNotice = 'The camera from the link was not found. Choose a camera and search again.';
      }

      cameraId =
        requested ??
        (cameras.some((camera) => camera.id === stored)
          ? stored!
          : (cameras[0]?.id ?? ''));

      if (requested) {
        storeCamera(requested);
      }

      const range = parseInitialRange(initialStart, initialEnd);
      if (range) {
        from = toLocalDateTimeInput(range.start);
        to = toLocalDateTimeInput(range.end);

        if (requested) {
          runInitialSearch = true;

          if (initialAutoplay) {
            const at = initialAt ? new Date(initialAt) : range.start;
            pendingAutoPlayAt = Number.isNaN(at.getTime()) ? range.start : at;
          }
        }
      }
    } catch (cause) {
      loadError = errorMessage(cause);
    } finally {
      camerasLoading = false;
    }

    if (runInitialSearch) {
      const notice = loadNotice;
      await search();
      if (result !== null || searchError !== null) {
        loadNotice = notice ?? loadNotice;
      }
      tryAutoPlay();
    }
  }

  function parseInitialRange(
    startValue: string | null,
    endValue: string | null,
  ): { start: Date; end: Date } | null {
    if (!startValue || !endValue) {
      return null;
    }

    const start = new Date(startValue);
    let end = new Date(endValue);

    if (
      Number.isNaN(start.getTime()) ||
      Number.isNaN(end.getTime()) ||
      end.getTime() <= start.getTime()
    ) {
      loadNotice = 'The playback link had an invalid time range.';
      return null;
    }

    const maximumMs = 31 * 24 * 60 * 60 * 1000;
    if (end.getTime() - start.getTime() > maximumMs) {
      end = new Date(start.getTime() + maximumMs - 1000);
      loadNotice = 'The linked playback range was longer than 31 days; showing 31 days.';
    }

    return { start, end };
  }

  function tryAutoPlay() {
    if (autoPlayAttempted || !result || !pendingAutoPlayAt) {
      return;
    }

    autoPlayAttempted = true;
    const at = pendingAutoPlayAt.getTime();
    const segments = result.recordings;
    const target =
      segments.find((segment) => {
        const start = new Date(segment.start).getTime();
        const end = new Date(segment.end).getTime();
        return start <= at && end > at;
      }) ??
      segments.find((segment) => new Date(segment.start).getTime() >= at) ??
      null;

    if (!target) {
      loadNotice = 'No recording covers the linked alarm time; showing the timeline instead.';
      return;
    }

    const targetStart = new Date(target.start).getTime();
    const start = Math.max(targetStart, at - 5000);
    const end = new Date(target.end).getTime();
    const adjusted = effectiveSegment({
      ...target,
      start: new Date(start).toISOString(),
      durationSeconds: Math.max(0, Math.round((end - start) / 1000)),
    });
    void play(adjusted);
  }

  function readStoredCamera(): string | null {
    try {
      return localStorage.getItem(CAMERA_KEY);
    } catch {
      return null;
    }
  }

  function storeCamera(id: string) {
    try {
      localStorage.setItem(CAMERA_KEY, id);
    } catch {
      // Local storage can be unavailable in private or restricted contexts.
    }
  }

  function handleCameraChange(event: Event) {
    const next = (event.currentTarget as HTMLSelectElement).value;
    if (next !== cameraId) {
      cameraId = next;
    }

    if (!cameraId) {
      return;
    }

    storeCamera(cameraId);
    resetResults();
  }

  function handleVisibility() {
    if (document.hidden) {
      rememberPlaybackForResume();
      stopPlayback();
      return;
    }

    resumeHiddenPlayback();
  }

  function rememberPlaybackForResume() {
    if (
      !cameraId ||
      !selectedSegment ||
      (playerStatus !== 'connecting' && playerStatus !== 'playing')
    ) {
      return;
    }

    const position = videoElement?.currentTime ?? 0;
    hiddenResume = {
      cameraId,
      segment: selectedSegment,
      positionSeconds: Number.isFinite(position) ? Math.max(0, position) : 0,
    };
  }

  function resumeHiddenPlayback() {
    const pending = hiddenResume;
    hiddenResume = null;

    if (!pending || pending.cameraId !== cameraId) {
      return;
    }

    const startMs =
      new Date(pending.segment.start).getTime() + pending.positionSeconds * 1000;
    const endMs = new Date(pending.segment.end).getTime();

    if (!Number.isFinite(startMs) || !Number.isFinite(endMs) || startMs >= endMs) {
      return;
    }

    const adjusted: RecordingSegment = {
      ...pending.segment,
      start: new Date(startMs).toISOString(),
      durationSeconds: Math.max(0, Math.round((endMs - startMs) / 1000)),
    };
    void play(adjusted);
  }

  function resetResults() {
    searchController?.abort();
    searchController = null;
    searching = false;
    hiddenResume = null;
    stopPlayback();
    result = null;
    motionRanges = [];
    motionError = null;
    motionTruncated = false;
    motionWindow = null;
    motionLoading = false;
    searchError = null;
    copyState = null;
    loadNotice = null;
  }

  function setQuickRange(name: QuickRangeName) {
    const range = quickRange(name);
    from = toLocalDateTimeInput(range.from);
    to = toLocalDateTimeInput(range.to);
    searchError = null;
    loadNotice = null;
  }

  async function search() {
    if (!canSearch) {
      return;
    }

    const start = parseLocalDateTimeInput(from);
    let end = parseLocalDateTimeInput(to);

    if (start === null || end === null) {
      searchError =
        'Choose a valid date and time range. The selected local time may not exist on that date.';
      return;
    }

    end = toExclusiveEnd(end);

    if (end.getTime() <= start.getTime()) {
      searchError = 'End time must be after start time.';
      return;
    }

    if (end.getTime() - start.getTime() > 31 * 24 * 60 * 60 * 1000) {
      searchError = 'A recording search can span at most 31 days.';
      return;
    }

    searchedFrom = from;
    searchedTo = to;
    loadNotice = null;
    hiddenResume = null;
    stopPlayback();
    result = null;
    motionRanges = [];
    motionError = null;
    motionTruncated = false;
    motionWindow = null;
    motionLoading = true;
    searchError = null;
    copyState = null;
    searching = true;

    searchController?.abort();
    const controller = new AbortController();
    searchController = controller;

    try {
      const recordingsRequest = api
        .listRecordings(
          cameraId,
          start.toISOString(),
          end.toISOString(),
          controller.signal,
        )
        .then(
          (value) => {
            if (!controller.signal.aborted) {
              result = value;
            }
          },
          (cause) => {
            if (!controller.signal.aborted) {
              searchError = errorMessage(cause);
            }
          },
        );

      const alarmsRequest = api
        .listAlarms(
          {
            cameraId,
            start: start.toISOString(),
            end: end.toISOString(),
          },
          controller.signal,
        )
        .then(
          (value) => {
            if (!controller.signal.aborted) {
              motionRanges = value.ranges;
              motionTruncated = value.truncated;
              motionWindow = { start: value.start, end: value.end };
              motionLoading = false;
            }
          },
          (cause) => {
            if (!controller.signal.aborted) {
              motionRanges = [];
              motionTruncated = false;
              motionWindow = null;
              motionError = errorMessage(cause);
              motionLoading = false;
            }
          },
        );

      await Promise.all([recordingsRequest, alarmsRequest]);
    } finally {
      if (searchController === controller) {
        searchController = null;
        searching = false;
        motionLoading = false;
      }
    }
  }

  function effectiveSegment(segment: RecordingSegment): RecordingSegment {
    if (!result) {
      return segment;
    }

    const start = Math.max(
      new Date(segment.start).getTime(),
      new Date(result.start).getTime(),
    );
    const end = Math.min(
      new Date(segment.end).getTime(),
      new Date(result.end).getTime(),
    );

    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) {
      return segment;
    }

    return {
      ...segment,
      start: new Date(start).toISOString(),
      end: new Date(end).toISOString(),
      durationSeconds: Math.max(0, Math.round((end - start) / 1000)),
    };
  }

  async function play(segment: RecordingSegment) {
    if (!cameraId) {
      return;
    }

    hiddenResume = null;
    const effective = effectiveSegment(segment);
    stopPlayback();
    copyState = null;
    const request = ++playRequest;
    selectedSegment = effective;
    jumpOffset = 0;
    playerStatus = 'connecting';
    playerError = '';
    await tick();

    if (request !== playRequest || !videoElement) {
      if (request === playRequest) {
        playerStatus = 'error';
        playerError = 'The video player is not available.';
      }

      return;
    }

    playerCard?.focus();

    const instance = new StreamPlayer(videoElement, {
      onPlaying: () => {
        if (player === instance) {
          playerStatus = 'playing';
        }
      },
      onError: (message) => {
        if (player === instance) {
          playerStatus = 'error';
          playerError = message;
        }
      },
      onEnded: () => {
        if (player === instance) {
          playerStatus = 'ended';
        }
      },
    });

    player = instance;
    instance.startPlayback(cameraId, effective.start, effective.end);
  }

  function stopPlayback() {
    playRequest += 1;
    player?.stop();
    player = null;
    selectedSegment = null;
    playerStatus = 'idle';
    playerError = '';
    copyState = null;
  }

  function closePlayer() {
    hiddenResume = null;
    stopPlayback();
    cameraSelect?.focus();
  }

  async function copyUrl() {
    if (!selectedSegment?.playbackUrl) {
      return;
    }

    try {
      await writeClipboard(selectedSegment.playbackUrl);
      showCopyState('copied');
    } catch {
      showCopyState('failed');
    }
  }

  async function writeClipboard(value: string) {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(value);
      return;
    }

    const restoreFocus =
      document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const textarea = document.createElement('textarea');
    textarea.value = value;
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    textarea.style.pointerEvents = 'none';
    document.body.appendChild(textarea);

    try {
      textarea.focus();
      textarea.select();

      if (!document.execCommand('copy')) {
        throw new Error('Clipboard copy failed.');
      }
    } finally {
      textarea.remove();
      restoreFocus?.focus();
    }
  }

  function showCopyState(status: 'copied' | 'failed') {
    copyState = status;

    if (copyTimer !== null) {
      window.clearTimeout(copyTimer);
    }

    copyTimer = window.setTimeout(() => {
      copyState = null;
      copyTimer = null;
    }, 2000);
  }

  function jumpToOffset() {
    if (!selectedSegment) {
      return;
    }

    const start = new Date(
      new Date(selectedSegment.start).getTime() + jumpOffset * 1000,
    );
    const end = new Date(selectedSegment.end);

    if (start >= end) {
      return;
    }

    const adjusted: RecordingSegment = {
      ...selectedSegment,
      start: start.toISOString(),
      durationSeconds: Math.max(
        0,
        Math.round((end.getTime() - start.getTime()) / 1000),
      ),
    };
    void play(adjusted);
  }

</script>

<section class="page">
  <div class="page-header">
    <div>
      <h1>Playback</h1>
      <p class="muted">Search recordings on the NVR and watch them only while they are open.</p>
    </div>
  </div>

  {#if loadError}
    <div class="alert" role="alert">{loadError}</div>
  {/if}

  {#if loadNotice}
    <div class="alert" role="status">{loadNotice}</div>
  {/if}

  {#if camerasLoading}
    <p class="muted">Loading cameras…</p>
  {:else if cameras.length === 0}
    <div class="empty-state">
      <h2>No cameras configured</h2>
      <p>Add an NVR and its cameras first, then come back to search their recordings.</p>
      <a class="button primary" href="#/config">Add NVRs</a>
    </div>
  {:else}
    <form
      class="card playback-filters"
      onsubmit={(event) => {
        event.preventDefault();
        void search();
      }}
    >
      <label>
        Camera
        <select bind:this={cameraSelect} bind:value={cameraId} onchange={handleCameraChange}>
          {#each cameras as camera (camera.id)}
            <option value={camera.id}>{camera.nvrName} · {camera.name}</option>
          {/each}
        </select>
      </label>

      <DateTimeField
        label="From"
        bind:value={from}
        onchange={() => {
          searchError = null;
          loadNotice = null;
        }}
        required
      />

      <DateTimeField
        label="To"
        bind:value={to}
        onchange={() => {
          searchError = null;
          loadNotice = null;
        }}
        required
      />

      <div class="playback-filter-actions">
        <button class="button small ghost" type="button" onclick={() => setQuickRange('today')}>
          Today
        </button>
        <button class="button small ghost" type="button" onclick={() => setQuickRange('yesterday')}>
          Yesterday
        </button>
        <button class="button small ghost" type="button" onclick={() => setQuickRange('last7days')}>
          Last 7 days
        </button>
        <button class="button primary" type="submit" disabled={!canSearch}>
          {searching ? 'Searching…' : 'Search recordings'}
        </button>
      </div>
    </form>

    {#if filtersDirty}
      <p class="hint">
        The time range changed. Search again to apply it; the current results are
        from the previous search.
      </p>
    {/if}

    {#if searchError}
      <div class="alert" role="alert">{searchError}</div>
    {/if}

    {#if motionError}
      <div class="alert" role="status">
        Motion ranges could not be loaded: {motionError}
      </div>
    {/if}

    {#if searching}
      <SearchStatus
        label="Searching recordings…"
        detail="Looking up recording coverage and motion events."
      />
    {/if}

    {#if selectedSegment}
      <article class="card playback-player" bind:this={playerCard} tabindex="-1">
        <header class="playback-player-header">
          <div>
            <h2>{result?.cameraName ?? cameras.find((camera) => camera.id === cameraId)?.name}</h2>
            <p class="muted">
              {formatDate(selectedSegment.start)} ·
              {formatTime(selectedSegment.start)} – {formatTime(selectedSegment.end)}
            </p>
          </div>

          <div class="actions">
            {#if selectedSegment.playbackUrl}
              <button class="button small" type="button" onclick={() => void copyUrl()}>
                {copyState === 'copied'
                  ? 'Copied'
                  : copyState === 'failed'
                    ? 'Copy failed'
                    : 'Copy stream URL'}
              </button>
            {/if}
            {#if playerStatus === 'ended'}
              <button
                class="button small"
                onclick={() => {
                  if (selectedSegment) {
                    void play(selectedSegment);
                  }
                }}
              >
                Play again
              </button>
            {/if}
            <button class="button small ghost" onclick={closePlayer}>Close</button>
          </div>
        </header>

        <div class="playback-video-wrap" aria-live="polite">
          <video
            bind:this={videoElement}
            class="playback-video"
            controls
            muted
            playsinline
            aria-label="Recording playback"
          ></video>

          {#if playerStatus === 'connecting'}
            <div class="playback-overlay">
              <span class="spinner" aria-hidden="true"></span>
              <span>Starting playback…</span>
            </div>
          {:else if playerStatus === 'error'}
            <div class="playback-overlay error">
              <span>Playback failed</span>
              <span class="playback-overlay-detail">{playerError}</span>
            </div>
          {:else if playerStatus === 'ended'}
            <div class="playback-overlay">
              <span>Playback ended</span>
            </div>
          {/if}
        </div>

        <div class="playback-jump">
          <label>
            Jump to
            <input
              type="range"
              min="0"
              max={Math.max(1, selectedSegment.durationSeconds)}
              step="1"
              bind:value={jumpOffset}
            />
          </label>
          <span class="muted">{jumpTarget}</span>
          <button
            class="button small"
            type="button"
            onclick={jumpToOffset}
            disabled={playerStatus === 'connecting'}
          >
            Play from here
          </button>
        </div>
        <p class="hint">
          SDK playback can be slower than real time. Jumping restarts the NVR stream
          at the selected time; the native video controls seek within the buffered
          part.
        </p>
      </article>
    {/if}

    {#if result || motionWindow}
      <div class="playback-summary">
        <p>
          {#if result}
            <strong>{result.recordings.length}</strong>
            recording{result.recordings.length === 1 ? '' : 's'} ·
            {formatDuration(totalDuration)} total
          {/if}
          {#if result && motionRanges.length > 0}·{/if}
          {#if motionRanges.length > 0}
            <strong>{motionRanges.length}</strong>
            motion range{motionRanges.length === 1 ? '' : 's'} ·
            {formatDuration(totalMotionDuration)}
          {/if}
        </p>
        {#if result?.truncated}
          <p class="hint">
            Showing a limited number of segments. Narrow the time range to see everything.
          </p>
        {/if}
        {#if motionTruncated}
          <p class="hint">
            Showing a limited number of motion ranges. Narrow the time range to see everything.
          </p>
        {/if}
      </div>

      <PlaybackTimeline
        start={result?.start ?? motionWindow!.start}
        end={result?.end ?? motionWindow!.end}
        recordings={result?.recordings ?? []}
        motionRanges={motionRanges}
        motionTruncated={motionTruncated}
        motionUnavailable={motionError !== null}
        motionPending={motionLoading}
        selectedSegment={selectedSegment}
        truncated={result?.truncated ?? false}
        onPlay={(segment) => void play(segment)}
      />

      <p class="hint">
        Hover or focus a recording on the timeline for its time range, duration, and
        codec. Click it to play.
      </p>

      {#if result && result.recordings.length === 0}
        <div class="empty-state">
          <h2>No recordings found</h2>
          <p>
            {motionRanges.length > 0
              ? 'Motion alarms are shown on the timeline above, but there are no recording segments to play.'
              : 'Try another date or a wider time range.'}
          </p>
        </div>
      {/if}
    {/if}
  {/if}
</section>
