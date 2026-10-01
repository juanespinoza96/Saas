import { test, expect } from '@playwright/test';

// 21.1 — Cajero navigating to /productos and /reportes is blocked
// 21.2 — Cajero navigating to /usuarios and /configuracion is blocked
test.describe('Route Blocking - Cajero', () => {
  test.use({ storageState: '.auth/cajero.json' });

  test('Cajero navigating to /productos is blocked', async ({ page }) => {
    await page.goto('/productos');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/productos/);
  });

  test('Cajero navigating to /reportes is blocked', async ({ page }) => {
    await page.goto('/reportes');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/reportes/);
  });

  test('Cajero navigating to /usuarios is blocked', async ({ page }) => {
    await page.goto('/usuarios');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/usuarios/);
  });

  test('Cajero navigating to /configuracion is blocked', async ({ page }) => {
    await page.goto('/configuracion');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/configuracion/);
  });

  test('Cajero navigating to /inventario is blocked', async ({ page }) => {
    await page.goto('/inventario');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/inventario/);
  });

  test('Cajero navigating to /sucursales is blocked', async ({ page }) => {
    await page.goto('/sucursales');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/sucursales/);
  });
});

// 21.3 — Supervisor navigating to /pos and /productos is blocked
test.describe('Route Blocking - Supervisor', () => {
  test.use({ storageState: '.auth/supervisor.json' });

  test('Supervisor navigating to /pos is blocked', async ({ page }) => {
    await page.goto('/pos');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/pos$/);
  });

  test('Supervisor navigating to /productos is blocked', async ({ page }) => {
    await page.goto('/productos');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/productos/);
  });

  test('Supervisor navigating to /inventario is blocked', async ({ page }) => {
    await page.goto('/inventario');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/inventario/);
  });

  test('Supervisor navigating to /clientes is blocked', async ({ page }) => {
    await page.goto('/clientes');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/clientes/);
  });
});

// 21.4 — Bodeguero navigating to /pos and /reportes is blocked
test.describe('Route Blocking - Bodeguero', () => {
  test.use({ storageState: '.auth/bodeguero.json' });

  test('Bodeguero navigating to /pos is blocked', async ({ page }) => {
    await page.goto('/pos');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/pos$/);
  });

  test('Bodeguero navigating to /reportes is blocked', async ({ page }) => {
    await page.goto('/reportes');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/reportes/);
  });

  test('Bodeguero navigating to /productos is blocked', async ({ page }) => {
    await page.goto('/productos');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/productos/);
  });

  test('Bodeguero navigating to /usuarios is blocked', async ({ page }) => {
    await page.goto('/usuarios');
    await page.waitForTimeout(1000);
    await expect(page).not.toHaveURL(/\/usuarios/);
  });
});
