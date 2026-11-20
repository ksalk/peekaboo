import { expect, test } from '@playwright/test';
import { createCameraFixture } from './support/api';
import { feeds, uniqueName } from './support/env';
import {
  expectConsumerCount,
  openPlayback,
} from './support/ui';

test('searches, copies, and plays a recording', async ({ page, context, request }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write']);

  const camera = await createCameraFixture(
    request,
    uniqueName('Playback camera'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);

  await page.getByLabel('From', { exact: true }).fill('12:00:00');
  await page.getByLabel('To', { exact: true }).fill('11:00:00');
  await page.getByRole('button', { name: 'Search recordings' }).click();
  await expect(
    page.getByRole('alert').filter({ hasText: 'End time must be after start time' }),
  ).toBeVisible();

  await page.getByLabel('From', { exact: true }).fill('00:00:00');
  await page.getByLabel('To', { exact: true }).fill('23:59:59');
  await page.getByRole('button', { name: 'Search recordings' }).click();

  const rows = page.locator('.recording-row');
  await expect(rows).toHaveCount(2);

  const row = rows.first();
  await expect(row).toBeVisible();

  const playbackUrl = await row.locator('.url').innerText();
  expect(playbackUrl).toMatch(
    /^rtsp:\/\/127\.0\.0\.1:8555\/ISAPI\/Streaming\/tracks\/101\?starttime=\d{8}T\d{6}Z&endtime=\d{8}T\d{6}Z$/,
  );
  expect(playbackUrl).not.toContain('campassword');

  await row.getByRole('button', { name: 'Copy URL' }).click();
  await expect(row.getByRole('button', { name: 'Copied' })).toBeVisible();

  const clipboard = await page.evaluate(() => navigator.clipboard.readText());
  expect(clipboard).toBe(playbackUrl);
  expect(clipboard).not.toContain('campassword');

  await expectConsumerCount(feeds.playback.path, 0);
  await row.getByRole('button', { name: 'Play', exact: true }).click();

  await expect
    .poll(
      () =>
        page
          .locator('.playback-video')
          .evaluate((video: HTMLVideoElement) => video.currentTime),
      { timeout: 30_000 },
    )
    .toBeGreaterThan(0.5);

  await expectConsumerCount(feeds.playback.path, 1);

  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await expectConsumerCount(feeds.playback.path, 0);
  await expect(page.locator('.playback-video')).toHaveCount(0);
});

test('stops playback on navigation and remembers the selected camera', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Playback navigation'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search recordings' }).click();

  const row = page.locator('.recording-row').first();
  await expect(row).toBeVisible();
  await row.getByRole('button', { name: 'Play', exact: true }).click();
  await expectConsumerCount(feeds.playback.path, 1);

  await page.getByRole('link', { name: 'Feeds', exact: true }).click();
  await expectConsumerCount(feeds.playback.path, 0);

  await openPlayback(page);
  await expect(page.getByLabel('Camera')).toHaveValue(camera.id);
});
