import { test, expect, Page } from '@playwright/test';

// ─── 36: Tests E2E — Checklist Pantallas Admin ───────────────────────────────

const ADMIN_BASE_URL = 'http://localhost:3001';
const ADMIN_THEME_KEY = 'saas-admin-theme';

/**
 * Helper: captures JS console errors during navigation and verifies the page renders.
 * Returns the list of console error messages collected.
 */
async function navigateAndVerify(page: Page, path: string): Promise<string[]> {
  const consoleErrors: string[] = [];

  const handler = (msg: import('@playwright/test').ConsoleMessage) => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text());
    }
  };

  page.on('console', handler);

  await page.goto(`${ADMIN_BASE_URL}${path}`, { waitUntil: 'domcontentloaded' });

  // Wait for the page to have meaningful content (not white screen)
  await page.waitForLoadState('networkidle');

  // Verify no white screen: body must have visible text content
  const bodyText = await page.locator('body').textContent();
  expect(
    bodyText && bodyText.trim().length > 0,
    `Page ${path} should render visible content (not white screen)`
  ).toBeTruthy();

  // Small delay to capture any late console errors
  await page.waitForTimeout(500);

  page.off('console', handler);

  return consoleErrors;
}

// ─── 36.1: Navigate Login, Dashboard, Comercios — verify no JS errors ────────
test.describe('Admin Screens Checklist — Core Screens', () => {
  test.describe('Public pages (no auth)', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('Admin Login page renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/login');
      expect(errors, 'Admin Login should have no JS console errors').toHaveLength(0);
    });
  });

  test.describe('Authenticated pages', () => {
    test.use({ storageState: '.auth/superadmin.json' });

    test('Admin Dashboard renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/dashboard');
      expect(errors, 'Admin Dashboard should have no JS console errors').toHaveLength(0);
    });

    test('Comercios renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/comercios');
      expect(errors, 'Comercios should have no JS console errors').toHaveLength(0);
    });
  });
});

// ─── 36.2: Navigate Detalle Comercio, Planes, Auditoría — no JS errors or white screen ─
test.describe('Admin Screens Checklist — Management Screens', () => {
  test.use({ storageState: '.auth/superadmin.json' });

  test('Detalle Comercio renders without JS errors or white screen', async ({ page }) => {
    const errors = await navigateAndVerify(page, '/comercios/1');
    expect(errors, 'Detalle Comercio should have no JS console errors').toHaveLength(0);
  });

  test('Planes renders without JS errors or white screen', async ({ page }) => {
    const errors = await navigateAndVerify(page, '/planes');
    expect(errors, 'Planes should have no JS console errors').toHaveLength(0);
  });

  test('Auditoría renders without JS errors or white screen', async ({ page }) => {
    const errors = await navigateAndVerify(page, '/auditoria');
    expect(errors, 'Auditoría should have no JS console errors').toHaveLength(0);
  });
});

// ─── 36.3: Repeat navigation with dark theme active — confirm text visible ───
test.describe('Admin Screens Checklist — Dark Theme', () => {
  const authenticatedRoutes = [
    '/dashboard',
    '/comercios',
    '/comercios/1',
    '/planes',
    '/auditoria',
  ];

  const publicRoutes = ['/login'];

  test.describe('Authenticated screens in dark mode', () => {
    test.use({ storageState: '.auth/superadmin.json' });

    for (const route of authenticatedRoutes) {
      test(`${route} renders correctly in dark theme`, async ({ page }) => {
        // Set dark theme before navigating
        await page.goto(`${ADMIN_BASE_URL}/dashboard`, { waitUntil: 'domcontentloaded' });
        await page.evaluate((key) => localStorage.setItem(key, 'dark'), ADMIN_THEME_KEY);
        await page.reload({ waitUntil: 'domcontentloaded' });

        // Verify dark class is applied
        const html = page.locator('html');
        await expect(html, 'HTML should have dark class').toHaveClass(/dark/);

        // Navigate to the target route
        const consoleErrors: string[] = [];
        page.on('console', (msg) => {
          if (msg.type() === 'error') {
            consoleErrors.push(msg.text());
          }
        });

        await page.goto(`${ADMIN_BASE_URL}${route}`, { waitUntil: 'domcontentloaded' });
        await page.waitForLoadState('networkidle');

        // Verify dark class persists after navigation
        await expect(html, `Dark class should persist on ${route}`).toHaveClass(/dark/);

        // Verify page renders (not white screen)
        const bodyText = await page.locator('body').textContent();
        expect(
          bodyText && bodyText.trim().length > 0,
          `Page ${route} should render content in dark mode`
        ).toBeTruthy();

        // Verify text is visible against dark background — check that visible text elements exist
        const visibleTextElements = page.locator(
          'h1, h2, h3, p, span, label, a, button, td, th, li'
        );
        const count = await visibleTextElements.count();
        expect(count, `Page ${route} should have visible text elements in dark mode`).toBeGreaterThan(0);

        // Verify at least one text element is actually visible (not hidden)
        const firstVisible = visibleTextElements.first();
        await expect(
          firstVisible,
          `First text element on ${route} should be visible in dark mode`
        ).toBeVisible();

        // No JS errors
        expect(consoleErrors, `${route} in dark mode should have no JS errors`).toHaveLength(0);
      });
    }
  });

  test.describe('Public screens in dark mode', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    for (const route of publicRoutes) {
      test(`${route} renders correctly in dark theme`, async ({ page }) => {
        // Set dark theme via localStorage before navigation
        await page.goto(`${ADMIN_BASE_URL}${route}`, { waitUntil: 'domcontentloaded' });
        await page.evaluate((key) => localStorage.setItem(key, 'dark'), ADMIN_THEME_KEY);
        await page.reload({ waitUntil: 'domcontentloaded' });

        const html = page.locator('html');
        await expect(html, `HTML should have dark class on ${route}`).toHaveClass(/dark/);

        // Verify page renders content
        const bodyText = await page.locator('body').textContent();
        expect(
          bodyText && bodyText.trim().length > 0,
          `Page ${route} should render content in dark mode`
        ).toBeTruthy();

        // Verify text is visible against dark background
        const visibleTextElements = page.locator(
          'h1, h2, h3, p, span, label, a, button, td, th, li'
        );
        const count = await visibleTextElements.count();
        expect(count, `Page ${route} should have visible text elements in dark mode`).toBeGreaterThan(0);

        const firstVisible = visibleTextElements.first();
        await expect(
          firstVisible,
          `First text element on ${route} should be visible in dark mode`
        ).toBeVisible();
      });
    }
  });
});

// ─── 36.4: Verify no console errors across all Admin pages ───────────────────
test.describe('Admin Screens Checklist — No Console Errors (Combined)', () => {
  test.describe('All authenticated Admin pages sequentially', () => {
    test.use({ storageState: '.auth/superadmin.json' });

    test('No console errors across all authenticated Admin pages', async ({ page }) => {
      const allErrors: { route: string; errors: string[] }[] = [];

      const routes = ['/dashboard', '/comercios', '/comercios/1', '/planes', '/auditoria'];

      for (const route of routes) {
        const errors = await navigateAndVerify(page, route);
        if (errors.length > 0) {
          allErrors.push({ route, errors });
        }
      }

      expect(
        allErrors,
        `Expected no console errors across all Admin pages, but found errors: ${JSON.stringify(allErrors)}`
      ).toHaveLength(0);
    });
  });

  test.describe('Public Admin pages', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('No console errors on Admin Login page', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/login');
      expect(errors, 'Admin Login should have no console errors').toHaveLength(0);
    });
  });
});
