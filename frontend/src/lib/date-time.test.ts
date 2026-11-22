import { describe, expect, it } from 'vitest';
import {
  addDays,
  buildLocalDateTime,
  dayKey,
  endOfDay,
  formatDate,
  formatDateRange,
  formatDateTime,
  formatDayLabel,
  formatTime,
  formatTimelineTick,
  isSameLocalDay,
  isValidDate,
  parseDisplayDateTime,
  parseLocalDateTimeInput,
  quickRange,
  startOfDay,
  toDisplayDateTime,
  toExclusiveEnd,
  toLocalDateTimeInput,
} from './date-time';

// The test script sets TZ=Europe/Warsaw so the DST cases are deterministic.
describe('date-time formatting', () => {
  it('formats dates as dd-mm-yyyy', () => {
    expect(formatDate(new Date(2026, 10, 2, 9, 5, 7))).toBe('02-11-2026');
    expect(formatDate(new Date(2026, 0, 31))).toBe('31-01-2026');
  });

  it('formats times with a 24-hour clock', () => {
    expect(formatTime(new Date(2026, 10, 22, 0, 5, 9))).toBe('00:05:09');
    expect(formatTime(new Date(2026, 10, 22, 23, 59, 59))).toBe('23:59:59');
    expect(formatTime(new Date(2026, 10, 22, 14, 30), false)).toBe('14:30');
  });

  it('formats a date and time together', () => {
    expect(formatDateTime(new Date(2026, 10, 22, 14, 30, 5))).toBe(
      '22-11-2026 14:30:05',
    );
    expect(formatDateTime('not a date')).toBe('—');
  });

  it('formats ranges on one day and across days', () => {
    expect(
      formatDateRange(
        new Date(2026, 10, 22, 14, 0, 0),
        new Date(2026, 10, 22, 15, 2, 3),
      ),
    ).toBe('22-11-2026 · 14:00:00–15:02:03');

    expect(
      formatDateRange(
        new Date(2026, 10, 21, 23, 0, 0),
        new Date(2026, 10, 22, 1, 0, 0),
      ),
    ).toBe('21-11-2026 23:00:00 – 22-11-2026 01:00:00');
  });

  it('formats timeline ticks without locale clocks', () => {
    expect(formatTimelineTick(new Date(2026, 10, 22, 9, 7).getTime(), false)).toBe(
      '09:07',
    );
    expect(formatTimelineTick(new Date(2026, 10, 22, 9, 7).getTime(), true)).toBe(
      '22-11 09:07',
    );
  });

  it('labels today and yesterday relative to a reference date', () => {
    const reference = new Date(2026, 10, 22, 12, 0, 0);
    expect(formatDayLabel(new Date(2026, 10, 22, 1, 0, 0), reference)).toBe(
      'Today',
    );
    expect(formatDayLabel(new Date(2026, 10, 21, 1, 0, 0), reference)).toBe(
      'Yesterday',
    );
    expect(formatDayLabel(new Date(2026, 10, 20, 1, 0, 0), reference)).toBe(
      '20-11-2026',
    );
  });

  it('handles invalid values without throwing', () => {
    expect(isValidDate('nope')).toBe(false);
    expect(formatTime('nope')).toBe('—');
    expect(formatDateRange('nope', 'nope')).toBe('—');
    expect(formatDayLabel('nope')).toBe('—');
  });
});

