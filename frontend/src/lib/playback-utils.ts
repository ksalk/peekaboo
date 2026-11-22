import type { RecordingSegment } from './types';

export function parseTime(value: string): number | null {
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.round(seconds));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const remainder = total % 60;

  if (hours > 0) {
    return `${hours}h ${minutes.toString().padStart(2, '0')}m`;
  }

  if (minutes > 0) {
    return `${minutes}m ${remainder.toString().padStart(2, '0')}s`;
  }

  return `${remainder}s`;
}

export function segmentsOverlap(
  left: RecordingSegment,
  right: RecordingSegment,
): boolean {
  const leftStart = parseTime(left.start);
  const leftEnd = parseTime(left.end);
  const rightStart = parseTime(right.start);
  const rightEnd = parseTime(right.end);

  if (
    leftStart === null ||
    leftEnd === null ||
    rightStart === null ||
    rightEnd === null
  ) {
    return false;
  }

  return leftStart < rightEnd && leftEnd > rightStart;
}
