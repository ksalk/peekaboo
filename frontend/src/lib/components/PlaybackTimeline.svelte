<script lang="ts">
  import { SvelteDate, SvelteSet } from 'svelte/reactivity';
  import { formatDuration, parseTime, segmentsOverlap } from '../playback-utils';
  import type { RecordingSegment } from '../types';

  const shortClockFormat = new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
  });
  const longClockFormat = new Intl.DateTimeFormat(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  });
  const dateFormat = new Intl.DateTimeFormat(undefined, {
    weekday: 'short',
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
  const dateTimeFormat = new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });

  type TimelineRange = {
    startMs: number;
    endMs: number;
    durationMs: number;
  };

  type TimelineItem = {
    key: string;
    segment: RecordingSegment;
    startMs: number;
    endMs: number;
    left: number;
    width: number;
  };

  type TimelineTick = {
    key: string;
    left: number;
    label: string;
  };

  let {
    start,
    end,
    recordings,
    selectedSegment = null,
    truncated = false,
    onPlay,
  }: {
    start: string;
    end: string;
    recordings: RecordingSegment[];
    selectedSegment?: RecordingSegment | null;
    truncated?: boolean;
    onPlay: (segment: RecordingSegment) => void;
  } = $props();

  const range = $derived(resolveRange(start, end, recordings));
  const items = $derived(layoutItems(recordings, range));
  const ticks = $derived(layoutTicks(range));
  const rangeLabel = $derived(formatRangeLabel(range));
  const activeKeys = $derived(resolveActiveKeys(items, selectedSegment));

  function resolveRange(
    startValue: string,
    endValue: string,
    segments: RecordingSegment[],
  ): TimelineRange {
    let startMs = parseTime(startValue);
    let endMs = parseTime(endValue);

    if (startMs === null || endMs === null || endMs <= startMs) {
      const valid = segments
        .map((segment) => ({
          start: parseTime(segment.start),
          end: parseTime(segment.end),
        }))
        .filter(
          (segment): segment is { start: number; end: number } =>
            segment.start !== null &&
            segment.end !== null &&
            segment.end > segment.start,
        );

      if (valid.length > 0) {
        startMs = Math.min(...valid.map((segment) => segment.start));
        endMs = Math.max(...valid.map((segment) => segment.end));
      }
    }

    if (startMs === null || endMs === null || endMs <= startMs) {
      return { startMs: 0, endMs: 1, durationMs: 1 };
    }

    return { startMs, endMs, durationMs: endMs - startMs };
  }

  function layoutItems(
    segments: RecordingSegment[],
    currentRange: TimelineRange,
  ): TimelineItem[] {
    return segments
      .map((segment, index) => {
        const segmentStart = parseTime(segment.start);
        const segmentEnd = parseTime(segment.end);

        if (
          segmentStart === null ||
          segmentEnd === null ||
          segmentEnd <= segmentStart
        ) {
          return null;
        }

        const visibleStart = Math.max(segmentStart, currentRange.startMs);
        const visibleEnd = Math.min(segmentEnd, currentRange.endMs);

        if (visibleEnd <= visibleStart) {
          return null;
        }

        const left = clamp(
          ((visibleStart - currentRange.startMs) / currentRange.durationMs) * 100,
          0,
          100,
        );
        const width = clamp(
          ((visibleEnd - visibleStart) / currentRange.durationMs) * 100,
          0,
          100 - left,
        );

        if (width <= 0) {
          return null;
        }

        return {
          key: `${segment.start}|${segment.end}|${index}`,
          segment,
          startMs: segmentStart,
          endMs: segmentEnd,
          left,
          width,
        };
      })
      .filter((item): item is TimelineItem => item !== null)
      .sort(
        (left, right) =>
          left.startMs - right.startMs || left.endMs - right.endMs,
      );
  }

  function layoutTicks(currentRange: TimelineRange): TimelineTick[] {
    const durationHours = currentRange.durationMs / 3_600_000;
    const stepHours =
      durationHours <= 6
        ? 1
        : durationHours <= 12
          ? 2
          : durationHours <= 24
            ? 3
            : durationHours <= 48
              ? 6
              : 24;
    const ticks: TimelineTick[] = [];

    const first = new SvelteDate(currentRange.startMs);
    first.setMinutes(0, 0, 0);

    if (first.getTime() < currentRange.startMs) {
      first.setHours(first.getHours() + 1);
    }

    let guard = 0;
    while (first.getHours() % stepHours !== 0 && guard < 48) {
      first.setHours(first.getHours() + 1);
      guard += 1;
    }

    guard = 0;

    while (first.getTime() < currentRange.endMs && ticks.length < 48 && guard < 96) {
      const position =
        ((first.getTime() - currentRange.startMs) / currentRange.durationMs) * 100;

      if (position >= 5 && position <= 90) {
        ticks.push({
          key: `${first.getTime()}`,
          left: position,
          label: formatTick(first.getTime(), durationHours > 24),
        });
      }

      first.setHours(first.getHours() + stepHours);
      guard += 1;
    }

    return ticks;
  }

  function resolveActiveKeys(
    timelineItems: TimelineItem[],
    selected: RecordingSegment | null,
  ): SvelteSet<string> {
    const keys = new SvelteSet<string>();

    if (selected === null) {
      return keys;
    }

    for (const item of timelineItems) {
      if (segmentsOverlap(item.segment, selected)) {
        keys.add(item.key);
      }
    }

    return keys;
  }

  function formatRangeLabel(currentRange: TimelineRange): string {
    const startDate = new Date(currentRange.startMs);
    const endDate = new Date(currentRange.endMs);

    if (startDate.toDateString() === endDate.toDateString()) {
      return `${dateFormat.format(startDate)} · ${shortClockFormat.format(startDate)}–${shortClockFormat.format(endDate)}`;
    }

    return `${dateFormat.format(startDate)} ${shortClockFormat.format(startDate)} – ${dateFormat.format(endDate)} ${shortClockFormat.format(endDate)}`;
  }

  function formatTick(ms: number, includeDate: boolean): string {
    return (includeDate ? dateTimeFormat : shortClockFormat).format(new Date(ms));
  }

  function formatClock(ms: number): string {
    return longClockFormat.format(new Date(ms));
  }

  function segmentTitle(item: TimelineItem): string {
    return `${formatClock(item.startMs)} – ${formatClock(item.endMs)} · ${formatDuration(item.segment.durationSeconds)}`;
  }

  function segmentLabel(item: TimelineItem): string {
    return `Play recording from ${formatClock(item.startMs)} to ${formatClock(item.endMs)}`;
  }

  function clamp(value: number, minimum: number, maximum: number): number {
    return Math.min(Math.max(value, minimum), maximum);
  }
