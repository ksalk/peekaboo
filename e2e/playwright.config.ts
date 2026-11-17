import { defineConfig } from '@playwright/test';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const rootDirectory = fileURLToPath(new URL('.', import.meta.url));
const baseURL = process.env.PEEKABOO_E2E_BASE_URL ?? 'http://127.0.0.1:5173';
const authFile = `${rootDirectory}.auth/admin.json`;

const explicitChromiumPath = process.env.PEEKABOO_CHROMIUM_PATH;

if (explicitChromiumPath && !existsSync(explicitChromiumPath)) {
  throw new Error(`PEEKABOO_CHROMIUM_PATH does not exist: ${explicitChromiumPath}`);
}

const chromiumCandidates = [
  explicitChromiumPath,
  '/usr/bin/google-chrome-stable',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
].filter((candidate): candidate is string => Boolean(candidate));

const executablePath = chromiumCandidates.find((candidate) => existsSync(candidate));

const browserArgs = ['--no-sandbox', '--autoplay-policy=no-user-gesture-required'];

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  timeout: 120_000,
  expect: {
    timeout: 15_000,
  },
  reporter: [['list']],
  forbidOnly: true,
  retries: process.env.CI ? 1 : 0,
  globalSetup: './global-setup.ts',
  use: {
    baseURL,
    storageState: authFile,
    viewport: { width: 1280, height: 900 },
    trace: 'retain-on-failure',
    ...(executablePath
      ? { launchOptions: { executablePath, args: browserArgs } }
      : { channel: 'chrome', launchOptions: { args: browserArgs } }),
  },
});
