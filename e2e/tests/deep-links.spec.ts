import { expect, test } from '@playwright/test';
import { createCameraFixture } from './support/api';
import { feeds, uniqueName } from './support/env';
import { expectConsumerCount } from './support/ui';

test('opens playback from a deep link and auto-plays the linked time', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Deep link camera'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  const end = new Date();
  const start = new Date(end.getTime() - 60 * 60 * 1000);
  const at = new Date(start.getTime() + 12 * 60 * 1000);
  const query = new URLSearchParams({
    cameraId: camera.id,
    start: start.toISOString(),
    end: end.toISOString(),
    at: at.toISOString(),
    autoplay: '1',
  });

  await page.goto(`/#/playback?${query.toString()}`);
  await expect(page.getByRole('heading', { name: 'Playback', exact: true })).toBeVisible();
  await expect(page.getByLabel('Camera')).toHaveValue(camera.id);
  await expect(page.locator('.playback-video')).toBeVisible();

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
});

test('replaces the view when a second playback deep link is opened', async ({
  page,
  request,
}) => {
  const firstCamera = await createCameraFixture(
    request,
    uniqueName('Deep link first'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );
  const secondCamera = await createCameraFixture(
    request,
    uniqueName('Deep link second'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  const end = new Date();
  const start = new Date(end.getTime() - 60 * 60 * 1000);
  const at = new Date(start.getTime() + 12 * 60 * 1000);
  const linkFor = (cameraId: string) => {
    const query = new URLSearchParams({
      cameraId,
      start: start.toISOString(),
      end: end.toISOString(),
      at: at.toISOString(),
      autoplay: '1',
    });
    return `#/playback?${query.toString()}`;
  };

  await page.goto(`/${linkFor(firstCamera.id)}`);
  await expect(page.getByLabel('Camera')).toHaveValue(firstCamera.id);
  await expect
    .poll(
      () =>
        page
          .locator('.playback-video')
          .evaluate((video: HTMLVideoElement) => video.currentTime),
      { timeout: 30_000 },
    )
    .toBeGreaterThan(0.5);

  await page.evaluate((hash) => {
    window.location.hash = hash;
  }, linkFor(secondCamera.id));

  await expect(page.getByLabel('Camera')).toHaveValue(secondCamera.id);
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
});
