/**
 * Shared layout for motion/alarm timelines.
 *
 * Individual events are grouped per target and merged into clusters when they
 * are visually close. Clusters are then packed into a bounded number of lanes,
 * which keeps dense bursts from growing the timeline vertically.
 */

import type { AlarmTarget } from './types';

const CLUSTER_GAP_FRACTION = 0.004;
const CLUSTER_GAP_MIN_MS = 5_000;
const CLUSTER_GAP_MAX_MS = 5 * 60_000;

/** Fixed row per target so bars never change level as other targets appear. */
const TARGET_LANES: Record<AlarmTarget, number> = {
  human: 0,
  vehicle: 1,
  unknown: 2,
};

export interface TimelineEntry<T> {
  key: string;
  item: T;
  startMs: number;
  endMs: number;
  target: AlarmTarget;
}

export interface TimelineCluster<T> {
  key: string;
  target: AlarmTarget;
  items: T[];
  /** Visible/clipped start in milliseconds. */
  startMs: number;
  /** Visible/clipped end in milliseconds. */
  endMs: number;
  /** Original (unclipped) start of the earliest event in the cluster. */
  originalStartMs: number;
  /** Original (unclipped) end of the latest event in the cluster. */
  originalEndMs: number;
  left: number;
  width: number;
  lane: number;
}

export interface TimelineLayoutOptions {
  startMs: number;
  endMs: number;
  gapMs: number;
}

interface VisibleEntry<T> extends TimelineEntry<T> {
  visibleStartMs: number;
  visibleEndMs: number;
}

interface WorkingCluster<T> {
  keys: string[];
  target: AlarmTarget;
  items: T[];
  startMs: number;
  endMs: number;
  originalStartMs: number;
  originalEndMs: number;
}

/**
 * Gap under which nearby events are considered part of the same burst.
 * Scales with the visible time span so zooming into playback splits clusters
 * back into individual events.
 */
export function timelineClusterGapMs(durationMs: number, zoom = 1): number {
  const duration = Number.isFinite(durationMs) ? Math.max(0, durationMs) : 0;
  const safeZoom = Number.isFinite(zoom) ? Math.max(1, zoom) : 1;
  const gap = (duration / safeZoom) * CLUSTER_GAP_FRACTION;

  return clamp(gap, CLUSTER_GAP_MIN_MS, CLUSTER_GAP_MAX_MS);
}

export function layoutTimeline<T>(
  entries: readonly TimelineEntry<T>[],
  options: TimelineLayoutOptions,
): TimelineCluster<T>[] {
  const rangeDurationMs = options.endMs - options.startMs;

  if (!Number.isFinite(rangeDurationMs) || rangeDurationMs <= 0) {
    return [];
  }

  const gapMs = Number.isFinite(options.gapMs) ? Math.max(0, options.gapMs) : 0;
  const byTarget = new Map<AlarmTarget, VisibleEntry<T>[]>();

  for (const entry of entries) {
    if (!Number.isFinite(entry.startMs) || !Number.isFinite(entry.endMs)) {
      continue;
    }

    const visibleStartMs = Math.max(entry.startMs, options.startMs);
    const visibleEndMs = Math.min(entry.endMs, options.endMs);

    if (visibleEndMs <= visibleStartMs) {
      continue;
    }

    const group = byTarget.get(entry.target);
    const visibleEntry = { ...entry, visibleStartMs, visibleEndMs };

    if (group) {
      group.push(visibleEntry);
    } else {
      byTarget.set(entry.target, [visibleEntry]);
    }
  }

  const clusters: WorkingCluster<T>[] = [];

  for (const [target, group] of byTarget) {
    group.sort(
      (left, right) =>
        left.visibleStartMs - right.visibleStartMs ||
        left.visibleEndMs - right.visibleEndMs ||
        left.key.localeCompare(right.key),
    );

    let current: WorkingCluster<T> | null = null;

    for (const entry of group) {
      if (current && entry.visibleStartMs <= current.endMs + gapMs) {
        current.endMs = Math.max(current.endMs, entry.visibleEndMs);
        current.originalStartMs = Math.min(
          current.originalStartMs,
          entry.startMs,
        );
        current.originalEndMs = Math.max(current.originalEndMs, entry.endMs);
        current.items.push(entry.item);
        current.keys.push(entry.key);
        continue;
      }

      current = {
        keys: [entry.key],
        target,
        items: [entry.item],
        startMs: entry.visibleStartMs,
        endMs: entry.visibleEndMs,
        originalStartMs: entry.startMs,
        originalEndMs: entry.endMs,
      };
      clusters.push(current);
    }
  }

  clusters.sort(
    (left, right) =>
      left.startMs - right.startMs ||
      left.endMs - right.endMs ||
      left.target.localeCompare(right.target),
  );

  return clusters.map((cluster) => {
    const left = clamp(
      ((cluster.startMs - options.startMs) / rangeDurationMs) * 100,
      0,
      100,
    );
    const width = clamp(
      ((cluster.endMs - cluster.startMs) / rangeDurationMs) * 100,
      0,
      100 - left,
    );

    return {
      key: cluster.keys[0] ?? `${cluster.target}|${cluster.originalStartMs}`,
      target: cluster.target,
      items: cluster.items,
      startMs: cluster.startMs,
      endMs: cluster.endMs,
      originalStartMs: cluster.originalStartMs,
      originalEndMs: cluster.originalEndMs,
      left,
      width,
      lane: TARGET_LANES[cluster.target],
    };
  });
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(Math.max(value, minimum), maximum);
}