</script>

<section class="card playback-timeline" aria-labelledby="recording-coverage-title">
  <header class="playback-timeline-header">
    <div>
      <h2 id="recording-coverage-title">Recording coverage</h2>
      <p class="muted">{rangeLabel}</p>
    </div>
    {#if truncated}
      <span class="badge">Partial result</span>
    {/if}
  </header>

  <div class="playback-timeline-scroll">
    <div class="playback-timeline-body">
      <div
        class="playback-timeline-track"
        role="group"
        aria-label="Recording coverage timeline"
      >
        {#each ticks as tick (tick.key)}
          <span
            class="playback-timeline-grid-line"
            style={`left:${tick.left}%`}
            aria-hidden="true"
          ></span>
        {/each}

        {#each items as item (item.key)}
          <button
            class="playback-timeline-segment"
            class:active={activeKeys.has(item.key)}
            type="button"
            style={`left:${item.left}%;width:${item.width}%`}
            title={segmentTitle(item)}
            aria-label={segmentLabel(item)}
            aria-pressed={activeKeys.has(item.key)}
            onclick={() => onPlay(item.segment)}
          ></button>
        {/each}

        {#if items.length === 0}
          <span class="playback-timeline-empty">
            No coverage within this range; see the recording list below
          </span>
        {/if}
      </div>

      <div class="playback-timeline-ticks" aria-hidden="true">
        {#each ticks as tick (tick.key)}
          <span
            class="playback-timeline-tick"
            style={`left:${tick.left}%`}
          >{tick.label}</span>
        {/each}
      </div>
    </div>
  </div>
</section>
