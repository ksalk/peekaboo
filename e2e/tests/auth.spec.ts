import { expect, test } from '@playwright/test';
import { createUser, deleteUser } from './support/api';
import { adminPassword, adminUsername, uniqueName } from './support/env';

test('rejects invalid credentials and signs in with valid ones', async ({ page }) => {
  await page.context().clearCookies();
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'Peekaboo' })).toBeVisible();
  await page.getByLabel('Username', { exact: true }).fill(adminUsername);
  await page.getByLabel('Password', { exact: true }).fill('definitely-wrong');
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByRole('alert')).toContainText(/invalid username or password/i);

  await page.getByLabel('Password', { exact: true }).fill(adminPassword);
  await page.getByRole('button', { name: 'Sign in' }).click();

  await expect(page.getByRole('heading', { name: 'Feeds' })).toBeVisible();
});

test('viewer accounts cannot open user management', async ({ page, request }) => {
  const username = uniqueName('viewer').toLowerCase();
  const password = 'viewer-password-123';
  const viewer = await createUser(request, username, password);

  try {
    await page.context().clearCookies();
    await page.goto('/');
    await page.getByLabel('Username', { exact: true }).fill(username);
    await page.getByLabel('Password', { exact: true }).fill(password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page.getByRole('heading', { name: 'Feeds' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Users', exact: true })).toHaveCount(0);

    await page.goto('/#/users');
    await expect(page.getByRole('heading', { name: 'Feeds' })).toBeVisible();
    await expect(page).toHaveURL(/#\/feeds/);
  } finally {
    await deleteUser(request, viewer.id);
  }
});
