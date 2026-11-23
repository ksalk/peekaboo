import { expect, test } from '@playwright/test';
import { createCameraFixture } from './support/api';
import { feeds, uniqueName } from './support/env';
import {
  expectConsumerCount,
  localDateTime,
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

  const today = new Date();
  const todayText = localDateTime(today);
  const dayPrefix = todayText.slice(0, 10);

  await page.getByLabel('From', { exact: true }).fill(`${dayPrefix} 12:00:00`);
  await page.getByLabel('To', { exact: true }).fill(`${dayPrefix} 11:00:00`);
  await page.getByRole('button', { name: 'Search recordings' }).click();
  await expect(
    page.getByRole('alert').filter({ hasText: 'End time must be after start time' }),
  ).toBeVisible();

  await page.getByLabel('From', { exact: true }).fill(`${dayPrefix} 00:00:00`);
  await page.getByLabel('To', { exact: true }).fill(`${dayPrefix} 23:59:59`);
  await page.getByRole('button', { name: 'Search recordings' }).click();

  await expect(page.locator('.recording-row')).toHaveCount(0);

  const timeline = page.locator('.playback-timeline');
  await expect(timeline).toBeVisible();
  const timelineSegments = timeline.locator('.playback-timeline-segment');
  await expect(timelineSegments).toHaveCount(2);
  await expect(timelineSegments.first()).toHaveAttribute(
    'aria-label',
    /Play recording from/,
  );

  await timelineSegments.first().hover();
  const tooltip = page.locator('.floating-tooltip');
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toContainText('H.264-BP');
  await expect(tooltip).toContainText('Click to play');
  await expect(tooltip).toContainText(/\d{2}-\d{2}-\d{4}/);

  await expectConsumerCount(feeds.playback.path, 0);
  await timelineSegments.first().click();

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
  await expect(page.locator('.playback-video')).toHaveJSProperty('controls', true);
  await expect(page.locator('.playback-jump')).toBeVisible();

  await page.getByRole('button', { name: 'Copy stream URL' }).click();
  await expect(page.getByRole('button', { name: 'Copied' })).toBeVisible();

  const clipboard = await page.evaluate(() => navigator.clipboard.readText());
  expect(clipboard).toMatch(
    /^rtsp:\/\/127\.0\.0\.1:8555\/ISAPI\/Streaming\/tracks\/101\?starttime=\d{8}T\d{6}Z&endtime=\d{8}T\d{6}Z$/,
  );
  expect(clipboard).not.toContain('campassword');

  const playerHeader = page.locator('.playback-player-header p');
  const beforeJumpHeader = await playerHeader.innerText();
  const beforeJumpTime = await page
    .locator('.playback-video')
    .evaluate((video: HTMLVideoElement) => video.currentTime);

  await page.locator('.playback-jump input[type=range]').evaluate((element) => {
    const input = element as HTMLInputElement;
    input.value = '5';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await page.getByRole('button', { name: 'Play from here' }).click();

  await expect(playerHeader).not.toHaveText(beforeJumpHeader);
  await expect
    .poll(
      () =>
        page
          .locator('.playback-video')
          .evaluate((video: HTMLVideoElement) => video.currentTime),
      { timeout: 10_000 },
    )
    .toBeLessThan(beforeJumpTime);

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

test('plays from the recording coverage timeline', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Timeline camera'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search recordings' }).click();

  const timeline = page.locator('.playback-timeline');
  await expect(timeline).toBeVisible();
  await expect(timeline.locator('.playback-timeline-segment')).toHaveCount(2);

  const segment = timeline.locator('.playback-timeline-segment').nth(1);
  await expect(segment).toBeVisible();

  const label = await segment.getAttribute('aria-label');
  const startTime = /Play recording from (.+?) to /.exec(label ?? '')?.[1];
  expect(startTime).toBeTruthy();

  await expectConsumerCount(feeds.playback.path, 0);
  await segment.click();

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
  await expect(segment).toHaveClass(/\bactive\b/);
  await expect(segment).toHaveAttribute('aria-pressed', 'true');
  await expect(page.locator('.playback-player-header p')).toContainText(startTime!);

  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await expectConsumerCount(feeds.playback.path, 0);
  await expect(segment).not.toHaveClass(/\bactive\b/);
  await expect(segment).toHaveAttribute('aria-pressed', 'false');

  await segment.focus();
  await expect(segment).toBeFocused();
  await page.keyboard.press('Enter');

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
  await expect(page.locator('.playback-player-header p')).toContainText(startTime!);

  await page.getByRole('button', { name: 'Close', exact: true }).click();
  await expectConsumerCount(feeds.playback.path, 0);
});

test('zooms and drags the recording timeline without starting playback', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Timeline zoom'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search recordings' }).click();
  await expect(page.locator('.playback-timeline-segment')).toHaveCount(2);

  const scroll = page.locator('.playback-timeline-scroll');
  const body = page.locator('.playback-timeline-body');
  const widthBefore = await body.evaluate((element) => element.getBoundingClientRect().width);

  await page.getByRole('button', { name: 'Zoom in timeline' }).click();
  await expect(body).toHaveAttribute('style', /width:\s*200%/);
  const widthAfter = await body.evaluate((element) => element.getBoundingClientRect().width);
  expect(widthAfter).toBeGreaterThan(widthBefore);

  const beforeScroll = await scroll.evaluate((element) => element.scrollLeft);
  expect(beforeScroll).toBeGreaterThanOrEqual(0);
  await scroll.evaluate((element) => {
    element.scrollLeft = 0;
  });

  const segment = page.locator('.playback-timeline-segment').first();
  const box = await segment.boundingBox();
  expect(box).not.toBeNull();

  await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2);
  await page.mouse.down();
  await page.mouse.move(box!.x + box!.width / 2 - 120, box!.y + box!.height / 2, {
    steps: 8,
  });
  await page.mouse.up();

  await expect
    .poll(() => scroll.evaluate((element) => element.scrollLeft))
    .toBeGreaterThan(0);
  await expect(page.locator('.playback-video')).toHaveCount(0);
});

