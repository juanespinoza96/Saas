import { test, expect } from '@playwright/test';

// ─── 19.3: Expired token redirects to login ─────────────────────────────────
test.describe('Session Expiry', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Accessing protected route with expired/invalid token redirects to login', async ({ page }) => {
    // First go to login to initialize the page context and gain access to localStorage
    await page.goto('/login');

    // Simulate an expired session by setting an invalid token
    await page.evaluate(() => {
      localStorage.setItem('token', 'expired.invalid.token');
    });

    // Attempt to navigate to a protected route
    await page.goto('/dashboard');

    // Should redirect to login since the token is invalid/expired
    await page.waitForURL('**/login', { timeout: 5000 });
    await expect(page).toHaveURL(/\/login/);
  });

  test('Cleared session redirects to login on protected route access', async ({ page }) => {
    // Navigate directly to a protected route with no auth state
    await page.goto('/dashboard');

    // Should redirect to login
    await page.waitForURL('**/login', { timeout: 5000 });
    await expect(page).toHaveURL(/\/login/);
  });
});
