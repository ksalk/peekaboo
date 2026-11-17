import { request } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { adminPassword, adminUsername } from './tests/support/env';

const rootDirectory = fileURLToPath(new URL('.', import.meta.url));
const baseURL = process.env.PEEKABOO_E2E_BASE_URL ?? 'http://127.0.0.1:5173';

export default async function globalSetup(): Promise<void> {
  const context = await request.newContext({ baseURL });

  try {
    let healthy = false;

    for (let attempt = 0; attempt < 60; attempt++) {
      try {
        const response = await context.get('/api/health');
        if (response.ok()) {
          healthy = true;
          break;
        }
      } catch {
        // The app is still starting.
      }

      await new Promise((resolve) => setTimeout(resolve, 500));
    }

    if (!healthy) {
      throw new Error(`Peekaboo did not become healthy at ${baseURL}.`);
    }

    const login = await context.post('/api/auth/login', {
      data: { username: adminUsername, password: adminPassword },
    });

    if (!login.ok()) {
      throw new Error(`E2E admin login failed: ${login.status()} ${await login.text()}`);
    }

    await mkdir(`${rootDirectory}.auth`, { recursive: true });
    await context.storageState({ path: `${rootDirectory}.auth/admin.json` });
  } finally {
    await context.dispose();
  }
}