test('plays a recording from the motion tooltip', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Motion tooltip'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  const day = localDateTime(new Date()).slice(0, 10);
  await page.getByLabel('From', { exact: true }).fill(`${day} 00:00:00`);
  await page.getByLabel('To', { exact: true }).fill(`${day} 02:00:00`);
  await page.getByRole('button', { name: 'Search recordings' }).click();

  const motion = page.locator('.playback-timeline-motion-range').first();
  await expect(motion).toBeVisible();
  await motion.hover();

  const tooltip = page.locator('.floating-tooltip');
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toContainText('Person');

  await expectConsumerCount(feeds.playback.path, 0);
  await tooltip.getByRole('button', { name: 'Play', exact: true }).click();

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

test('splits a dense motion burst when zooming the timeline', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Motion density'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await page.route(/\/api\/alarms\?/, async (route) => {
    const url = new URL(route.request().url());
    const requestedStart = Date.parse(url.searchParams.get('start') ?? '');
    const requestedEnd = Date.parse(url.searchParams.get('end') ?? '');
    const fallbackEnd = Date.now();
    const rangeStart = Number.isFinite(requestedStart)
      ? requestedStart
      : fallbackEnd - 2 * 60 * 60_000;
    const rangeEnd =
      Number.isFinite(requestedEnd) && requestedEnd > rangeStart
        ? requestedEnd
        : fallbackEnd;
    const span = rangeEnd - rangeStart;
    const step = Math.max(1, (span * 0.4) / 115);
    const alarmDuration = Math.max(1, Math.min(5_000, step));
    const burstStart = rangeStart + span * 0.3;

    const ranges = Array.from({ length: 115 }, (_, index) => {
      const start = new Date(burstStart + index * step);
      const end = new Date(start.getTime() + alarmDuration);

      return {
        cameraId: camera.id,
        cameraName: camera.name,
        nvrId: camera.nvrId,
        nvrName: camera.nvrName,
        channelId: 1,
        start: start.toISOString(),
        end: end.toISOString(),
        durationSeconds: Math.max(1, Math.round(alarmDuration / 1000)),
        target: 'human',
        targetLabel: 'Osoba',
        inferredStart: false,
        inferredEnd: false,
      };
    });

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        start: new Date(rangeStart).toISOString(),
        end: new Date(rangeEnd).toISOString(),
        truncated: false,
        ranges,
        errors: [],
      }),
    });
  });

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  const day = localDateTime(new Date()).slice(0, 10);
  await page.getByLabel('From', { exact: true }).fill(`${day} 00:00:00`);
  await page.getByLabel('To', { exact: true }).fill(`${day} 02:00:00`);
  await page.getByRole('button', { name: 'Search recordings' }).click();

  const motion = page.locator('.playback-timeline-motion-range');
  await expect(motion).toHaveCount(1);
  await expect(page.locator('.playback-timeline-motion-track')).toHaveCSS(
    'height',
    '26px',
  );

  await page.getByRole('button', { name: 'Zoom in timeline' }).click();
  await expect(motion).toHaveCount(115);
});

test('keeps results when the quick range changes', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Playback range retention'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search recordings' }).click();
  await expect(page.locator('.playback-timeline-segment')).toHaveCount(2);

  await page.getByRole('button', { name: 'Yesterday' }).click();

  await expect(page.locator('.playback-timeline-segment')).toHaveCount(2);
  await expect(page.getByText('Search again to apply it')).toBeVisible();
  await expect(page.getByLabel('From', { exact: true })).toHaveValue(
    /^\d{2}-\d{2}-\d{4} 00:00:00$/,
  );

  await page.getByRole('button', { name: 'Search recordings' }).click();
  await expect(page.locator('.playback-timeline-segment')).toHaveCount(2);
  await expect(page.getByText('Search again to apply it')).toHaveCount(0);
});

test('stops playback when the tab is hidden and resumes on return', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Playback visibility'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search recordings' }).click();
  await page.locator('.playback-timeline-segment').first().click();

  await expect
    .poll(
      () =>
        page
          .locator('.playback-video')
          .evaluate((video: HTMLVideoElement) => video.currentTime),
      { timeout: 30_000 },
    )
    .toBeGreaterThan(1);

  const beforeHeader = await page.locator('.playback-player-header p').innerText();
  await expectConsumerCount(feeds.playback.path, 1);

  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: () => true,
    });
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: () => 'hidden',
    });
    document.dispatchEvent(new Event('visibilitychange'));
  });

  await expect(page.locator('.playback-video')).toHaveCount(0);
  await expectConsumerCount(feeds.playback.path, 0);

  await page.evaluate(() => {
    Object.defineProperty(document, 'hidden', {
      configurable: true,
      get: () => false,
    });
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: () => 'visible',
    });
    document.dispatchEvent(new Event('visibilitychange'));
  });

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
  await expect(page.locator('.playback-player-header p')).not.toHaveText(beforeHeader);
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

  const segment = page.locator('.playback-timeline-segment').first();
  await expect(segment).toBeVisible();
  await segment.click();
  await expectConsumerCount(feeds.playback.path, 1);

  await page.getByRole('link', { name: 'Feeds', exact: true }).click();
  await expectConsumerCount(feeds.playback.path, 0);

  await openPlayback(page);
  await expect(page.getByLabel('Camera')).toHaveValue(camera.id);
});
