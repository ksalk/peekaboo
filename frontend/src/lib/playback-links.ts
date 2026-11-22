/**
 * Builds a Playback deep link around an event or alarm.
 *
 * The search window gets a short pre-roll and post-roll so the user sees the
 * moments around the event. `at` tells Playback which instant to start from
 * when auto-play is enabled.
 */
export function buildPlaybackLink(options: {
  cameraId: string;
  start: string;
  end: string;
  at?: string;
  autoplay?: boolean;
}): string | null {
  const start = new Date(options.start);
  const end = new Date(options.end);
  const at = options.at ? new Date(options.at) : start;

  if (
    !options.cameraId ||
    Number.isNaN(start.getTime()) ||
    Number.isNaN(end.getTime()) ||
    Number.isNaN(at.getTime())
  ) {
    return null;
  }

  const searchStart = new Date(start.getTime() - 10_000);
  let searchEnd = new Date(end.getTime() + 30_000);

  if (searchEnd.getTime() - searchStart.getTime() < 60_000) {
    searchEnd = new Date(searchStart.getTime() + 60_000);
  }

  if (searchEnd.getTime() - searchStart.getTime() > 24 * 60 * 60 * 1000) {
    searchEnd = new Date(searchStart.getTime() + 24 * 60 * 60 * 1000);
  }

  const params = new URLSearchParams({
    cameraId: options.cameraId,
    start: searchStart.toISOString(),
    end: searchEnd.toISOString(),
    at: at.toISOString(),
  });

  if (options.autoplay) {
    params.set('autoplay', '1');
  }

  return `#/playback?${params.toString()}`;
}

export function readPlaybackParams(
  hash: string,
): {
  cameraId: string | null;
  start: string | null;
  end: string | null;
  at: string | null;
  autoplay: boolean;
} {
  const raw = hash.replace(/^#\/?/, '');
  const separator = raw.indexOf('?');
  const value = separator >= 0 ? raw.slice(separator + 1) : '';
  // Shared links may contain an unencoded +hh:mm offset; URLSearchParams
  // would turn '+' into a space.
  const params = new URLSearchParams(value.replaceAll('+', '%2B'));

  return {
    cameraId: params.get('cameraId'),
    start: params.get('start'),
    end: params.get('end'),
    at: params.get('at'),
    autoplay: params.get('autoplay') === '1',
  };
}
