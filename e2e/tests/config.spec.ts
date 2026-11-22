import { expect, test } from '@playwright/test';
import { feeds, nvrBaseUrl, rtspPort, uniqueName } from './support/env';
import { acceptDialogs, feedCell, openFeeds, openNvrs } from './support/ui';

test('manages NVRs and their cameras', async ({ page }) => {
  let dialogMessage = '';
  acceptDialogs(page, (message) => {
    dialogMessage = message;
  });

  const nvrName = uniqueName('NVR');
  const renamedNvr = `${nvrName} renamed`;
  const cameraName = uniqueName('Front door');
  const renamedCamera = `${cameraName} edited`;
  const submittedBaseUrl = `RTSP://127.0.0.1:${rtspPort}/`;

  await openNvrs(page);

  await page.getByRole('button', { name: 'Add NVR', exact: true }).click();
  await page.getByLabel('Name', { exact: true }).fill(nvrName);
  await page.getByLabel('NVR address', { exact: true }).fill(submittedBaseUrl);
  await page.getByLabel('Time zone', { exact: true }).fill('Europe/Warsaw');
  await page.getByRole('button', { name: 'Add NVR', exact: true }).click();

  const nvrCard = page.locator('.nvr-card').filter({ hasText: nvrName });
  await expect(nvrCard.getByRole('heading', { name: nvrName, exact: true })).toBeVisible();
  await expect(nvrCard.locator('.url')).toHaveText(nvrBaseUrl);
  await expect(nvrCard.locator('.url')).not.toContainText('camuser');
  await expect(nvrCard.locator('.url')).not.toContainText('campassword');
  await expect(nvrCard).toContainText('Europe/Warsaw');

  await nvrCard.getByRole('button', { name: 'Check clock' }).click();
  await expect(nvrCard).toContainText('NVR clock:');
  await expect(nvrCard).toContainText('(override)');

  await page.getByRole('button', { name: `Add camera to ${nvrName}` }).click();
  await page.getByLabel('Name', { exact: true }).fill(cameraName);
  await page.getByLabel('Main stream ID', { exact: true }).fill(feeds.main.streamId);
  await page.getByLabel('Sub stream ID', { exact: true }).fill(feeds.sub.streamId);
  await page.getByRole('button', { name: 'Add camera', exact: true }).click();

  const cameraRow = page.locator('.camera-row').filter({ hasText: cameraName });
  await expect(cameraRow.getByRole('heading', { name: cameraName, exact: true })).toBeVisible();
  await expect(cameraRow.locator('.stream-ids')).toHaveText(
    `Main ${feeds.main.streamId} · Sub ${feeds.sub.streamId}`,
  );

  await page.getByRole('button', { name: `Edit ${cameraName} on ${nvrName}` }).click();
  await page.getByLabel('Name', { exact: true }).fill(renamedCamera);
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect(
    page.getByRole('heading', { name: renamedCamera, exact: true }),
  ).toBeVisible();

  await page.getByRole('button', { name: `Edit ${nvrName}`, exact: true }).click();
  await page.getByLabel('Name', { exact: true }).fill(renamedNvr);
  await page.getByRole('button', { name: 'Save changes', exact: true }).click();

  await expect(page.getByRole('heading', { name: renamedNvr, exact: true })).toBeVisible();

  // Reload the page so the camera query must return the renamed NVR.
  await page.reload();
  await expect(
    page.getByRole('button', { name: `Edit ${renamedCamera} on ${renamedNvr}` }),
  ).toBeVisible();

  await openFeeds(page);
  await feedCell(page, 0).locator('button.feed-center').click();
  const pickerOption = page.locator('.picker-option').filter({ hasText: renamedCamera });
  await expect(pickerOption).toContainText(renamedNvr);
  await page.getByRole('button', { name: 'Cancel', exact: true }).click();

  await openNvrs(page);
  await page.getByRole('button', { name: `Delete ${renamedNvr}`, exact: true }).click();

  expect(dialogMessage).toContain('1 camera will be removed too');
  await expect(page.getByRole('heading', { name: renamedNvr, exact: true })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: renamedCamera, exact: true })).toHaveCount(0);
});

test('rejects HTTP and credential-bearing NVR addresses', async ({ page }) => {
  const nvrName = uniqueName('Bad NVR');

  await openNvrs(page);

  await page.getByRole('button', { name: 'Add NVR', exact: true }).click();
  await page.getByLabel('Name', { exact: true }).fill(nvrName);
  await page.getByLabel('NVR address', { exact: true }).fill('http://example.test');
  await page.getByRole('button', { name: 'Add NVR', exact: true }).click();

  await expect(page.getByRole('alert')).toContainText('rtsp');
  await expect(page.getByRole('heading', { name: nvrName, exact: true })).toHaveCount(0);

  await page
    .getByLabel('NVR address', { exact: true })
    .fill(`rtsp://user:password@127.0.0.1:${rtspPort}`);
  await page.getByRole('button', { name: 'Add NVR', exact: true }).click();

  await expect(page.getByRole('alert')).toContainText('credentials');

  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
});
