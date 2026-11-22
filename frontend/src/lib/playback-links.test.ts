import { describe, expect, it } from 'vitest';
import { buildPlaybackLink, readPlaybackParams } from './playback-links';

describe('playback links', () => {
  it('builds a search window with pre-roll and post-roll', () => {
    const link = buildPlaybackLink({
      cameraId: 'cam-1',
      start: '2026-11-22T10:00:00.000Z',
      end: '2026-11-22T10:00:07.000Z',
      at: '2026-11-22T10:00:07.000Z',
      autoplay: true,
    });

    expect(link).not.toBeNull();
    const params = new URLSearchParams(link!.split('?')[1]);
    expect(params.get('cameraId')).toBe('cam-1');
    expect(params.get('start')).toBe('2026-11-22T09:59:50.000Z');
    expect(params.get('end')).toBe('2026-11-22T10:00:50.000Z');
    expect(params.get('at')).toBe('2026-11-22T10:00:07.000Z');
    expect(params.get('autoplay')).toBe('1');
  });

  it('enforces a minimum search window', () => {
    const link = buildPlaybackLink({
      cameraId: 'cam-1',
      start: '2026-11-22T10:00:00.000Z',
      end: '2026-11-22T10:00:00.000Z',
    });

    const params = new URLSearchParams(link!.split('?')[1]);
    expect(
      new Date(params.get('end')!).getTime() -
        new Date(params.get('start')!).getTime(),
    ).toBe(60_000);
  });

  it('caps the search window at 24 hours', () => {
    const link = buildPlaybackLink({
      cameraId: 'cam-1',
      start: '2026-11-20T00:00:00.000Z',
      end: '2026-11-23T00:00:00.000Z',
    });

    const params = new URLSearchParams(link!.split('?')[1]);
    expect(
      new Date(params.get('end')!).getTime() -
        new Date(params.get('start')!).getTime(),
    ).toBe(24 * 60 * 60 * 1000);
  });

  it('rejects an invalid camera or timestamp', () => {
    expect(
      buildPlaybackLink({
        cameraId: '',
        start: '2026-11-22T10:00:00.000Z',
        end: '2026-11-22T10:00:07.000Z',
      }),
    ).toBeNull();
    expect(
      buildPlaybackLink({
        cameraId: 'cam-1',
        start: 'nope',
        end: '2026-11-22T10:00:07.000Z',
      }),
    ).toBeNull();
  });

  it('reads encoded and unencoded offsets', () => {
    const parsed = readPlaybackParams(
      '#/playback?cameraId=cam-1&start=2026-11-22T10%3A00%3A00%2B01%3A00' +
        '&end=2026-11-22T11%3A00%3A00%2B01%3A00&at=2026-11-22T10%3A00%3A00%2B01%3A00&autoplay=1',
    );

    expect(parsed.cameraId).toBe('cam-1');
    expect(parsed.start).toBe('2026-11-22T10:00:00+01:00');
    expect(parsed.autoplay).toBe(true);
  });

  it('returns nulls for an empty query', () => {
    expect(readPlaybackParams('#/playback')).toEqual({
      cameraId: null,
      start: null,
      end: null,
      at: null,
      autoplay: false,
    });
  });
});
