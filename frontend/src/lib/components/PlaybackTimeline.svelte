<script lang="ts">
  import { onDestroy, tick } from 'svelte';
  import { SvelteDate, SvelteSet } from 'svelte/reactivity';
  import FloatingTooltip from './FloatingTooltip.svelte';
  import {
    formatDateRange,
    formatTime,
    formatTimelineTick,
  } from '../date-time';
  import { formatDuration, parseTime, segmentsOverlap } from '../playback-utils';
  import {
    layoutTimeline,
    timelineClusterGapMs,
    type TimelineCluster,
    type TimelineEntry,
  } from '../timeline-layout';
  import type { AlarmRange, AlarmTarget, RecordingSegment } from '../types';

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

  type MotionItem = TimelineCluster<AlarmRange> & {
    className: string;
  };

  type TooltipContent =
    | {
        kind: 'recording';
        item: TimelineItem;
      }
    | {
        kind: 'motion';
        item: MotionItem;
      };

  type TooltipState = TooltipContent & {
    x: number;
    y: number;
    above: boolean;
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

  let zoom = $state(1);
  let viewport = $state<HTMLDivElement>();
  let drag = $state<{
    pointerId: number;
    startX: number;
    startScroll: number;
    moved: boolean;
  } | null>(null);
  let suppressClick = false;
  let lastRangeKey = '';

  const range = $derived(resolveRange(start, end, recordings, motionRanges));
  const items = $derived(layoutItems(recordings, range));
  const motionItems = $derived(
    layoutMotion(
      motionRanges,
      range,
      timelineClusterGapMs(range.durationMs, zoom),
    ),
  );
  const motionLaneCount = $derived(
    Math.max(1, ...motionItems.map((item) => item.lane + 1)),
  );
  const ticks = $derived(layoutTicks(range, zoom));
  const rangeLabel = $derived(formatRangeLabel(range));
  const activeKeys = $derived(resolveActiveKeys(items, selectedSegment));

  let tooltip = $state<TooltipState | null>(null);
  let hideTimer: number | null = null;

  $effect(() => {
    const key = `${start}|${end}`;
    if (key !== lastRangeKey) {
      lastRangeKey = key;
      zoom = 1;
      hideTooltip();
      void tick().then(() => {
        if (viewport) {
          viewport.scrollLeft = 0;
        }
      });
    }
  });

  $effect(() => {
    void items;
    void motionItems;
    hideTooltip();
  });

  onDestroy(() => {
    if (hideTimer !== null) {
      window.clearTimeout(hideTimer);
    }
  });

  async function changeZoom(delta: number) {
    const next = clamp(zoom + delta, 1, 16);
    if (next === zoom) {
      return;
    }

    if (!viewport) {
      zoom = next;
      return;
    }

    const oldScrollWidth = viewport.scrollWidth;
    const centerFraction =
      oldScrollWidth > 0
        ? (viewport.scrollLeft + viewport.clientWidth / 2) / oldScrollWidth
        : 0;

    zoom = next;
    await tick();

    if (viewport) {
      viewport.scrollLeft = Math.max(
        0,
        centerFraction * viewport.scrollWidth - viewport.clientWidth / 2,
      );
    }
  }

  function handlePointerDown(event: PointerEvent) {
    hideTooltip();

    if (
      event.pointerType !== 'mouse' ||
      event.button !== 0 ||
      zoom <= 1 ||
      !viewport
    ) {
      return;
    }

    drag = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startScroll: viewport.scrollLeft,
      moved: false,
    };
    viewport.setPointerCapture(event.pointerId);
  }

  function handlePointerMove(event: PointerEvent) {
    if (!drag || !viewport || drag.pointerId !== event.pointerId) {
      return;
    }

    const delta = event.clientX - drag.startX;
    if (!drag.moved && Math.abs(delta) > 4) {
      drag.moved = true;
    }

    if (drag.moved) {
      event.preventDefault();
      viewport.scrollLeft = drag.startScroll - delta;
    }
  }

  function handlePointerUp(event: PointerEvent) {
    if (!drag || drag.pointerId !== event.pointerId) {
      return;
    }

    const moved = drag.moved;
    drag = null;

    if (viewport?.hasPointerCapture(event.pointerId)) {
      viewport.releasePointerCapture(event.pointerId);
    }

    if (moved) {
      suppressClick = true;
      window.setTimeout(() => {
        suppressClick = false;
      }, 0);
    }
  }

  function shouldSuppressClick(): boolean {
    if (suppressClick) {
      suppressClick = false;
      return true;
    }

    return false;
  }

  function showTooltip(
    event: PointerEvent,
    state: TooltipContent,
  ) {
    const above = event.clientY > 120;
    tooltip = {
      ...state,
      x: clamp(event.clientX, 140, window.innerWidth - 140),
      y: above ? event.clientY - 12 : event.clientY + 18,
      above,
    } as TooltipState;
    cancelHide();
  }

  function moveTooltip(event: PointerEvent) {
    if (!tooltip) {
      return;
    }

    const above = event.clientY > 120;
    tooltip = {
      ...tooltip,
      x: clamp(event.clientX, 140, window.innerWidth - 140),
      y: above ? event.clientY - 12 : event.clientY + 18,
      above,
    };
  }

  function showTooltipAtElement(
    element: Element,
    state: TooltipContent,
  ) {
    const rect = element.getBoundingClientRect();
    const above = rect.top > 120;
    tooltip = {
      ...state,
      x: clamp(rect.left + rect.width / 2, 140, window.innerWidth - 140),
      y: above ? rect.top - 8 : rect.bottom + 8,
      above,
    } as TooltipState;
    cancelHide();
  }

  function scheduleHide() {
    cancelHide();
    hideTimer = window.setTimeout(() => {
      tooltip = null;
      hideTimer = null;
    }, 100);
  }

  function cancelHide() {
    if (hideTimer !== null) {
      window.clearTimeout(hideTimer);
      hideTimer = null;
    }
  }

  function hideTooltip() {
    cancelHide();
    tooltip = null;
  }

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
    gapMs: number,
  ): MotionItem[] {
    const entries: TimelineEntry<AlarmRange>[] = [];

    for (const [index, alarm] of alarms.entries()) {
      const alarmStart = parseTime(alarm.start);
      const alarmEnd = parseTime(alarm.end);

      if (alarmStart === null || alarmEnd === null || alarmEnd <= alarmStart) {
        continue;
      }

      entries.push({
        key: `${alarm.start}|${alarm.end}|${index}`,
        item: alarm,
        startMs: alarmStart,
        endMs: alarmEnd,
        target: alarm.target,
      });
    }

    return layoutTimeline(entries, {
      startMs: currentRange.startMs,
      endMs: currentRange.endMs,
      gapMs,
    }).map((cluster) => ({
      ...cluster,
      className: motionClass(cluster.target),
    }));
  }

  function motionClass(target: AlarmTarget): string {
    return target === 'human'
      ? 'motion-human'
      : target === 'vehicle'
        ? 'motion-vehicle'
        : 'motion-unknown';
  }

  function motionTargetText(target: AlarmTarget): string {
    return target === 'human'
      ? 'Person'
      : target === 'vehicle'
        ? 'Vehicle'
        : 'Motion';
  }

  function motionLabel(item: MotionItem): string {
    const target = motionTargetText(item.target);
    const count = item.items.length;
    const subject =
      count > 1 ? `${count} ${target.toLowerCase()} alarms` : `${target} alarm`;

    return `Play recording for ${subject} from ` +
      `${formatClock(item.originalStartMs)} to ${formatClock(item.originalEndMs)}`;
  }

  function playableMotionSegment(item: MotionItem): RecordingSegment | null {
    const alarmStart = item.originalStartMs;
    const alarmEnd = item.originalEndMs;
    const segments = recordings
      .map((segment) => ({
        segment,
        start: parseTime(segment.start),
        end: parseTime(segment.end),
      }))
      .filter(
        (segment): segment is { segment: RecordingSegment; start: number; end: number } =>
          segment.start !== null &&
          segment.end !== null &&
          segment.end > segment.start,
      )
      .sort((left, right) => left.start - right.start);

    const candidate =
      segments.find(
        (segment) => segment.start <= alarmStart && segment.end > alarmStart,
      ) ??
      segments.find(
        (segment) => segment.start < alarmEnd && segment.end > alarmStart,
      );

    if (!candidate) {
      return null;
    }

    const start = Math.max(candidate.start, alarmStart - 5000);
    if (start >= candidate.end) {
      return null;
    }

    return {
      ...candidate.segment,
      start: new Date(start).toISOString(),
      durationSeconds: Math.max(
        0,
        Math.round((candidate.end - start) / 1000),
      ),
    };
  }

  function playMotion(item: MotionItem) {
    const segment = playableMotionSegment(item);
    if (!segment) {
      return;
    }

    hideTooltip();
    onPlay(segment);
  }

  function layoutTicks(
    currentRange: TimelineRange,
    currentZoom: number,
  ): TimelineTick[] {
    const visibleMinutes = currentRange.durationMs / 60_000 / currentZoom;
    const steps = [1, 5, 15, 30, 60, 120, 180, 360, 720, 1440];
    const stepMinutes =
      steps.find((candidate) => visibleMinutes / candidate <= 10) ?? 1440;
    const stepMs = stepMinutes * 60_000;
    const ticks: TimelineTick[] = [];
    const includeDate = currentRange.durationMs > 24 * 60 * 60 * 1000;

    const first = new SvelteDate(currentRange.startMs);
    first.setSeconds(0, 0);

    if (stepMinutes < 60) {
      first.setMinutes(first.getMinutes() - (first.getMinutes() % stepMinutes));
      while (first.getTime() < currentRange.startMs) {
        first.setTime(first.getTime() + stepMs);
      }
    } else if (stepMinutes === 1440) {
      first.setHours(0, 0, 0, 0);
      while (first.getTime() < currentRange.startMs) {
        first.setDate(first.getDate() + 1);
      }
    } else {
      first.setMinutes(0, 0, 0);
      const stepHours = stepMinutes / 60;
      let guard = 0;
      while (first.getTime() < currentRange.startMs && guard < 96) {
        first.setHours(first.getHours() + 1);
        guard += 1;
      }

      guard = 0;
      while (first.getHours() % stepHours !== 0 && guard < 48) {
        first.setHours(first.getHours() + 1);
        guard += 1;
      }
    }

    let guard = 0;
    while (first.getTime() < currentRange.endMs && ticks.length < 48 && guard < 96) {
      const position =
        ((first.getTime() - currentRange.startMs) / currentRange.durationMs) * 100;

      if (position >= 5 && position <= 90) {
        ticks.push({
          key: `${first.getTime()}`,
          left: position,
          label: formatTick(first.getTime(), includeDate),
        });
      }

      if (stepMinutes === 1440) {
        first.setDate(first.getDate() + 1);
      } else if (stepMinutes < 60) {
        first.setTime(first.getTime() + stepMs);
      } else {
        first.setHours(first.getHours() + stepMinutes / 60);
      }

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

    <div class="timeline-zoom" aria-label="Timeline zoom">
      <button
        class="timeline-zoom-button"
        type="button"
        aria-label="Zoom out timeline"
        title="Zoom out"
        disabled={zoom <= 1}
        onclick={() => void changeZoom(-1)}
      >
        <svg viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="8.5" cy="8.5" r="5"></circle>
          <path d="M12.2 12.2L17 17M6 8.5h5"></path>
        </svg>
      </button>
      <span>{Math.round(zoom * 100)}%</span>
      <button
        class="timeline-zoom-button"
        type="button"
        aria-label="Zoom in timeline"
        title="Zoom in"
        disabled={zoom >= 16}
        onclick={() => void changeZoom(1)}
      >
        <svg viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="8.5" cy="8.5" r="5"></circle>
          <path d="M12.2 12.2L17 17M6 8.5h5M8.5 6v5"></path>
        </svg>
      </button>
    </div>
  </header>

  <div
    class="playback-timeline-scroll"
    class:panning={drag !== null}
    class:zoomed={zoom > 1}
    role="group"
    aria-label="Timeline scroll area"
    bind:this={viewport}
    onscroll={hideTooltip}
    onpointerdown={handlePointerDown}
    onpointermove={handlePointerMove}
    onpointerup={handlePointerUp}
    onpointercancel={handlePointerUp}
  >
    <div class="playback-timeline-body" style={`width:${zoom * 100}%`}>
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
        style={`height:${Math.max(24, motionLaneCount * 20 + 6)}px`}
      >
        {#each ticks as tick (tick.key)}
          <span
            class="playback-timeline-grid-line"
            style={`left:${tick.left}%`}
            aria-hidden="true"
          ></span>
        {/each}

        {#each motionItems as item (item.key)}
          <button
            class="playback-timeline-motion-range {item.className}"
            type="button"
            style={`left:${item.left}%;width:${item.width}%;top:${item.lane * 20 + 3}px`}
            aria-label={motionLabel(item)}
            onpointerenter={(event) =>
              showTooltip(event, { kind: 'motion', item })}
            onpointermove={moveTooltip}
            onpointerleave={scheduleHide}
            onfocus={(event) =>
              showTooltipAtElement(event.currentTarget, { kind: 'motion', item })}
            onblur={(event) => {
              const next = event.relatedTarget;
              if (next instanceof Element && next.closest('.floating-tooltip')) {
                return;
              }
              scheduleHide();
            }}
            onclick={() => {
              if (shouldSuppressClick()) {
                return;
              }
              playMotion(item);
            }}
          ></button>
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
            aria-label={segmentLabel(item)}
            aria-pressed={activeKeys.has(item.key)}
            onpointerenter={(event) =>
              showTooltip(event, { kind: 'recording', item })}
            onpointermove={moveTooltip}
            onpointerleave={scheduleHide}
            onfocus={(event) =>
              showTooltipAtElement(event.currentTarget, {
                kind: 'recording',
                item,
              })}
            onblur={scheduleHide}
            onclick={() => {
              if (shouldSuppressClick()) {
                return;
              }
              hideTooltip();
              onPlay(item.segment);
            }}
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

  {#if tooltip}
    <FloatingTooltip
      x={tooltip.x}
      y={tooltip.y}
      above={tooltip.above}
      interactive={tooltip.kind === 'motion'}
      onpointerenter={tooltip.kind === 'motion' ? cancelHide : null}
      onpointerleave={tooltip.kind === 'motion' ? scheduleHide : null}
      onfocusout={tooltip.kind === 'motion'
        ? (event) => {
            const current = event.currentTarget;
            const next = event.relatedTarget;
            if (
              !(current instanceof HTMLElement) ||
              !(next instanceof Node) ||
              !current.contains(next)
            ) {
              scheduleHide();
            }
          }
        : null}
    >
      {#if tooltip.kind === 'recording'}
        <strong class="tooltip-time">
          {formatDateRange(tooltip.item.startMs, tooltip.item.endMs)}
        </strong>
        <span class="tooltip-row">
          {formatDuration(tooltip.item.segment.durationSeconds)}
          {#if tooltip.item.segment.codecType}
            · <span class="badge accent">{tooltip.item.segment.codecType}</span>
          {/if}
        </span>
        <span class="tooltip-hint">Click to play</span>
      {:else}
        {@const motion = tooltip.item}
        <strong class="tooltip-time">
          {motionTargetText(motion.target)}
          {#if motion.items.length > 1}
            <span class="badge">{motion.items.length} alarms</span>
          {/if}
        </strong>
        <span class="tooltip-row">
          {formatDateRange(motion.originalStartMs, motion.originalEndMs)}
        </span>
        <span class="tooltip-row">
          {formatDuration(
            Math.max(
              0,
              Math.round((motion.originalEndMs - motion.originalStartMs) / 1000),
            ),
          )}
        </span>
        {@const segment = playableMotionSegment(motion)}
        {#if segment}
          <button
            class="button small primary tooltip-link"
            type="button"
            onclick={() => playMotion(motion)}
          >
            Play
          </button>
        {:else}
          <span class="tooltip-hint">No recording covers this alarm time</span>
        {/if}
      {/if}
    </FloatingTooltip>
  {/if}
</section>
