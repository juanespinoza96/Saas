import { test, expect, Page } from '@playwright/test';

// ─── 35: Tests E2E — Checklist Pantallas POS ─────────────────────────────────

const POS_THEME_KEY = 'saas-pos-theme';

/**
 * Helper: captures JS console errors during navigation and verifies the page renders.
 * Returns the list of console error messages collected.
 */
async function navigateAndVerify(page: Page, route: string): Promise<string[]> {
  const consoleErrors: string[] = [];

  const handler = (msg: import('@playwright/test').ConsoleMessage) => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text());
    }
  };

  page.on('console', handler);

  await page.goto(route, { waitUntil: 'domcontentloaded' });

  // Wait for the page to have meaningful content (not white screen)
  await page.waitForLoadState('networkidle');

  // Verify no white screen: body must have visible text content
  const bodyText = await page.locator('body').textContent();
  expect(
    bodyText && bodyText.trim().length > 0,
    `Page ${route} should render visible content (not white screen)`
  ).toBeTruthy();

  // Small delay to capture any late console errors
  await page.waitForTimeout(500);

  page.off('console', handler);

  return consoleErrors;
}

// ─── 35.1: Navigate Login, Dashboard, POS Normal, POS Bar — verify no JS errors ─
test.describe('POS Screens Checklist — Core Screens', () => {
  test.describe('Public pages (no auth)', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('Login page renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/login');
      expect(errors, 'Login should have no JS console errors').toHaveLength(0);
    });
  });

  test.describe('Authenticated pages', () => {
    test.use({ storageState: '.auth/gerente.json' });

    test('Dashboard renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/dashboard');
      expect(errors, 'Dashboard should have no JS console errors').toHaveLength(0);
    });

    test('POS Normal renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/pos');
      expect(errors, 'POS Normal should have no JS console errors').toHaveLength(0);
    });

    test('POS Bar Escolar renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/pos-bar');
      expect(errors, 'POS Bar should have no JS console errors').toHaveLength(0);
    });
  });
});

// ─── 35.2: Navigate Productos, Categorías, Inventario, Clientes — no JS errors or white screen ─
test.describe('POS Screens Checklist — Management Screens', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Productos renders without JS errors or white screen', async ({ page }) => {
    const errors = await navigateAndVerify(page, '/productos');
    expect(errors, 'Productos should have no JS console errors').toHaveLength(0);
  });

  test('Categorías renders without JS errors or white screen', async ({ page }) => {
    const errors = await navigateAndVerify(page, '/categorias');
    expect(errors, 'Categorías should have no JS console errors').toHaveLength(0);
  });

  test('Inventario renders without JS errors or white screen', async ({ page }) => {
    const errors = await navigateAndVerify(page, '/inventario');
    expect(errors, 'Inventario should have no JS console errors').toHaveLength(0);
  });

  test('Clientes renders without JS errors or white screen', async ({ page }) => {
    const errors = await navigateAndVerify(page, '/clientes');
    expect(errors, 'Clientes should have no JS console errors').toHaveLength(0);
  });
});

// ─── 35.3: Navigate Reportes, Notificaciones, Usuarios, Sucursales, Configuración, Recuperación — verify renders ─
test.describe('POS Screens Checklist — Additional Screens', () => {
  test.describe('Authenticated screens', () => {
    test.use({ storageState: '.auth/gerente.json' });

    test('Reportes renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/reportes');
      expect(errors, 'Reportes should have no JS console errors').toHaveLength(0);
    });

    test('Notificaciones renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/notificaciones');
      expect(errors, 'Notificaciones should have no JS console errors').toHaveLength(0);
    });

    test('Usuarios renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/usuarios');
      expect(errors, 'Usuarios should have no JS console errors').toHaveLength(0);
    });

    test('Sucursales renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/sucursales');
      expect(errors, 'Sucursales should have no JS console errors').toHaveLength(0);
    });

    test('Configuración renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/configuracion');
      expect(errors, 'Configuración should have no JS console errors').toHaveLength(0);
    });
  });

  test.describe('Public pages (no auth)', () => {
    test.use({ storageState: { cookies: [], origins: [] } });

    test('Recuperación renders without JS errors', async ({ page }) => {
      const errors = await navigateAndVerify(page, '/recuperacion');
      expect(errors, 'Recuperación should have no JS console errors').toHaveLength(0);
    });
  });
});

// ─── 35.4: Repeat navigation with dark theme active — confirm text visible against dark background ─
test.describe('POS Screens Checklist — Dark Theme', () => {
  const authenticatedRoutes = [
    '/dashboard',
    '/pos',
    '/pos-bar',
    '/productos',
    '/categorias',
    '/inventario',
    '/clientes',
    '/reportes',
    '/notificaciones',
    '/usuarios',
    '/sucursales',
    '/configuracion',
  ];

  const publicRoutes = ['/login', '/recuperacion'];

  test.describe('Authenticated screens in dark mode', () => {
    test.use({ storageState: '.auth/gerente.json' });

    for (const route of authenticatedRoutes) {
      test(`${route} renders correctly in dark theme`, async ({ page }) => {
        // Set dark theme before navigating
        await page.goto('/dashboard', { waitUntil: 'domcontentloaded' });
        await page.evaluate((key) => localStorage.setItem(key, 'dark'), POS_THEME_KEY);
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

        await page.goto(route, { waitUntil: 'domcontentloaded' });
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
        await page.goto(route, { waitUntil: 'domcontentloaded' });
        await page.evaluate((key) => localStorage.setItem(key, 'dark'), POS_THEME_KEY);
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
