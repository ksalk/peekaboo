import { expect, test, type Route } from '@playwright/test';
import { createCameraFixture } from './support/api';
import { feeds, uniqueName } from './support/env';
import { openPlayback } from './support/ui';

const delayMs = 700;

async function delayRoute(route: Route): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, delayMs));
  await route.continue();
}

test('shows progress while searching recordings', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Playback loading'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await page.route(/\/api\/cameras\/[^/]+\/recordings\?/, delayRoute);

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search recordings' }).click();

  const status = page.locator('.search-status');
  await expect(status).toBeVisible();
  await expect(status).toContainText('Searching recordings…');

  await expect(page.locator('.playback-timeline')).toBeVisible();
  await expect(status).toHaveCount(0);
});
