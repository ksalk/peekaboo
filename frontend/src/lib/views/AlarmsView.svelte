<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import { SvelteDate, SvelteMap } from 'svelte/reactivity';
  import { api, errorMessage } from '../api';
  import DateTimeField from '../components/DateTimeField.svelte';
  import {
    formatDayLabel,
    formatTime,
    parseLocalDateTimeInput,
    quickRange,
    toExclusiveEnd,
    toLocalDateTimeInput,
    type QuickRangeName,
  } from '../date-time';
  import { formatDuration } from '../playback-utils';
  import type { AlarmRange, AlarmSearchResult, AlarmTarget, Camera } from '../types';

  let cameras = $state<Camera[]>([]);
  let camerasLoading = $state(true);
  let camerasError = $state<string | null>(null);
  let cameraId = $state('');
  let target = $state('');
  const initialRange = quickRange('today');
  let from = $state(toLocalDateTimeInput(initialRange.from));
  let to = $state(toLocalDateTimeInput(initialRange.to));
  let loading = $state(false);
  let error = $state<string | null>(null);
  let result = $state<AlarmSearchResult | null>(null);
  let controller: AbortController | null = null;
  let destroyed = false;

  const canSearch = $derived(!loading && !camerasLoading);
  const totalDuration = $derived(
    (result?.ranges ?? []).reduce(
      (sum, range) => sum + range.durationSeconds,
      0,
    ),
  );
  const groups = $derived(groupRanges(result?.ranges ?? []));

  onMount(() => {
    void load();
  });

  onDestroy(() => {
    destroyed = true;
    controller?.abort();
  });

  async function load() {
    camerasLoading = true;
    camerasError = null;

    try {
      cameras = await api.listCameras();
    } catch (cause) {
      camerasError = errorMessage(cause);
    } finally {
      camerasLoading = false;
    }

    if (cameras.length > 0) {
      await search();
    }
  }

  function resetResults() {
    controller?.abort();
    controller = null;
    loading = false;
    result = null;
    error = null;
  }

  async function search() {
    if (destroyed) {
      return;
    }

    const start = parseLocalDateTimeInput(from);
    let end = parseLocalDateTimeInput(to);

    if (start === null || end === null) {
      error =
        'Choose a valid alarm range. The selected local time may not exist on that date.';
      return;
    }

    end = toExclusiveEnd(end);

    if (end <= start) {
      error = 'The alarm range end must be after its start.';
      return;
    }

    if (end.getTime() - start.getTime() > 31 * 24 * 60 * 60 * 1000) {
      error = 'An alarm search can span at most 31 days.';
      return;
    }

    controller?.abort();
    const request = new AbortController();
    controller = request;
    loading = true;
    error = null;
    result = null;

    try {
      result = await api.listAlarms(
        {
          cameraId: cameraId || undefined,
          start: start.toISOString(),
          end: end.toISOString(),
          target: target || undefined,
        },
        request.signal,
      );
    } catch (cause) {
      if (!request.signal.aborted) {
        error = errorMessage(cause);
      }
    } finally {
      if (controller === request) {
        controller = null;
        loading = false;
      }
    }
  }

  function setQuickRange(name: QuickRangeName) {
    const range = quickRange(name);
    from = toLocalDateTimeInput(range.from);
    to = toLocalDateTimeInput(range.to);
    resetResults();
  }

  function targetLabel(value: AlarmTarget): string {
    return value === 'human' ? 'Person' : value === 'vehicle' ? 'Vehicle' : 'Motion';
  }

  function displayTarget(range: AlarmRange): string {
    if (range.target === 'unknown') {
      return range.targetLabel?.trim() || 'Motion';
    }

    return targetLabel(range.target);
  }

  function targetClass(value: AlarmTarget): string {
    return `alarm-target-${value}`;
  }

  function groupRanges(ranges: AlarmRange[]): {
    key: string;
    label: string;
    ranges: AlarmRange[];
  }[] {
    const grouped = new SvelteMap<string, AlarmRange[]>();

    for (const range of ranges) {
      const date = new SvelteDate(range.start);
      const key = `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`;
      const items = grouped.get(key);
      if (items) {
        items.push(range);
      } else {
        grouped.set(key, [range]);
      }
    }

    return [...grouped.entries()].map(([key, items]) => ({
      key,
      label: formatDayLabel(items[0]!.start),
      ranges: items.sort(
        (left, right) =>
          new SvelteDate(right.start).getTime() -
          new SvelteDate(left.start).getTime(),
      ),
    }));
  }
