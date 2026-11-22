<script lang="ts">
  import { SvelteDate, SvelteSet } from 'svelte/reactivity';
  import {
    formatDateRange,
    formatTime,
    formatTimelineTick,
  } from '../date-time';
  import { formatDuration, parseTime, segmentsOverlap } from '../playback-utils';
  import type { AlarmRange, RecordingSegment } from '../types';

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

  type MotionItem = {
    key: string;
    startMs: number;
    endMs: number;
    originalStartMs: number;
    originalEndMs: number;
    left: number;
    width: number;
    targets: string[];
    className: string;
  };

  let {
    start,
    end,
    recordings,
    motionRanges = [],
    motionTruncated = false,
    motionUnavailable = false,
    motionPending = false,
    selectedSegment = null,
    truncated = false,
    onPlay,
  }: {
    start: string;
    end: string;
    recordings: RecordingSegment[];
    motionRanges?: AlarmRange[];
    motionTruncated?: boolean;
    motionUnavailable?: boolean;
    motionPending?: boolean;
    selectedSegment?: RecordingSegment | null;
    truncated?: boolean;
    onPlay: (segment: RecordingSegment) => void;
  } = $props();

  const range = $derived(resolveRange(start, end, recordings, motionRanges));
  const items = $derived(layoutItems(recordings, range));
  const motionItems = $derived(layoutMotion(motionRanges, range));
  const ticks = $derived(layoutTicks(range));
  const rangeLabel = $derived(formatRangeLabel(range));
  const activeKeys = $derived(resolveActiveKeys(items, selectedSegment));

  function resolveRange(
    startValue: string,
    endValue: string,
    segments: RecordingSegment[],
    alarms: AlarmRange[],
  ): TimelineRange {
    let startMs = parseTime(startValue);
    let endMs = parseTime(endValue);

    if (startMs === null || endMs === null || endMs <= startMs) {
      const valid = [
        ...segments.map((segment) => ({
          start: parseTime(segment.start),
          end: parseTime(segment.end),
        })),
        ...alarms.map((alarm) => ({
          start: parseTime(alarm.start),
          end: parseTime(alarm.end),
        })),
      ].filter(
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

  function layoutMotion(
    alarms: AlarmRange[],
    currentRange: TimelineRange,
  ): MotionItem[] {
    const visible = alarms
      .map((alarm, index) => {
        const alarmStart = parseTime(alarm.start);
        const alarmEnd = parseTime(alarm.end);

        if (alarmStart === null || alarmEnd === null || alarmEnd <= alarmStart) {
          return null;
        }

        const visibleStart = Math.max(alarmStart, currentRange.startMs);
        const visibleEnd = Math.min(alarmEnd, currentRange.endMs);
        if (visibleEnd <= visibleStart) {
          return null;
        }

        return {
          alarm,
          index,
          startMs: visibleStart,
          endMs: visibleEnd,
          originalStartMs: alarmStart,
          originalEndMs: alarmEnd,
        };
      })
      .filter(
        (item): item is {
          alarm: AlarmRange;
          index: number;
          startMs: number;
          endMs: number;
          originalStartMs: number;
          originalEndMs: number;
        } => item !== null,
      )
      .sort(
        (left, right) =>
          left.startMs - right.startMs || left.endMs - right.endMs,
      );

    const merged: {
      startMs: number;
      endMs: number;
      originalStartMs: number;
      originalEndMs: number;
      targets: Set<string>;
      keys: string[];
    }[] = [];

    for (const item of visible) {
      const groups = merged[merged.length - 1];
      if (groups && item.startMs <= groups.endMs + 1000) {
        groups.endMs = Math.max(groups.endMs, item.endMs);
        groups.originalStartMs = Math.min(
          groups.originalStartMs,
          item.originalStartMs,
        );
        groups.originalEndMs = Math.max(groups.originalEndMs, item.originalEndMs);
        groups.targets.add(item.alarm.target);
        groups.keys.push(`${item.alarm.start}|${item.alarm.end}|${item.index}`);
        continue;
      }

      merged.push({
        startMs: item.startMs,
        endMs: item.endMs,
        originalStartMs: item.originalStartMs,
        originalEndMs: item.originalEndMs,
        targets: new Set([item.alarm.target]),
        keys: [`${item.alarm.start}|${item.alarm.end}|${item.index}`],
      });
    }

    return merged.map((item) => {
      const left = clamp(
        ((item.startMs - currentRange.startMs) / currentRange.durationMs) * 100,
        0,
        100,
      );
      const width = clamp(
        ((item.endMs - item.startMs) / currentRange.durationMs) * 100,
        0,
        100 - left,
      );
      const targets = [...item.targets].sort();
      const key = item.keys.join('|');

      return {
        key,
        startMs: item.startMs,
        endMs: item.endMs,
        originalStartMs: item.originalStartMs,
        originalEndMs: item.originalEndMs,
        left,
        width,
        targets,
        className: alignmentClass(targets),
      };
    });
  }

  function alignmentClass(targets: string[]): string {
    const set = new Set(targets);

    if (set.size > 1) {
      if (set.has('human') && set.has('vehicle')) {
        return 'motion-human-vehicle';
      }

      if (set.has('human')) {
        return 'motion-human-unknown';
      }

      if (set.has('vehicle')) {
        return 'motion-vehicle-unknown';
      }

      return 'motion-unknown';
    }

    return targets[0] === 'human'
      ? 'motion-human'
      : targets[0] === 'vehicle'
        ? 'motion-vehicle'
        : 'motion-unknown';
  }

  function motionTargetText(targets: string[]): string {
    if (targets.length === 0) {
      return 'Motion';
    }

    return targets
      .map((target) =>
        target === 'human'
          ? 'Person'
          : target === 'vehicle'
            ? 'Vehicle'
            : 'Motion',
      )
      .join(' and ');
  }

  function motionTitle(item: MotionItem): string {
    const seconds = Math.max(
      0,
      Math.round((item.originalEndMs - item.originalStartMs) / 1000),
    );
    return `${formatClock(item.originalStartMs)} – ${formatClock(item.originalEndMs)} · ` +
      `${motionTargetText(item.targets)} · ${formatDuration(seconds)}`;
  }

  function motionLabel(item: MotionItem): string {
    return `Motion from ${formatClock(item.originalStartMs)} to ` +
      `${formatClock(item.originalEndMs)}: ${motionTargetText(item.targets)}`;
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
    return formatDateRange(currentRange.startMs, currentRange.endMs, false);
  }

  function formatTick(ms: number, includeDate: boolean): string {
    return formatTimelineTick(ms, includeDate);
  }

  function formatClock(ms: number): string {
    return formatTime(ms, true);
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
      <h2 id="recording-coverage-title">Recording and motion</h2>
      <p class="muted">{rangeLabel}</p>
    </div>
    {#if truncated}
      <span class="badge">Partial result</span>
    {/if}
  </header>

  <div class="playback-timeline-scroll">
    <div class="playback-timeline-body">
      <div class="playback-timeline-track-header">
        <h3>Motion</h3>
        {#if motionTruncated}
          <span class="badge">Partial</span>
        {/if}
      </div>
      <div
        class="playback-timeline-motion-track"
        role="group"
        aria-label="Motion ranges timeline"
      >
        {#each ticks as tick (tick.key)}
          <span
            class="playback-timeline-grid-line"
            style={`left:${tick.left}%`}
            aria-hidden="true"
          ></span>
        {/each}

        {#each motionItems as item (item.key)}
          <span
            class="playback-timeline-motion-range {item.className}"
            style={`left:${item.left}%;width:${item.width}%`}
            title={motionTitle(item)}
            aria-label={motionLabel(item)}
            role="img"
          ></span>
        {/each}

        {#if motionItems.length === 0}
          <span class="playback-timeline-empty">
            {motionPending
              ? 'Loading motion…'
              : motionUnavailable
                ? 'Motion unavailable'
                : 'No motion in this range'}
          </span>
        {/if}
      </div>

      <div class="playback-timeline-track-header">
        <h3>Recording</h3>
      </div>
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
            No recordings in this range
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
