import { test, expect } from '@playwright/test';

// ─── 19.4: Unauthenticated access redirects to login ────────────────────────
test.describe('Protected Routes - Unauthenticated Access', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Unauthenticated access to /dashboard redirects to login', async ({ page }) => {
    await page.goto('/dashboard');
    await page.waitForURL('**/login', { timeout: 5000 });
    await expect(page).toHaveURL(/\/login/);
  });

  test('Unauthenticated access to /productos redirects to login', async ({ page }) => {
    await page.goto('/productos');
    await page.waitForURL('**/login', { timeout: 5000 });
    await expect(page).toHaveURL(/\/login/);
  });

  test('Unauthenticated access to /pos redirects to login', async ({ page }) => {
    await page.goto('/pos');
    await page.waitForURL('**/login', { timeout: 5000 });
    await expect(page).toHaveURL(/\/login/);
  });

  test('Unauthenticated access to /configuracion redirects to login', async ({ page }) => {
    await page.goto('/configuracion');
    await page.waitForURL('**/login', { timeout: 5000 });
    await expect(page).toHaveURL(/\/login/);
  });
});
