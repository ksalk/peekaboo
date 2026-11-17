import { execFileSync } from 'node:child_process';
import { rtspPort } from './env';

/**
 * Counts server-side ffmpeg pull processes for a given MediaMTX path. The test
 * publisher runs under the same machine but uses lavfi as input, so requiring
 * "-i rtsp://" isolates Peekaboo's own stream sessions.
 */
export function consumerProcessCount(feedPath: string): number {
  if (process.platform === 'win32') {
    throw new Error('Process assertions require Linux or macOS; run the e2e suite there.');
  }

  const output = execFileSync('ps', ['-ww', '-eo', 'args'], { encoding: 'utf8' });
  const marker = `:${rtspPort}/${feedPath}`;

  return output
    .split('\n')
    .map((line) => line.trim())
    .filter(
      (line) =>
        (line.startsWith('ffmpeg ') || line.includes('/ffmpeg ')) &&
        line.includes('-i rtsp://') &&
        line.includes(marker) &&
        !line.includes('lavfi'),
    ).length;
}
