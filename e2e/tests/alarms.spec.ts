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

test('opens an alarm tooltip link from the keyboard', async ({ page, request }) => {
  const camera = await createCameraFixture(
    request,
    uniqueName('Alarm keyboard'),
    feeds.main.streamId,
    feeds.sub.streamId,
  );

  await openAlarms(page);
  await page.getByLabel('Camera').selectOption(camera.id);
  await page.getByRole('button', { name: 'Search alarms' }).click();
  await expect(page.locator('.alarm-bar')).toHaveCount(2);

  const secondBar = page.locator('.alarm-bar').nth(1);
  await secondBar.focus();
  await expect(page.locator('.floating-tooltip')).toBeVisible();

  await page.keyboard.press('Tab');
  const link = page.getByRole('link', { name: 'Open in playback' });
  await expect(link).toBeFocused();
  await expect(page.locator('.floating-tooltip')).toBeVisible();

  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Playback', exact: true })).toBeVisible();
  await expect(page.getByLabel('Camera')).toHaveValue(camera.id);
});
