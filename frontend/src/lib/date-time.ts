/**
 * Local date/time helpers used across the UI.
 *
 * API timestamps are ISO 8601 instants. Everything the user sees is formatted
 * explicitly in the browser's local time, in 24-hour `dd-mm-yyyy` form. Do not
 * use `toLocaleString` for these values: browser locales can switch to 12-hour
 * clocks and month-first dates.
 */

export type DateInput = Date | string | number;
export type QuickRangeName = 'today' | 'yesterday' | 'last7days';

const pad2 = (value: number): string => value.toString().padStart(2, '0');

function toDate(value: DateInput): Date | null {
  const date = value instanceof Date ? new Date(value.getTime()) : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

export function isValidDate(value: DateInput): boolean {
  return toDate(value) !== null;
}

export function formatDate(value: DateInput): string {
  const date = toDate(value);
  return date
    ? `${pad2(date.getDate())}-${pad2(date.getMonth() + 1)}-${date.getFullYear()}`
    : '—';
}

export function formatTime(value: DateInput, includeSeconds = true): string {
  const date = toDate(value);
  if (!date) {
    return '—';
  }

  const clock = `${pad2(date.getHours())}:${pad2(date.getMinutes())}`;
  return includeSeconds ? `${clock}:${pad2(date.getSeconds())}` : clock;
}

export function formatDateTime(value: DateInput, includeSeconds = true): string {
  const date = toDate(value);
  if (!date) {
    return '—';
  }

  return `${formatDate(date)} ${formatTime(date, includeSeconds)}`;
}

export function formatDateRange(
  start: DateInput,
  end: DateInput,
  includeSeconds = true,
): string {
  const startDate = toDate(start);
  const endDate = toDate(end);

  if (!startDate || !endDate) {
    return '—';
  }

  if (isSameLocalDay(startDate, endDate)) {
    return `${formatDate(startDate)} · ${formatTime(startDate, includeSeconds)}–${formatTime(endDate, includeSeconds)}`;
  }

  return (
    `${formatDate(startDate)} ${formatTime(startDate, includeSeconds)} – ` +
    `${formatDate(endDate)} ${formatTime(endDate, includeSeconds)}`
  );
}

/** Compact timeline tick: `22-11 14:00` when the range spans days, else `14:00`. */
export function formatTimelineTick(ms: number, includeDate: boolean): string {
  const date = toDate(ms);
  if (!date) {
    return '—';
  }

  const clock = `${pad2(date.getHours())}:${pad2(date.getMinutes())}`;
  return includeDate
    ? `${pad2(date.getDate())}-${pad2(date.getMonth() + 1)} ${clock}`
    : clock;
}

export function isSameLocalDay(left: DateInput, right: DateInput): boolean {
  const leftDate = toDate(left);
  const rightDate = toDate(right);

  return (
    leftDate !== null &&
    rightDate !== null &&
    leftDate.getFullYear() === rightDate.getFullYear() &&
    leftDate.getMonth() === rightDate.getMonth() &&
    leftDate.getDate() === rightDate.getDate()
  );
}

export function dayKey(value: DateInput): string {
  const date = toDate(value);
  return date
    ? `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`
    : '';
}

export function formatDayLabel(
  value: DateInput,
  reference: DateInput = new Date(),
): string {
  const date = toDate(value);
  const today = toDate(reference);

  if (!date || !today) {
    return '—';
  }

  if (isSameLocalDay(date, today)) {
    return 'Today';
  }

  const yesterday = addDays(today, -1);
  if (yesterday && isSameLocalDay(date, yesterday)) {
    return 'Yesterday';
  }

  return formatDate(date);
}

export function startOfDay(value: DateInput): Date | null {
  const date = toDate(value);
  if (!date) {
    return null;
  }

  const result = new Date(date.getTime());
  result.setHours(0, 0, 0, 0);
  return result;
}

/**
 * Last whole second of the local day. Consumers that send an exclusive end to
 * the API must add one second (the Playback/Alarms search builders do this).
 */
export function endOfDay(value: DateInput): Date | null {
  const date = toDate(value);
  if (!date) {
    return null;
  }

  const result = new Date(date.getTime());
  result.setHours(23, 59, 59, 0);
  return result;
}

export function addDays(value: DateInput, days: number): Date | null {
  const date = toDate(value);
  if (!date) {
    return null;
  }

  const result = new Date(date.getTime());
  result.setDate(result.getDate() + days);
  return result;
}

export function addSeconds(value: DateInput, seconds: number): Date | null {
  const date = toDate(value);
  return date ? new Date(date.getTime() + seconds * 1000) : null;
}

export function quickRange(
  name: QuickRangeName,
  now: DateInput = new Date(),
): { from: Date; to: Date } {
  const current = toDate(now) ?? new Date();

  if (name === 'yesterday') {
    const day = addDays(current, -1) ?? current;
    return {
      from: startOfDay(day) ?? day,
      to: endOfDay(day) ?? day,
    };
  }

  if (name === 'last7days') {
    return {
      from: new Date(current.getTime() - 7 * 24 * 60 * 60 * 1000),
      to: new Date(current.getTime()),
    };
  }

  return {
    from: startOfDay(current) ?? current,
    to: new Date(current.getTime()),
  };
}

/** Internal form value: `YYYY-MM-DDTHH:mm:ss` in local time. */
export function toLocalDateTimeInput(value: DateInput): string {
  const date = toDate(value);
  if (!date) {
    return '';
  }

  return (
    `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}` +
    `T${pad2(date.getHours())}:${pad2(date.getMinutes())}:${pad2(date.getSeconds())}`
  );
}

/** Visible form value: `dd-mm-yyyy HH:mm:ss`. */
export function toDisplayDateTime(value: DateInput): string {
  const date = toDate(value);
  if (!date) {
    return '';
  }

  return `${formatDate(date)} ${formatTime(date, true)}`;
}

/**
 * Parses the internal `YYYY-MM-DDTHH:mm[:ss]` form as local time. Returns null
 * for impossible values, including local times skipped by a DST transition.
 */
export function parseLocalDateTimeInput(value: string): Date | null {
  const match =
    /^(\d{4})-(\d{2})-(\d{2})[T ](\d{1,2}):(\d{2})(?::(\d{2}))?$/.exec(value.trim());
  if (!match) {
    return null;
  }

  return buildLocalDateTime(
    Number(match[1]),
    Number(match[2]),
    Number(match[3]),
    Number(match[4]),
    Number(match[5]),
    Number(match[6] ?? '0'),
  );
}

/**
 * Parses the visible `dd-mm-yyyy HH:mm[:ss]` form. A date without a time is
 * accepted and treated as local midnight.
 */
export function parseDisplayDateTime(value: string): Date | null {
  const match =
    /^(\d{1,2})-(\d{1,2})-(\d{4})(?:[ T](\d{1,2}):(\d{2})(?::(\d{2}))?)?$/.exec(
      value.trim(),
    );
  if (!match) {
    return null;
  }

  return buildLocalDateTime(
    Number(match[3]),
    Number(match[2]),
    Number(match[1]),
    Number(match[4] ?? '0'),
    Number(match[5] ?? '0'),
    Number(match[6] ?? '0'),
  );
}

/**
 * Builds a local Date only when every component survives the Date
 * constructor. This rejects impossible dates and wall times skipped by a DST
 * transition.
 */
export function buildLocalDateTime(
  year: number,
  month: number,
  day: number,
  hours: number,
  minutes: number,
  seconds: number,
): Date | null {
  if (
    !Number.isInteger(year) ||
    !Number.isInteger(month) ||
    !Number.isInteger(day) ||
    !Number.isInteger(hours) ||
    !Number.isInteger(minutes) ||
    !Number.isInteger(seconds) ||
    hours < 0 ||
    hours > 23 ||
    minutes < 0 ||
    minutes > 59 ||
    seconds < 0 ||
    seconds > 59
  ) {
    return null;
  }

  const value = new Date(year, month - 1, day, hours, minutes, seconds, 0);

  if (
    Number.isNaN(value.getTime()) ||
    value.getFullYear() !== year ||
    value.getMonth() !== month - 1 ||
    value.getDate() !== day ||
    value.getHours() !== hours ||
    value.getMinutes() !== minutes ||
    value.getSeconds() !== seconds
  ) {
    return null;
  }

  return value;
}
