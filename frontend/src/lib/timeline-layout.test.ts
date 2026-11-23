import { describe, expect, it } from 'vitest';
import {
  layoutTimeline,
  timelineClusterGapMs,
  type TimelineEntry,
} from './timeline-layout';
import type { AlarmTarget } from './types';

const DAY_MS = 24 * 60 * 60 * 1000;
const DAY_START = Date.UTC(2026, 10, 23);
const DAY_END = DAY_START + DAY_MS;
const FIVE_MINUTES = 5 * 60_000;

let sequence = 0;

function entry(
  startOffsetMs: number,
  durationMs: number,
  target: AlarmTarget = 'human',
): TimelineEntry<number> {
  sequence += 1;
  const startMs = DAY_START + startOffsetMs;

  return {
    key: `entry-${sequence}`,
    item: sequence,
    startMs,
    endMs: startMs + durationMs,
    target,
  };
}

describe('timelineClusterGapMs', () => {
  it('caps the burst gap at five minutes for a full day', () => {
    expect(timelineClusterGapMs(DAY_MS)).toBe(FIVE_MINUTES);
  });

  it('scales the gap down with shorter visible ranges and zoom', () => {
    expect(timelineClusterGapMs(60 * 60_000)).toBe(14_400);
    expect(timelineClusterGapMs(DAY_MS, 16)).toBe(21_600);
  });

  it('keeps a minimum five-second gap', () => {
    expect(timelineClusterGapMs(10 * 60_000)).toBe(5_000);
  });
});

describe('layoutTimeline', () => {
  it('collapses a dense same-target burst into one bar', () => {
    const entries = Array.from({ length: 115 }, (_, index) =>
      entry(12 * 60 * 60_000 + index * 10_000, 5_000),
    );

    const clusters = layoutTimeline(entries, {
      startMs: DAY_START,
      endMs: DAY_END,
      gapMs: FIVE_MINUTES,
    });

    expect(clusters).toHaveLength(1);
    expect(clusters[0]!.items).toHaveLength(115);
    expect(clusters[0]!.lane).toBe(0);
    expect(clusters[0]!.originalStartMs).toBe(
      DAY_START + 12 * 60 * 60_000,
    );
    expect(clusters[0]!.originalEndMs).toBe(
      DAY_START + 12 * 60 * 60_000 + 114 * 10_000 + 5_000,
    );
  });

  it('splits a burst back into individual bars with a zoom-level gap', () => {
    const entries = Array.from({ length: 115 }, (_, index) =>
      entry(12 * 60 * 60_000 + index * 10_000, 5_000),
    );

    const clusters = layoutTimeline(entries, {
      startMs: DAY_START,
      endMs: DAY_END,
      gapMs: 4_000,
    });

    expect(clusters).toHaveLength(115);
    expect(new Set(clusters.map((cluster) => cluster.lane))).toEqual(
      new Set([0]),
    );
  });

  it('keeps different targets in separate lanes', () => {
    const clusters = layoutTimeline(
      [
        entry(7 * 60 * 60_000 + 8_000, 4_000, 'unknown'),
        entry(7 * 60 * 60_000, 10_000, 'human'),
        entry(7 * 60 * 60_000 + 5_000, 10_000, 'vehicle'),
      ],
      {
        startMs: DAY_START,
        endMs: DAY_END,
        gapMs: FIVE_MINUTES,
      },
    );

    expect(clusters).toHaveLength(3);
    expect(clusters.map((cluster) => cluster.target)).toEqual([
      'human',
      'vehicle',
      'unknown',
    ]);
    expect(clusters.map((cluster) => cluster.lane)).toEqual([0, 1, 2]);
  });

  it('reuses a lane for same-target clusters that are far apart', () => {
    const clusters = layoutTimeline(
      [entry(10 * 60_000, 5_000), entry(12 * 60 * 60_000, 5_000)],
      {
        startMs: DAY_START,
        endMs: DAY_END,
        gapMs: FIVE_MINUTES,
      },
    );

    expect(clusters).toHaveLength(2);
    expect(clusters.map((cluster) => cluster.lane)).toEqual([0, 0]);
  });

  it('merges same-target events below the gap and keeps larger gaps separate', () => {
    const merged = layoutTimeline(
      [entry(0, 5_000), entry(4 * 60_000, 5_000)],
      {
        startMs: DAY_START,
        endMs: DAY_END,
        gapMs: FIVE_MINUTES,
      },
    );

    const separate = layoutTimeline(
      [entry(0, 5_000), entry(6 * 60_000, 5_000)],
      {
        startMs: DAY_START,
        endMs: DAY_END,
        gapMs: FIVE_MINUTES,
      },
    );

    expect(merged).toHaveLength(1);
    expect(merged[0]!.items).toHaveLength(2);
    expect(separate).toHaveLength(2);
  });

  it('clips bars to the visible range and drops invisible events', () => {
    const clusters = layoutTimeline(
      [
        entry(-60 * 60_000, 2 * 60 * 60_000),
        entry(DAY_MS + 60 * 60_000, 60_000),
      ],
      {
        startMs: DAY_START,
        endMs: DAY_END,
        gapMs: FIVE_MINUTES,
      },
    );

    expect(clusters).toHaveLength(1);
    expect(clusters[0]!.left).toBe(0);
    expect(clusters[0]!.startMs).toBe(DAY_START);
    expect(clusters[0]!.originalStartMs).toBe(DAY_START - 60 * 60_000);
    expect(clusters[0]!.width).toBeGreaterThan(0);
  });

  it('keeps target lanes fixed regardless of which targets are present', () => {
    const vehicleOnly = layoutTimeline(
      [entry(12 * 60 * 60_000, 5_000, 'vehicle')],
      {
        startMs: DAY_START,
        endMs: DAY_END,
        gapMs: FIVE_MINUTES,
      },
    );

    const vehicleBeforeHuman = layoutTimeline(
      [
        entry(7 * 60 * 60_000, 5_000, 'vehicle'),
        entry(7 * 60 * 60_000 + 10_000, 5_000, 'human'),
      ],
      {
        startMs: DAY_START,
        endMs: DAY_END,
        gapMs: FIVE_MINUTES,
      },
    );

    expect(vehicleOnly).toHaveLength(1);
    expect(vehicleOnly[0]!.lane).toBe(1);
    expect(vehicleBeforeHuman.map((cluster) => cluster.lane)).toEqual([1, 0]);
  });
});
