<script lang="ts">
  import { onDestroy } from 'svelte';
  import { SvelteMap } from 'svelte/reactivity';
  import FloatingTooltip from './FloatingTooltip.svelte';
  import { formatDate, formatDateRange, formatTime } from '../date-time';
  import { formatDuration } from '../playback-utils';
  import { buildPlaybackLink } from '../playback-links';
  import {
    layoutTimeline,
    timelineClusterGapMs,
    type TimelineCluster,
    type TimelineEntry,
  } from '../timeline-layout';
  import type { AlarmRange, AlarmTarget } from '../types';

  type AlarmCluster = TimelineCluster<AlarmRange>;

  type CameraLane = {
    key: string;
    label: string;
    clusters: AlarmCluster[];
    laneCount: number;
  };

  type DayGroup = {
    key: string;
    label: string;
    dayStartMs: number;
    dayEndMs: number;
    count: number;
    cameras: CameraLane[];
    ranges: AlarmRange[];
  };

  type TooltipState = {
    cluster: AlarmCluster;
    x: number;
    y: number;
    above: boolean;
  };

  let {
    ranges,
    onOpen,
  }: {
    ranges: AlarmRange[];
    onOpen: (range: AlarmRange) => void;
  } = $props();

  const dayGroups = $derived(groupByDay(ranges));
  const ticks = [0, 3, 6, 9, 12, 15, 18, 21, 24];

  let tooltip = $state<TooltipState | null>(null);
  let hideTimer: number | null = null;

  onDestroy(() => {
    if (hideTimer !== null) {
      window.clearTimeout(hideTimer);
    }
  });

  function groupByDay(items: AlarmRange[]): DayGroup[] {
    const days = new SvelteMap<
      string,
      {
        dayStartMs: number;
        cameras: Map<
          string,
          { label: string; entries: TimelineEntry<AlarmRange>[] }
        >;
        count: number;
        ranges: AlarmRange[];
      }
    >();

    for (const [rangeIndex, range] of items.entries()) {
      const rangeStart = new Date(range.start).getTime();
      const rangeEnd = new Date(range.end).getTime();

      if (
        !Number.isFinite(rangeStart) ||
        !Number.isFinite(rangeEnd) ||
        rangeEnd <= rangeStart
      ) {
        continue;
      }

      let dayStart = startOfLocalDay(rangeStart);

      while (dayStart < rangeEnd) {
        const dayEnd = startOfNextDay(dayStart);
        const visibleStart = Math.max(rangeStart, dayStart);
        const visibleEnd = Math.min(rangeEnd, dayEnd);

        if (visibleEnd > visibleStart) {
          const key = `${dayStart}`;
          let day = days.get(key);
          if (!day) {
            day = {
              dayStartMs: dayStart,
              cameras: new SvelteMap(),
              count: 0,
              ranges: [],
            };
            days.set(key, day);
          }

          day.ranges.push(range);

          const cameraKey = `${range.nvrId}|${range.cameraId}`;
          let camera = day.cameras.get(cameraKey);
          if (!camera) {
            camera = {
              label: `${range.cameraName} · ${range.nvrName} · ch ${range.channelId}`,
              entries: [],
            };
            day.cameras.set(cameraKey, camera);
          }

          camera.entries.push({
            key: `${range.start}|${range.end}|${rangeIndex}|${cameraKey}|${dayStart}`,
            item: range,
            startMs: rangeStart,
            endMs: rangeEnd,
            target: range.target,
          });
        }

        dayStart = dayEnd;
      }
    }

    return [...days.values()]
      .sort((left, right) => right.dayStartMs - left.dayStartMs)
      .map((day) => {
        const dayEndMs = startOfNextDay(day.dayStartMs);

        return {
          key: `${day.dayStartMs}`,
          label: formatDate(day.dayStartMs),
          dayStartMs: day.dayStartMs,
          dayEndMs,
          count: day.ranges.length,
          cameras: [...day.cameras.entries()]
            .map(([key, camera]) => {
              const clusters = layoutTimeline(camera.entries, {
                startMs: day.dayStartMs,
                endMs: dayEndMs,
                gapMs: timelineClusterGapMs(dayEndMs - day.dayStartMs),
              });

              return {
                key,
                label: camera.label,
                clusters,
                laneCount: Math.max(
                  1,
                  ...clusters.map((cluster) => cluster.lane + 1),
                ),
              };
            })
            .sort((left, right) => left.label.localeCompare(right.label)),
          ranges: [...day.ranges].sort(
            (left, right) =>
              new Date(left.start).getTime() - new Date(right.start).getTime(),
          ),
        };
      });
  }

  function startOfLocalDay(ms: number): number {
    const date = new Date(ms);
    return new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
  }

  function startOfNextDay(ms: number): number {
    const date = new Date(ms);
    return new Date(
      date.getFullYear(),
      date.getMonth(),
      date.getDate() + 1,
    ).getTime();
  }

  function barClass(target: AlarmTarget): string {
    return `alarm-bar-${target}`;
  }

  function primaryRange(cluster: AlarmCluster): AlarmRange {
    return cluster.items[0]!;
  }

  function clusterLabel(cluster: AlarmCluster): string {
    const target = displayTarget(primaryRange(cluster));
    const count = cluster.items.length;
    const subject =
      count > 1 ? `${count} ${target.toLowerCase()} alarms` : `${target} alarm`;

    return (
      `${subject} from ${formatTime(cluster.startMs)} ` +
      `to ${formatTime(cluster.endMs)}`
    );
  }

  function clusterDurationSeconds(cluster: AlarmCluster): number {
    return Math.max(
      0,
      Math.round((cluster.originalEndMs - cluster.originalStartMs) / 1000),
    );
  }

  function targetClass(target: AlarmTarget): string {
    return `alarm-target-${target}`;
  }

  function tableTime(range: AlarmRange): string {
    const start = new Date(range.start);
    const end = new Date(range.end);
    return start.toDateString() === end.toDateString()
      ? `${formatTime(start)} – ${formatTime(end)}`
      : formatDateRange(range.start, range.end);
  }

  function targetLabel(target: AlarmTarget): string {
    return target === 'human' ? 'Person' : target === 'vehicle' ? 'Vehicle' : 'Motion';
  }

  function displayTarget(range: AlarmRange): string {
    return range.target === 'unknown'
      ? range.targetLabel?.trim() || 'Motion'
      : targetLabel(range.target);
  }

  function playbackLink(cluster: AlarmCluster): string | null {
    const range = primaryRange(cluster);

    return buildPlaybackLink({
      cameraId: range.cameraId,
      start: range.start,
      end: range.end,
      at: range.start,
      autoplay: true,
    });
  }

  function showTooltip(event: PointerEvent, cluster: AlarmCluster) {
    const above = event.clientY > 140;
    tooltip = {
      cluster,
      x: clamp(event.clientX, 160, window.innerWidth - 160),
      y: above ? event.clientY - 12 : event.clientY + 18,
      above,
    };
    cancelHide();
  }

  function moveTooltip(event: PointerEvent) {
    if (!tooltip) {
      return;
    }

    const above = event.clientY > 140;
    tooltip = {
      ...tooltip,
      x: clamp(event.clientX, 160, window.innerWidth - 160),
      y: above ? event.clientY - 12 : event.clientY + 18,
      above,
    };
  }

  function showTooltipAtElement(element: Element, cluster: AlarmCluster) {
    const rect = element.getBoundingClientRect();
    const above = rect.top > 140;
    tooltip = {
      cluster,
      x: clamp(rect.left + rect.width / 2, 160, window.innerWidth - 160),
      y: above ? rect.top - 8 : rect.bottom + 8,
      above,
    };
    cancelHide();
  }

  function scheduleHide() {
    cancelHide();
    hideTimer = window.setTimeout(() => {
      tooltip = null;
      hideTimer = null;
    }, 120);
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

  function clamp(value: number, minimum: number, maximum: number): number {
    return Math.min(Math.max(value, minimum), maximum);
  }
</script>

{#each dayGroups as day (day.key)}
  <section class="alarm-day" aria-labelledby={`alarm-day-${day.key}`}>
    <header class="alarm-day-header">
      <h3 id={`alarm-day-${day.key}`}>{day.label}</h3>
      <span class="muted">
        {day.count} alarm{day.count === 1 ? '' : 's'}
      </span>
    </header>

    {#each day.cameras as camera, cameraIndex (camera.key)}
      <div class="alarm-lane">
        <span
          class="alarm-lane-label"
          id={`alarm-lane-${day.key}-${cameraIndex}`}
        >{camera.label}</span>
        <div
          class="alarm-track"
          style={`height:${Math.max(24, camera.laneCount * 20 + 6)}px`}
        >
          {#each ticks as tick (tick)}
            <span
              class="alarm-grid-line"
              style={`left:${(tick / 24) * 100}%`}
              aria-hidden="true"
            ></span>
          {/each}

          {#each camera.clusters as cluster (cluster.key)}
            <button
              class={`alarm-bar ${barClass(cluster.target)}`}
              type="button"
              style={`left:${cluster.left}%;width:${cluster.width}%;top:${cluster.lane * 20 + 3}px`}
              aria-label={clusterLabel(cluster)}
              aria-describedby={`alarm-lane-${day.key}-${cameraIndex}`}
              onpointerenter={(event) => showTooltip(event, cluster)}
              onpointermove={moveTooltip}
              onpointerleave={scheduleHide}
              onfocus={(event) => showTooltipAtElement(event.currentTarget, cluster)}
              onblur={(event) => {
                const next = event.relatedTarget;
                if (
                  next instanceof Element &&
                  next.closest('.floating-tooltip')
                ) {
                  return;
                }
                scheduleHide();
              }}
              onclick={() => {
                hideTooltip();
                onOpen(primaryRange(cluster));
              }}
            ></button>
          {/each}
        </div>
      </div>
    {/each}

    <div class="alarm-axis" aria-hidden="true">
      {#each ticks as tick (tick)}
        <span style={`left:${(tick / 24) * 100}%`}>
          {tick.toString().padStart(2, '0')}:00
        </span>
      {/each}
    </div>

    <div class="alarm-day-entries">
      <table class="alarm-table">
        <thead>
          <tr>
            <th scope="col">Time</th>
            <th scope="col">Alarm</th>
            <th scope="col">Camera</th>
            <th scope="col">Duration</th>
            <th scope="col">Play</th>
          </tr>
        </thead>
        <tbody>
          {#each day.ranges as range, rangeIndex (`${range.cameraId}|${range.channelId}|${range.target}|${range.start}|${range.end}|${rangeIndex}`)}
            <tr>
              <td class="alarm-table-time">
                {tableTime(range)}
                {#if range.inferredStart || range.inferredEnd}
                  <span class="alarm-table-badges">
                    {#if range.inferredStart}<span class="badge">est. start</span>{/if}
                    {#if range.inferredEnd}<span class="badge">est. end</span>{/if}
                  </span>
                {/if}
              </td>
              <td>
                <span class={`alarm-table-target ${targetClass(range.target)}`}>
                  {displayTarget(range)}
                </span>
              </td>
              <td>
                <div class="alarm-table-camera">
                  <span>{range.cameraName}</span>
                  <span class="alarm-table-nvr">
                    {range.nvrName} · ch {range.channelId}
                  </span>
                </div>
              </td>
              <td class="alarm-table-duration">
                {formatDuration(range.durationSeconds)}
              </td>
              <td class="alarm-table-action">
                <button
                  class="button small primary"
                  type="button"
                  onclick={() => onOpen(range)}
                >
                  Play
                </button>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  </section>
{/each}

{#if tooltip}
  {@const cluster = tooltip.cluster}
  {@const range = primaryRange(cluster)}
  <FloatingTooltip
    x={tooltip.x}
    y={tooltip.y}
    above={tooltip.above}
    interactive
    onpointerenter={cancelHide}
    onpointerleave={scheduleHide}
    onfocusout={(event) => {
      const next = event.relatedTarget;
      const current = event.currentTarget;
      if (
        !(current instanceof HTMLElement) ||
        !(next instanceof Node) ||
        !current.contains(next)
      ) {
        scheduleHide();
      }
    }}
  >
    <strong class="tooltip-time">
      {displayTarget(range)}
      {#if cluster.items.length > 1}
        <span class="badge">{cluster.items.length} alarms</span>
      {/if}
    </strong>
    <span class="tooltip-row">
      {range.cameraName} · {range.nvrName} · channel
      {range.channelId}
    </span>
    <span class="tooltip-row">
      {formatDateRange(cluster.originalStartMs, cluster.originalEndMs)}
    </span>
    <span class="tooltip-row">
      {formatDuration(clusterDurationSeconds(cluster))}
    </span>
    {#if cluster.items.some((item) => item.inferredStart) || cluster.items.some((item) => item.inferredEnd)}
      <span class="tooltip-row">
        {#if cluster.items.some((item) => item.inferredStart)}
          <span class="badge">estimated start</span>
        {/if}
        {#if cluster.items.some((item) => item.inferredEnd)}
          <span class="badge">estimated end</span>
        {/if}
      </span>
    {/if}
    {@const link = playbackLink(cluster)}
    {#if link}
      <a class="button small primary tooltip-link" href={link}>
        Open in playback
      </a>
    {/if}
  </FloatingTooltip>
{/if}
