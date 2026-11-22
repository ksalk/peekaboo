<script lang="ts">
  import { onDestroy, onMount } from 'svelte';
  import { SvelteDate, SvelteMap } from 'svelte/reactivity';
  import { api, errorMessage } from '../api';
  import { formatDuration } from '../playback-utils';
  import type { AlarmRange, AlarmSearchResult, AlarmTarget, Camera } from '../types';

  const clockFormat = new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
  const dayFormat = new Intl.DateTimeFormat(undefined, {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  });

  let cameras = $state<Camera[]>([]);
  let camerasLoading = $state(true);
  let camerasError = $state<string | null>(null);
  let cameraId = $state('');
  let target = $state('');
  let from = $state(formatInput(new Date(Date.now() - 24 * 60 * 60 * 1000)));
  let to = $state(formatInput(new Date()));
  let loading = $state(false);
  let error = $state<string | null>(null);
  let result = $state<AlarmSearchResult | null>(null);
  let controller: AbortController | null = null;
  let destroyed = false;

  const canSearch = $derived(!loading && from !== '' && to !== '');
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

    const start = parseInput(from);
    const end = parseInput(to);

    if (start === null || end === null) {
      error =
        'Choose a valid alarm range. The selected local time may not exist on that date.';
      return;
    }

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

  function setToday() {
    const now = new SvelteDate();
    const start = new SvelteDate(now);
    start.setHours(0, 0, 0, 0);
    from = formatInput(start);
    to = formatInput(now);
    resetResults();
  }

  function setYesterday() {
    const start = new SvelteDate();
    start.setDate(start.getDate() - 1);
    start.setHours(0, 0, 0, 0);
    const end = new SvelteDate(start);
    end.setDate(end.getDate() + 1);
    from = formatInput(start);
    to = formatInput(end);
    resetResults();
  }

  function setLastSevenDays() {
    const now = new SvelteDate();
    from = formatInput(new SvelteDate(now.getTime() - 7 * 24 * 60 * 60 * 1000));
    to = formatInput(now);
    resetResults();
  }

  function formatInput(value: Date): string {
    const pad = (part: number) => part.toString().padStart(2, '0');
    return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}` +
      `T${pad(value.getHours())}:${pad(value.getMinutes())}:${pad(value.getSeconds())}`;
  }

  function parseInput(value: string): Date | null {
    if (!value) {
      return null;
    }

    const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.\d{1,3})?)?$/.exec(
      value,
    );
    if (!match) {
      return null;
    }

    const year = Number(match[1]);
    const month = Number(match[2]);
    const day = Number(match[3]);
    const hours = Number(match[4]);
    const minutes = Number(match[5]);
    const seconds = Number(match[6] ?? '0');
    const parsed = new SvelteDate(
      year,
      month - 1,
      day,
      hours,
      minutes,
      seconds,
      0,
    );

    if (
      Number.isNaN(parsed.getTime()) ||
      parsed.getFullYear() !== year ||
      parsed.getMonth() !== month - 1 ||
      parsed.getDate() !== day ||
      parsed.getHours() !== hours ||
      parsed.getMinutes() !== minutes ||
      parsed.getSeconds() !== seconds
    ) {
      return null;
    }

    return parsed;
  }

  function formatClock(iso: string): string {
    return clockFormat.format(new SvelteDate(iso));
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
      label: groupLabel(key, items[0]!.start),
      ranges: items.sort(
        (left, right) =>
          new SvelteDate(right.start).getTime() -
          new SvelteDate(left.start).getTime(),
      ),
    }));
  }

  function groupLabel(key: string, iso: string): string {
    const today = new SvelteDate();
    const yesterday = new SvelteDate();
    yesterday.setDate(yesterday.getDate() - 1);
    const date = new SvelteDate(iso);

    if (key === dayKey(today)) {
      return 'Today';
    }

    if (key === dayKey(yesterday)) {
      return 'Yesterday';
    }

    return dayFormat.format(date);
  }

  function dayKey(value: Date): string {
    return `${value.getFullYear()}-${value.getMonth()}-${value.getDate()}`;
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

      <label>
        From
        <input
          type="datetime-local"
          step="1"
          bind:value={from}
          onchange={resetResults}
          required
        />
      </label>

      <label>
        To
        <input
          type="datetime-local"
          step="1"
          bind:value={to}
          onchange={resetResults}
          required
        />
      </label>

      <div class="alarm-filter-actions">
        <button class="button small ghost" type="button" onclick={setToday}>
          Today
        </button>
        <button class="button small ghost" type="button" onclick={setYesterday}>
          Yesterday
        </button>
        <button class="button small ghost" type="button" onclick={setLastSevenDays}>
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
                    <strong>{formatClock(range.start)} – {formatClock(range.end)}</strong>
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
