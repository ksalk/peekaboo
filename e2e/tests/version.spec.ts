import { readFileSync } from 'node:fs';
import { expect, test } from '@playwright/test';

const version = readFileSync(new URL('../../VERSION', import.meta.url), 'utf8').trim();

test('reports the release version in the footer and health endpoint', async ({ page }) => {
  await page.goto('/');

  await expect(page.locator('.app-footer .app-version')).toHaveText(`v${version}`);

  const response = await page.request.get('/api/health');
  expect(response.ok()).toBe(true);

  const health = (await response.json()) as { status: string; version: string };
  expect(health.status).toBe('ok');
  expect(health.version).toBe(version);
});
