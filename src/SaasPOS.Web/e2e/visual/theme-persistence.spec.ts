import { test, expect } from '@playwright/test';

const ADMIN_BASE_URL = 'http://localhost:3001';
const POS_THEME_KEY = 'saas-pos-theme';
const ADMIN_THEME_KEY = 'saas-admin-theme';

// ─── 32: Tests E2E — Tema Visual ────────────────────────────────────────────

// ─── 32.1 & 32.3: POS Frontend — Toggle Dark Mode applies dark styles ───────
test.describe('Theme Persistence - POS Frontend', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('toggle Dark Mode applies dark styles to document', async ({ page }) => {
    await page.goto('/');
    const html = page.locator('html');

    // Initially light mode (default)
    await expect(html, 'HTML should not have dark class in light mode').not.toHaveClass(/dark/);

    // Find the theme toggle in the sidebar
    const themeToggle = page.getByRole('button', { name: /Cambiar a modo oscuro|Modo Oscuro/i });
    await expect(themeToggle, 'Theme toggle button should be visible in POS sidebar').toBeVisible();

    // Click to activate dark mode
    await themeToggle.click();

    // Verify dark class is applied to html element
    await expect(html, 'HTML should have dark class after toggling to dark mode').toHaveClass(/dark/);

    // Verify localStorage was updated
    const storedTheme = await page.evaluate((key) => localStorage.getItem(key), POS_THEME_KEY);
    expect(storedTheme, 'localStorage should store "dark" after toggling').toBe('dark');
  });

  // ─── 32.2: Reloading page preserves theme via localStorage ─────────────────
  test('reloading page preserves dark theme via localStorage', async ({ page }) => {
    await page.goto('/');

    // Set dark theme via the toggle
    const themeToggle = page.getByRole('button', { name: /Cambiar a modo oscuro|Modo Oscuro/i });
    await themeToggle.click();

    const html = page.locator('html');
    await expect(html, 'Dark class should be applied before reload').toHaveClass(/dark/);

    // Reload the page
    await page.reload();

    // Verify dark theme persists after reload
    await expect(html, 'Dark class should persist after page reload').toHaveClass(/dark/);

    // Verify localStorage still has the dark value
    const storedTheme = await page.evaluate((key) => localStorage.getItem(key), POS_THEME_KEY);
    expect(storedTheme, 'localStorage should still contain "dark" after reload').toBe('dark');
  });

  test('toggling back to light mode removes dark class and updates localStorage', async ({ page }) => {
    // Start with dark mode set in localStorage
    await page.goto('/');
    await page.evaluate((key) => localStorage.setItem(key, 'dark'), POS_THEME_KEY);
    await page.reload();

    const html = page.locator('html');
    await expect(html, 'Dark mode should be active from localStorage').toHaveClass(/dark/);

    // Toggle back to light
    const themeToggle = page.getByRole('button', { name: /Cambiar a modo claro|Modo Claro/i });
    await themeToggle.click();

    // Verify dark class removed
    await expect(html, 'Dark class should be removed after toggling to light mode').not.toHaveClass(/dark/);

    // Verify localStorage updated
    const storedTheme = await page.evaluate((key) => localStorage.getItem(key), POS_THEME_KEY);
    expect(storedTheme, 'localStorage should store "light" after toggling back').toBe('light');
  });
});

// ─── 32.4: Admin Frontend — Theme persistence ───────────────────────────────
test.describe('Theme Persistence - Admin Frontend', () => {
  test.use({ storageState: '.auth/superadmin.json' });

  test('toggle Dark Mode applies dark styles in Admin panel', async ({ page }) => {
    await page.goto(`${ADMIN_BASE_URL}/`);
    const html = page.locator('html');

    // Initially light mode (default)
    await expect(html, 'Admin HTML should not have dark class in light mode').not.toHaveClass(/dark/);

    // Find the theme toggle in the Admin header
    const themeToggle = page.getByRole('button', { name: /Activar modo oscuro/i });
    await expect(themeToggle, 'Theme toggle button should be visible in Admin header').toBeVisible();

    // Click to activate dark mode
    await themeToggle.click();

    // Verify dark class is applied
    await expect(html, 'Admin HTML should have dark class after toggling').toHaveClass(/dark/);

    // Verify localStorage was updated with admin key
    const storedTheme = await page.evaluate((key) => localStorage.getItem(key), ADMIN_THEME_KEY);
    expect(storedTheme, 'Admin localStorage should store "dark"').toBe('dark');
  });

  test('reloading Admin page preserves dark theme via localStorage', async ({ page }) => {
    await page.goto(`${ADMIN_BASE_URL}/`);

    // Set dark theme via the toggle
    const themeToggle = page.getByRole('button', { name: /Activar modo oscuro/i });
    await themeToggle.click();

    const html = page.locator('html');
    await expect(html, 'Dark class should be applied in Admin before reload').toHaveClass(/dark/);

    // Reload the page
    await page.reload();

    // Verify dark theme persists after reload
    await expect(html, 'Dark class should persist in Admin after reload').toHaveClass(/dark/);

    // Verify localStorage still has the dark value
    const storedTheme = await page.evaluate((key) => localStorage.getItem(key), ADMIN_THEME_KEY);
    expect(storedTheme, 'Admin localStorage should still contain "dark" after reload').toBe('dark');
  });

  test('toggling back to light mode removes dark class in Admin panel', async ({ page }) => {
    // Start with dark mode set in localStorage
    await page.goto(`${ADMIN_BASE_URL}/`);
    await page.evaluate((key) => localStorage.setItem(key, 'dark'), ADMIN_THEME_KEY);
    await page.reload();

    const html = page.locator('html');
    await expect(html, 'Admin dark mode should be active from localStorage').toHaveClass(/dark/);

    // Toggle back to light
    const themeToggle = page.getByRole('button', { name: /Activar modo claro/i });
    await themeToggle.click();

    // Verify dark class removed
    await expect(html, 'Admin dark class should be removed after toggling to light').not.toHaveClass(/dark/);

    // Verify localStorage updated
    const storedTheme = await page.evaluate((key) => localStorage.getItem(key), ADMIN_THEME_KEY);
    expect(storedTheme, 'Admin localStorage should store "light" after toggling back').toBe('light');
  });
});