describe('local date parsing', () => {
  it('parses the internal local form', () => {
    const parsed = parseLocalDateTimeInput('2026-11-22T14:30:05');
    expect(parsed).not.toBeNull();
    expect(parsed!.getFullYear()).toBe(2026);
    expect(parsed!.getMonth()).toBe(10);
    expect(parsed!.getDate()).toBe(22);
    expect(parsed!.getHours()).toBe(14);
    expect(parsed!.getMinutes()).toBe(30);
    expect(parsed!.getSeconds()).toBe(5);
  });

  it('defaults missing seconds to zero', () => {
    const parsed = parseLocalDateTimeInput('2026-11-22T14:30');
    expect(parsed!.getSeconds()).toBe(0);
  });

  it('rejects impossible dates and times', () => {
    expect(parseLocalDateTimeInput('2026-02-30T10:00:00')).toBeNull();
    expect(parseLocalDateTimeInput('2026-11-22T24:00:00')).toBeNull();
    expect(parseLocalDateTimeInput('2026-11-22T10:60:00')).toBeNull();
    expect(parseLocalDateTimeInput('22-11-2026 10:00:00')).toBeNull();
  });

  it('rejects local times skipped by the spring DST transition', () => {
    // Europe/Warsaw jumps from 02:00 to 03:00 on 29 March 2026.
    expect(buildLocalDateTime(2026, 3, 29, 2, 30, 0)).toBeNull();
    expect(buildLocalDateTime(2026, 3, 29, 3, 30, 0)).not.toBeNull();
    expect(parseLocalDateTimeInput('2026-03-29T02:30:00')).toBeNull();
    expect(parseLocalDateTimeInput('2026-03-29T03:30:00')).not.toBeNull();
  });

  it('rejects impossible local calendar values', () => {
    expect(buildLocalDateTime(2026, 2, 31, 10, 0, 0)).toBeNull();
    expect(buildLocalDateTime(2026, 13, 1, 10, 0, 0)).toBeNull();
    expect(buildLocalDateTime(2026, 11, 22, 24, 0, 0)).toBeNull();
  });

  it('accepts times repeated by the autumn DST transition', () => {
    expect(parseLocalDateTimeInput('2026-10-25T02:30:00')).not.toBeNull();
  });

  it('parses the visible dd-mm-yyyy form', () => {
    const parsed = parseDisplayDateTime('22-11-2026 14:30:05');
    expect(parsed).not.toBeNull();
    expect(parsed!.getHours()).toBe(14);

    const dateOnly = parseDisplayDateTime('22-11-2026');
    expect(dateOnly!.getHours()).toBe(0);
    expect(dateOnly!.getMinutes()).toBe(0);
  });

  it('rejects the visible form with an impossible day', () => {
    expect(parseDisplayDateTime('31-02-2026 10:00:00')).toBeNull();
  });

  it('converts between local form values and display values', () => {
    const value = new Date(2026, 10, 22, 14, 30, 5);
    expect(toLocalDateTimeInput(value)).toBe('2026-11-22T14:30:05');
    expect(toDisplayDateTime(value)).toBe('22-11-2026 14:30:05');
    expect(toLocalDateTimeInput('nope')).toBe('');
    expect(toDisplayDateTime('nope')).toBe('');
  });
});

describe('day and quick-range helpers', () => {
  const now = new Date(2026, 10, 22, 12, 34, 56, 789);

  it('computes local day boundaries', () => {
    expect(toDisplayDateTime(startOfDay(now)!)).toBe('22-11-2026 00:00:00');
    expect(toDisplayDateTime(endOfDay(now)!)).toBe('22-11-2026 23:59:59');
  });

  it('compares local days', () => {
    expect(isSameLocalDay(now, new Date(2026, 10, 22, 23, 59, 59))).toBe(true);
    expect(isSameLocalDay(now, new Date(2026, 10, 23, 0, 0, 0))).toBe(false);
  });

  it('creates stable day keys', () => {
    expect(dayKey(now)).toBe(dayKey(new Date(2026, 10, 22, 0, 0, 0)));
    expect(dayKey(now)).not.toBe(dayKey(new Date(2026, 10, 23)));
  });

  it('computes the Today quick range', () => {
    const range = quickRange('today', now);
    expect(toDisplayDateTime(range.from)).toBe('22-11-2026 00:00:00');
    expect(toDisplayDateTime(range.to)).toBe('22-11-2026 12:34:56');
  });

  it('computes the Yesterday quick range', () => {
    const range = quickRange('yesterday', now);
    expect(toDisplayDateTime(range.from)).toBe('21-11-2026 00:00:00');
    expect(toDisplayDateTime(range.to)).toBe('21-11-2026 23:59:59');
  });

  it('computes a rolling seven-day range', () => {
    const range = quickRange('last7days', now);
    expect(range.to.getTime()).toBe(now.getTime());
    expect(range.from.getTime()).toBe(
      now.getTime() - 7 * 24 * 60 * 60 * 1000,
    );
  });

  it('turns the last second of a day into the next exclusive midnight', () => {
    expect(
      toDisplayDateTime(toExclusiveEnd(new Date(2026, 10, 22, 23, 59, 59))),
    ).toBe('23-11-2026 00:00:00');
    expect(
      toDisplayDateTime(toExclusiveEnd(new Date(2026, 10, 22, 12, 0, 0))),
    ).toBe('22-11-2026 12:00:00');
  });

  it('adds calendar days across month boundaries', () => {
    expect(toDisplayDateTime(addDays(new Date(2026, 10, 30), 2)!)).toBe(
      '02-12-2026 00:00:00',
    );
  });
});
