import { expect, test } from '@playwright/test';
import { createCamera } from './support/api';
import { adminPassword, adminUsername, uniqueName } from './support/env';
import {
  assignCamera,
  expectConsumerCount,
  expectPixelsChanging,
  feedCell,
  openFeeds,
  selectLayout,
  startCell,
  stopCell,
  waitForPlaying,
} from './support/ui';

test('plays video only after Start and kills the server stream on Stop', async ({
  page,
  request,
}) => {
  const camera = await createCamera(request, uniqueName('Front door'), 'feed1');

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await expectConsumerCount('feed1', 0);

  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectPixelsChanging(page, 0);
  await expectConsumerCount('feed1', 1);

  await stopCell(page, 0);
  await expectConsumerCount('feed1', 0);
  await expect(feedCell(page, 0).locator('video')).not.toHaveAttribute('src', /.+/);
});

test('two cells watching the same camera share one ffmpeg process', async ({ page, request }) => {
  const camera = await createCamera(request, uniqueName('Shared camera'), 'feed1');

  await openFeeds(page);
  await selectLayout(page, '2 × 2');
  await assignCamera(page, 0, camera.name);
  await assignCamera(page, 1, camera.name);
  await expectConsumerCount('feed1', 0);

  await startCell(page, 0);
  await startCell(page, 1);
  await waitForPlaying(page, 0);
  await waitForPlaying(page, 1);
  await expectConsumerCount('feed1', 1);

  await stopCell(page, 0);
  await expectConsumerCount('feed1', 1);

  await stopCell(page, 1);
  await expectConsumerCount('feed1', 0);
});

test('Stop all tears down every active stream', async ({ page, request }) => {
  const first = await createCamera(request, uniqueName('First feed'), 'feed1');
  const second = await createCamera(request, uniqueName('Second feed'), 'feed2');

  await openFeeds(page);
  await selectLayout(page, '2 × 2');
  await assignCamera(page, 0, first.name);
  await assignCamera(page, 1, second.name);

  await startCell(page, 0);
  await startCell(page, 1);
  await waitForPlaying(page, 0);
  await waitForPlaying(page, 1);
  await expectConsumerCount('feed1', 1);
  await expectConsumerCount('feed2', 1);

  await page.getByRole('button', { name: 'Stop all' }).click();

  await expectConsumerCount('feed1', 0);
  await expectConsumerCount('feed2', 0);
  await expect(feedCell(page, 0).getByRole('button', { name: 'Start', exact: true })).toBeVisible();
  await expect(feedCell(page, 1).getByRole('button', { name: 'Start', exact: true })).toBeVisible();
});

test('hiding the tab stops streams immediately', async ({ page, request }) => {
  const camera = await createCamera(request, uniqueName('Hidden tab'), 'feed1');

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount('feed1', 1);

  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });
    document.dispatchEvent(new Event('visibilitychange'));
  });

  await expectConsumerCount('feed1', 0);
  await expect(feedCell(page, 0).getByRole('button', { name: 'Start', exact: true })).toBeVisible();
});

test('navigating away and signing out stop streams', async ({ page, request }) => {
  const camera = await createCamera(request, uniqueName('Navigation'), 'feed1');

  // Use a dedicated session so signing out does not invalidate the shared
  // storage-state session used by the rest of the suite.
  const login = await page.request.post('/api/auth/login', {
    data: { username: adminUsername, password: adminPassword },
  });
  expect(login.ok()).toBeTruthy();

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount('feed1', 1);

  await page.getByRole('link', { name: 'Cameras', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Cameras' })).toBeVisible();
  await expectConsumerCount('feed1', 0);

  await page.getByRole('link', { name: 'Feeds', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Feeds' })).toBeVisible();
  await expect(feedCell(page, 0).getByRole('button', { name: 'Start', exact: true })).toBeVisible();

  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount('feed1', 1);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Peekaboo' })).toBeVisible();
  await expectConsumerCount('feed1', 0);
});

test('layout and cell assignments persist across reloads', async ({ page, request }) => {
  const camera = await createCamera(request, uniqueName('Persistent'), 'feed2');

  await openFeeds(page);
  await selectLayout(page, '2 × 2');
  await assignCamera(page, 1, camera.name);

  await page.reload();
  await expect(page.getByRole('heading', { name: 'Feeds' })).toBeVisible();
  await expect(page.locator('.feed-cell')).toHaveCount(4);
  await expect(feedCell(page, 1).locator('.feed-name')).toHaveText(camera.name);
  await expect(feedCell(page, 1).getByRole('button', { name: 'Start', exact: true })).toBeVisible();

  await selectLayout(page, '3 × 3');
  await expect(page.locator('.feed-cell')).toHaveCount(9);

  await selectLayout(page, '2 × 2');
  await expect(feedCell(page, 1).locator('.feed-name')).toHaveText(camera.name);
});

test('corrupt local storage does not break the grid', async ({ page, request }) => {
  await createCamera(request, uniqueName('Storage guard'), 'feed1');
  const pageErrors: string[] = [];
  page.on('pageerror', (error) => pageErrors.push(String(error)));

  await page.addInitScript(() => {
    localStorage.setItem('peekaboo.layout', 'not-a-layout');
    localStorage.setItem('peekaboo.assignments.1x1', '{{{not-json');
  });

  await openFeeds(page);

  await expect(page.locator('.feed-cell')).toHaveCount(1);
  await expect(page.locator('.feed-cell button.feed-center')).toBeVisible();
  expect(pageErrors).toEqual([]);
});

test('unreachable cameras show an error and leave no process behind', async ({
  page,
  request,
}) => {
  const camera = await createCamera(request, uniqueName('Broken feed'), 'missing-stream');

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);

  const error = feedCell(page, 0).locator('.feed-error');
  await expect(error).toBeVisible({ timeout: 30_000 });
  await expect(error).toContainText(/failed|not found|404/i);
  await expectConsumerCount('missing-stream', 0);
  await expect(feedCell(page, 0).getByRole('button', { name: 'Retry', exact: true })).toBeVisible();
});

test('changing the layout stops active streams', async ({ page, request }) => {
  const camera = await createCamera(request, uniqueName('Layout switch'), 'feed1');

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount('feed1', 1);

  await selectLayout(page, '3 × 3');

  await expectConsumerCount('feed1', 0);
  await expect(page.locator('.feed-cell')).toHaveCount(9);
  await expect(feedCell(page, 0).locator('button.feed-center')).toBeVisible();
});

test('grids collapse to one column on mobile', async ({ page, request }) => {
  await createCamera(request, uniqueName('Mobile'), 'feed1');
  await page.setViewportSize({ width: 390, height: 844 });

  await openFeeds(page);
  await selectLayout(page, '2 × 2');
  await expect(page.locator('.feed-cell')).toHaveCount(4);

  const boxes = await page.locator('.feed-cell').evaluateAll((cells) =>
    cells.slice(0, 2).map((cell) => {
      const rect = cell.getBoundingClientRect();
      return { x: rect.x, y: rect.y, width: rect.width };
    }),
  );

  expect(boxes).toHaveLength(2);
  expect(boxes[1].y).toBeGreaterThan(boxes[0].y + 50);
  expect(Math.abs(boxes[1].x - boxes[0].x)).toBeLessThan(2);
  expect(boxes[0].width).toBeGreaterThan(300);
});
