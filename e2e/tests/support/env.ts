export const adminUsername = process.env.PEEKABOO_E2E_ADMIN_USERNAME ?? 'admin';

export const adminPassword =
  process.env.PEEKABOO_E2E_ADMIN_PASSWORD ?? 'peekaboo-e2e-password';

export const rtspPort = 8555;

export const feedUrl = (path: string): string => `rtsp://127.0.0.1:${rtspPort}/${path}`;

let sequence = 0;

export function uniqueName(prefix: string): string {
  sequence += 1;
  return `${prefix}-${Date.now()}-${sequence}`;
}