</script>

<section class="page">
  <div class="page-header">
    <div>
      <h1>Alarms</h1>
      <p class="muted">
        Search motion alarms recorded in the NVR log. These are fetched directly from
        the NVR; retention depends on its log settings.
      </p>
    </div>
  </div>

  {#if camerasLoading}
    <p class="muted">Loading cameras…</p>
  {:else if camerasError}
    <div class="alert" role="alert">{camerasError}</div>
    <div class="alarm-filter-actions">
      <button class="button" type="button" onclick={() => void load()}>
        Retry
      </button>
    </div>
  {:else if cameras.length === 0}
    <div class="empty-state">
      <h2>No cameras configured</h2>
      <p>Add an NVR and its cameras first, then come back to search motion alarms.</p>
      <a class="button primary" href="#/config">Add NVRs</a>
    </div>
  {:else}
    <form
      class="card alarm-filters"
      aria-busy={loading}
      onsubmit={(event) => {
        event.preventDefault();
        void search();
      }}
    >
      <label>
        Camera
        <select bind:value={cameraId} onchange={resetResults}>
          <option value="">All cameras</option>
          {#each cameras as camera (camera.id)}
            <option value={camera.id}>{camera.nvrName} · {camera.name}</option>
          {/each}
        </select>
      </label>

      <label>
        Target
        <select bind:value={target} onchange={resetResults}>
          <option value="">Any target</option>
          <option value="human">Person</option>
          <option value="vehicle">Vehicle</option>
          <option value="unknown">Motion (unclassified)</option>
        </select>
      </label>

      <DateTimeField
        label="From"
        bind:value={from}
        onchange={resetResults}
        required
      />

      <DateTimeField
        label="To"
        bind:value={to}
        onchange={resetResults}
        required
      />

      <div class="alarm-filter-actions">
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
          {loading ? 'Searching…' : 'Search alarms'}
        </button>
      </div>
    </form>

    {#if error}
      <div class="alert" role="alert">{error}</div>
    {/if}

    {#if result}
      {#if result.errors.length > 0}
        <div class="alert" role="status">
          {#each result.errors as item (item.nvrId)}
            <p><strong>{item.nvrName}</strong>: {item.message}</p>
          {/each}
        </div>
      {/if}

      <div class="alarm-summary" aria-live="polite">
        <p>
          <strong>{result.ranges.length}</strong>
          motion range{result.ranges.length === 1 ? '' : 's'} ·
          {formatDuration(totalDuration)} total
        </p>
        {#if result.truncated}
          <p class="hint">
            Showing a limited number of NVR log entries. Narrow the time range to see
            everything.
          </p>
        {/if}
      </div>

      {#if result.ranges.length === 0}
        <div class="empty-state">
          <h2>No motion alarms found</h2>
          <p>Try another camera, target, or time range.</p>
        </div>
      {:else}
        {#each groups as group (group.key)}
          <section class="alarm-group">
            <h2>{group.label}</h2>

            <div class="alarm-list">
              {#each group.ranges as range (`${range.cameraId}|${range.channelId}|${range.target}|${range.start}|${range.end}`)}
                <article class="alarm-row">
                  <div class="alarm-time">
                    <strong>{formatTime(range.start)} – {formatTime(range.end)}</strong>
                    <span class="muted">{formatDuration(range.durationSeconds)}</span>
                  </div>

                  <div class="alarm-meta">
                    <span class="badge {targetClass(range.target)}">
                      {displayTarget(range)}
                    </span>
                    <span>{range.cameraName}</span>
                    <span class="muted">{range.nvrName} · channel {range.channelId}</span>
                    {#if range.inferredStart}
                      <span class="badge">estimated start</span>
                    {/if}
                    {#if range.inferredEnd}
                      <span class="badge">estimated end</span>
                    {/if}
                  </div>
                </article>
              {/each}
            </div>
          </section>
        {/each}
      {/if}
    {/if}
  {/if}
</section>
