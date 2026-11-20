import { expect, type Page } from '@playwright/test';
import { consumerProcessCount } from './processes';

export function feedCell(page: Page, index: number) {
  return page.locator('.feed-cell').nth(index);
}

export async function openFeeds(page: Page): Promise<void> {
  await page.goto('/#/feeds');
  await expect(page.getByRole('heading', { name: 'Feeds' })).toBeVisible();
}

export async function openPlayback(page: Page): Promise<void> {
  await page.goto('/#/playback');
  await expect(page.getByRole('heading', { name: 'Playback', exact: true })).toBeVisible();
}

export async function openNvrs(page: Page): Promise<void> {
  await page.goto('/#/config');
  await expect(page.getByRole('heading', { name: 'NVRs', exact: true })).toBeVisible();
}

export async function selectStream(
  page: Page,
  cellIndex: number,
  stream: 'main' | 'sub',
): Promise<void> {
  await feedCell(page, cellIndex)
    .locator('.stream-toggle')
    .getByRole('button', { name: stream === 'main' ? 'Main' : 'Sub', exact: true })
    .click();
}

export function acceptDialogs(
  page: Page,
  onMessage?: (message: string) => void,
): void {
  page.on('dialog', (dialog) => {
    onMessage?.(dialog.message());
    void dialog.accept();
  });
}

export async function selectLayout(page: Page, label: '1 × 1' | '2 × 2' | '3 × 3'): Promise<void> {
  await page.getByRole('button', { name: label, exact: true }).click();
}

export async function assignCamera(
  page: Page,
  cellIndex: number,
  cameraName: string,
): Promise<void> {
  const cell = feedCell(page, cellIndex);
  await cell.locator('button.feed-center').click();
  await page.locator('.picker-option').filter({ hasText: cameraName }).click();
  await expect(cell.locator('.feed-name')).toHaveText(cameraName);
}

export async function startCell(page: Page, cellIndex: number): Promise<void> {
  await feedCell(page, cellIndex).getByRole('button', { name: 'Start', exact: true }).click();
}

export async function stopCell(page: Page, cellIndex: number): Promise<void> {
  await feedCell(page, cellIndex).getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(
    feedCell(page, cellIndex).getByRole('button', { name: 'Start', exact: true }),
  ).toBeVisible();
}

export async function waitForPlaying(page: Page, cellIndex: number): Promise<void> {
  const video = feedCell(page, cellIndex).locator('video');

  await expect
    .poll(async () => video.evaluate((element: HTMLVideoElement) => element.currentTime), {
      timeout: 30_000,
    })
    .toBeGreaterThan(0.5);

  const state = await video.evaluate((element: HTMLVideoElement) => ({
    readyState: element.readyState,
    width: element.videoWidth,
    height: element.videoHeight,
  }));

  expect(state.readyState).toBeGreaterThanOrEqual(2);
  expect(state.width).toBeGreaterThan(0);
  expect(state.height).toBeGreaterThan(0);
}

export async function expectPixelsChanging(page: Page, cellIndex: number): Promise<void> {
  const changedPixels = await feedCell(page, cellIndex)
    .locator('video')
    .evaluate(async (video: HTMLVideoElement) => {
      const canvas = document.createElement('canvas');
      canvas.width = 160;
      canvas.height = 90;
      const context = canvas.getContext('2d', { willReadFrequently: true });

      if (!context) {
        return 0;
      }

      context.drawImage(video, 0, 0, 160, 90);
      const before = context.getImageData(0, 0, 160, 90).data.slice();

      await new Promise((resolve) => setTimeout(resolve, 800));

      context.drawImage(video, 0, 0, 160, 90);
      const after = context.getImageData(0, 0, 160, 90).data;
      let changed = 0;

      for (let index = 0; index < before.length; index += 4) {
        if (
          Math.abs(before[index] - after[index]) > 8 ||
          Math.abs(before[index + 1] - after[index + 1]) > 8 ||
          Math.abs(before[index + 2] - after[index + 2]) > 8
        ) {
          changed += 1;
        }
      }

      return changed;
    });

  expect(changedPixels).toBeGreaterThan(50);
}

export async function expectConsumerCount(feedPath: string, expected: number): Promise<void> {
  await expect.poll(() => consumerProcessCount(feedPath), { timeout: 15_000 }).toBe(expected);
}
