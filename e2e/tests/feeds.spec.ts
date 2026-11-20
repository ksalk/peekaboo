import { expect, test } from '@playwright/test';
import { createCameraFixture } from './support/api';
import { adminPassword, adminUsername, feeds, uniqueName } from './support/env';
import {
  assignCamera,
  expectConsumerCount,
  expectPixelsChanging,
  feedCell,
  openFeeds,
  selectLayout,
  selectStream,
  startCell,
  stopCell,
  waitForPlaying,
} from './support/ui';

test('plays video only after Start and kills the server stream on Stop', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Front door'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await expectConsumerCount(feeds.sub.path, 0);

  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectPixelsChanging(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);

  await stopCell(page, 0);
  await expectConsumerCount(feeds.sub.path, 0);
  await expect(feedCell(page, 0).locator('video')).not.toHaveAttribute('src', /.+/);
});

test('switches a live cell between sub and main streams', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Quality switch'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);
  await expectConsumerCount(feeds.main.path, 0);

  const subWidth = await feedCell(page, 0)
    .locator('video')
    .evaluate((video: HTMLVideoElement) => video.videoWidth);
  expect(subWidth).toBe(320);

  await selectStream(page, 0, 'main');
  await waitForPlaying(page, 0);
  await expectConsumerCount(feeds.main.path, 1);
  await expectConsumerCount(feeds.sub.path, 0);

  const mainWidth = await feedCell(page, 0)
    .locator('video')
    .evaluate((video: HTMLVideoElement) => video.videoWidth);
  expect(mainWidth).toBe(640);

  await stopCell(page, 0);
  await expectConsumerCount(feeds.main.path, 0);
});

test('two cells watching the same camera share one ffmpeg process', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Shared camera'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await selectLayout(page, '2 × 2');
  await assignCamera(page, 0, camera.name);
  await assignCamera(page, 1, camera.name);
  await expectConsumerCount(feeds.sub.path, 0);

  await startCell(page, 0);
  await startCell(page, 1);
  await waitForPlaying(page, 0);
  await waitForPlaying(page, 1);
  await expectConsumerCount(feeds.sub.path, 1);

  await stopCell(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);

  await stopCell(page, 1);
  await expectConsumerCount(feeds.sub.path, 0);
});

test('main and sub of the same camera run independently', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Independent streams'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await selectLayout(page, '2 × 2');
  await assignCamera(page, 0, camera.name);
  await assignCamera(page, 1, camera.name);
  await selectStream(page, 1, 'main');

  await startCell(page, 0);
  await startCell(page, 1);
  await waitForPlaying(page, 0);
  await waitForPlaying(page, 1);
  await expectConsumerCount(feeds.sub.path, 1);
  await expectConsumerCount(feeds.main.path, 1);

  await stopCell(page, 0);
  await expectConsumerCount(feeds.sub.path, 0);
  await expectConsumerCount(feeds.main.path, 1);

  await stopCell(page, 1);
  await expectConsumerCount(feeds.main.path, 0);
});

test('Stop all tears down every active stream', async ({ page, request }) => {
  const first = await createCameraFixture(
    request,
    uniqueName('First feed'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );
  const second = await createCameraFixture(
    request,
    uniqueName('Second feed'),
    '301',
    feeds.second.streamId,
  );

  await openFeeds(page);
  await selectLayout(page, '2 × 2');
  await assignCamera(page, 0, first.name);
  await assignCamera(page, 1, second.name);

  await startCell(page, 0);
  await startCell(page, 1);
  await waitForPlaying(page, 0);
  await waitForPlaying(page, 1);
  await expectConsumerCount(feeds.sub.path, 1);
  await expectConsumerCount(feeds.second.path, 1);

  await page.getByRole('button', { name: 'Stop all' }).click();

  await expectConsumerCount(feeds.sub.path, 0);
  await expectConsumerCount(feeds.second.path, 0);
  await expect(feedCell(page, 0).getByRole('button', { name: 'Start', exact: true })).toBeVisible();
  await expect(feedCell(page, 1).getByRole('button', { name: 'Start', exact: true })).toBeVisible();
});

test('hiding the tab stops streams immediately', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Hidden tab'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);

  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', { configurable: true, get: () => true });
    document.dispatchEvent(new Event('visibilitychange'));
  });

  await expectConsumerCount(feeds.sub.path, 0);
  await expect(feedCell(page, 0).getByRole('button', { name: 'Start', exact: true })).toBeVisible();
});

test('navigating away and signing out stop streams', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Navigation'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

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
  await expectConsumerCount(feeds.sub.path, 1);

  await page.getByRole('link', { name: 'NVRs', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'NVRs', exact: true })).toBeVisible();
  await expectConsumerCount(feeds.sub.path, 0);

  await page.getByRole('link', { name: 'Feeds', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Feeds' })).toBeVisible();
  await expect(feedCell(page, 0).getByRole('button', { name: 'Start', exact: true })).toBeVisible();

  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);

  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect(page.getByRole('heading', { name: 'Peekaboo' })).toBeVisible();
  await expectConsumerCount(feeds.sub.path, 0);
});

test('layout and cell assignments persist across reloads', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Persistent'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

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
  await createCameraFixture(
    request,
    uniqueName('Storage guard'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );
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

test('deleting a camera stops its live stream', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Deleted camera'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);

  const response = await request.delete(
    `/api/nvrs/${encodeURIComponent(camera.nvrId)}/cameras/${encodeURIComponent(camera.id)}`,
  );
  expect(response.status()).toBe(204);

  await expectConsumerCount(feeds.sub.path, 0);
  await expect(feedCell(page, 0).locator('.feed-error')).toContainText(/deleted/i, {
    timeout: 30_000,
  });
});

test('deleting an NVR stops its camera streams', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Deleted NVR camera'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);

  const response = await request.delete(`/api/nvrs/${encodeURIComponent(camera.nvrId)}`);
  expect(response.status()).toBe(204);

  await expectConsumerCount(feeds.sub.path, 0);
  await expect(feedCell(page, 0).locator('.feed-error')).toContainText(/NVR was deleted/i, {
    timeout: 30_000,
  });
});

test('unreachable cameras show an error and leave no process behind', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Broken feed'),
    '998',
    feeds.missing.streamId,
  );

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);

  const error = feedCell(page, 0).locator('.feed-error');
  await expect(error).toBeVisible({ timeout: 30_000 });
  await expect(error).toContainText(/failed|not found|404/i);
  await expect(error).not.toContainText('campassword');
  await expectConsumerCount(feeds.missing.path, 0);
  await expect(feedCell(page, 0).getByRole('button', { name: 'Retry', exact: true })).toBeVisible();
});

test('changing the layout stops active streams', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Layout switch'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openFeeds(page);
  await assignCamera(page, 0, camera.name);
  await startCell(page, 0);
  await waitForPlaying(page, 0);
  await expectConsumerCount(feeds.sub.path, 1);

  await selectLayout(page, '3 × 3');

  await expectConsumerCount(feeds.sub.path, 0);
  await expect(page.locator('.feed-cell')).toHaveCount(9);
  await expect(feedCell(page, 0).locator('button.feed-center')).toBeVisible();
});

test('grids collapse to one column on mobile', async ({ page, request }) => {
  await createCameraFixture(
    request,
    uniqueName('Mobile'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );
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
