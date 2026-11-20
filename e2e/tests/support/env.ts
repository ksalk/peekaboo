export const adminUsername = process.env.PEEKABOO_E2E_ADMIN_USERNAME ?? 'admin';

export const adminPassword =
  process.env.PEEKABOO_E2E_ADMIN_PASSWORD ?? 'peekaboo-e2e-password';

export const rtspPort = 8555;

export const nvrBaseUrl = `rtsp://127.0.0.1:${rtspPort}`;

export const feeds = {
  main: { streamId: '101', path: 'ISAPI/Streaming/channels/101' },
  sub: { streamId: '102', path: 'ISAPI/Streaming/channels/102' },
  second: { streamId: '201', path: 'ISAPI/Streaming/channels/201' },
  missing: { streamId: '999', path: 'ISAPI/Streaming/channels/999' },
  playback: { path: 'ISAPI/Streaming/tracks/101' },
} as const;

let sequence = 0;

export function uniqueName(prefix: string): string {
  sequence += 1;
  return `${prefix}-${Date.now()}-${sequence}`;
}
