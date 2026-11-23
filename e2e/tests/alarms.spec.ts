import { expect, test, type Route } from '@playwright/test';
import { createCameraFixture } from './support/api';
import { feeds, uniqueName } from './support/env';
import { expectConsumerCount, openAlarms, openPlayback } from './support/ui';

async function delayRoute(route: Route): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 700));
  await route.continue();
}

test('shows progress while searching alarms', async ({ page, request }) => {
  await createCameraFixture(
    request,
    uniqueName('Alarm loading'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await page.route(/\/api\/alarms\?/, delayRoute);

  await openAlarms(page);

  const status = page.locator('.search-status');
  await expect(status).toBeVisible();
  await expect(status).toContainText('Searching alarms…');

  await expect(page.locator('.alarm-summary')).toBeVisible();
  await expect(status).toHaveCount(0);
});

test('shows NVR motion alarms on the Alarms page and playback timeline', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Alarm camera'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openAlarms(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search alarms' }).click();

  await expect(page.locator('.alarm-bar')).toHaveCount(2);
  await expect(page.locator('.alarm-bar-human')).toHaveCount(1);
  await expect(page.locator('.alarm-bar-vehicle')).toHaveCount(1);
  await expect(page.locator('.alarm-day')).toHaveCount(1);
  await expect(page.locator('.alarm-summary')).toContainText('2 motion ranges');

  const table = page.locator('.alarm-table').first();
  await expect(table.locator('tbody tr')).toHaveCount(2);
  await expect(table).toContainText(camera.name);
  await expect(table.locator('tbody tr').first()).toContainText('Person');
  await expect(table.getByRole('button', { name: 'Play', exact: true })).toHaveCount(2);

  await page.locator('.alarm-bar').first().hover();
  const tooltip = page.locator('.floating-tooltip');
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toContainText(camera.name);
  await expect(tooltip).toContainText('Open in playback');
  await expectConsumerCount(feeds.playback.path, 0);

  await page.getByRole('button', { name: 'Yesterday' }).click();
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-bar')).toHaveCount(2);

  await openPlayback(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search recordings' }).click();

  await expect(page.locator('.playback-timeline-segment')).toHaveCount(2);
  const motion = page.locator('.playback-timeline-motion-range');
  await expect(motion).toHaveCount(2);
  await expect(motion.first()).toHaveClass(/motion-human/);
  await expect(motion.nth(1)).toHaveClass(/motion-vehicle/);
  await expect(page.locator('.playback-timeline')).toContainText('Motion');
  await expectConsumerCount(feeds.playback.path, 0);
});

test('collapses a dense alarm burst into one bounded timeline bar', async ({
  page,
  request,
}) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Alarm density'),
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
      : fallbackEnd - 60 * 60_000;
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

  await openAlarms(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search alarms' }).click();

  await expect(page.locator('.alarm-bar')).toHaveCount(1);
  await expect(page.locator('.alarm-track').first()).toHaveCSS('height', '26px');
  await expect(page.locator('.alarm-summary')).toContainText('115 motion ranges');

  await page.locator('.alarm-bar').first().hover();
  const tooltip = page.locator('.floating-tooltip');
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toContainText('115 alarms');
});

test('validates filters and clears stale alarm results', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Alarm filters'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openAlarms(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-bar')).toHaveCount(2);

  await page.getByLabel('Target').selectOption('unknown');
  await expect(page.locator('.alarm-bar')).toHaveCount(0);
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.getByText('No motion alarms found')).toBeVisible();

  await page.getByLabel('Target').selectOption('');
  await page.getByRole('button', { name: 'Last 7 days' }).click();
  const lastSevenFrom = await page
    .getByLabel('From', { exact: true })
    .inputValue();
  const [lastSevenDate, lastSevenTime] = lastSevenFrom.split(' ');
  const [lastSevenDay, lastSevenMonth, lastSevenYear] = lastSevenDate!
    .split('-')
    .map(Number);
  const [lastSevenHour, lastSevenMinute, lastSevenSecond] = lastSevenTime!
    .split(':')
    .map(Number);
  const lastSevenStart = new Date(
    lastSevenYear!,
    lastSevenMonth! - 1,
    lastSevenDay!,
    lastSevenHour!,
    lastSevenMinute!,
    lastSevenSecond!,
  );
  const lastSevenAge = Date.now() - lastSevenStart.getTime();
  expect(lastSevenAge).toBeGreaterThan(6 * 24 * 60 * 60 * 1000);
  expect(lastSevenAge).toBeLessThan(8 * 24 * 60 * 60 * 1000);
  await page.getByRole('button', { name: 'Today' }).click();
  await expect(page.getByLabel('From', { exact: true })).toHaveValue(
    /^\d{2}-\d{2}-\d{4} 00:00:00$/,
  );
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-bar')).toHaveCount(2);

  await page.getByLabel('From', { exact: true }).fill('21-11-2026 12:00:00');
  await page.getByLabel('To', { exact: true }).fill('21-11-2026 11:00:00');
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(
    page.getByRole('alert').filter({ hasText: 'end must be after its start' }),
  ).toBeVisible();

  await page.getByLabel('From', { exact: true }).fill('01-10-2026 00:00:00');
  await page.getByLabel('To', { exact: true }).fill('22-11-2026 00:00:00');
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(
    page.getByRole('alert').filter({ hasText: 'at most 31 days' }),
  ).toBeVisible();
});

test('keeps alarm results when the quick range changes', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Alarm range retention'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openAlarms(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-bar')).toHaveCount(2);

  await page.getByRole('button', { name: 'Yesterday' }).click();

  await expect(page.locator('.alarm-bar')).toHaveCount(2);
  await expect(page.getByText('Search again to apply it')).toBeVisible();
  await expect(page.getByLabel('From', { exact: true })).toHaveValue(
    /^\d{2}-\d{2}-\d{4} 00:00:00$/,
  );

  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-bar')).toHaveCount(2);
  await expect(page.getByText('Search again to apply it')).toHaveCount(0);
});

test('opens an alarm from its tooltip in playback', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Alarm playback'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openAlarms(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-bar')).toHaveCount(2);

  await page.locator('.alarm-bar').first().hover();
  const link = page.getByRole('link', { name: 'Open in playback' });
  await expect(link).toBeVisible();
  await expectConsumerCount(feeds.playback.path, 0);
  await link.click();

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

test('opens an alarm from the table with the keyboard', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Alarm keyboard'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openAlarms(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-table tbody tr')).toHaveCount(2);

  const play = page
    .locator('.alarm-table')
    .getByRole('button', { name: 'Play', exact: true })
    .first();
  await play.focus();
  await page.keyboard.press('Enter');

  await expect(page.getByRole('heading', { name: 'Playback', exact: true })).toBeVisible();
  await expect(page.getByLabel('Camera')).toHaveValue(camera.id);
});
