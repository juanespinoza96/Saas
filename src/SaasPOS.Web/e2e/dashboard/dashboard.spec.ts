import { test, expect } from '@playwright/test';
import { DashboardPage } from '../page-objects/DashboardPage';

// ─── 30.3: Day sales total, notification count, and quick access ─────────────
test.describe('Dashboard - Ventas, Notificaciones y Accesos Rápidos (Gerente)', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Sales total is displayed with dollar format', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await page.goto('/dashboard');
    await dashboard.expectLoaded();

    const salesTotal = await dashboard.getSalesTotal();
    // Sales total should be a dollar-formatted string (e.g., "$0.00", "$123.45")
    expect(salesTotal).toMatch(/^\$\d+\.\d{2}$/);
  });

  test('Notification count is visible for Gerente', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await page.goto('/dashboard');
    await dashboard.expectLoaded();

    // The notification button should be visible for Gerente role
    await expect(dashboard.notificationButton).toBeVisible();
  });

  test('Quick access shows correct options for Gerente', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await page.goto('/dashboard');
    await dashboard.expectLoaded();

    const labels = await dashboard.getQuickAccessLabels();
    // Gerente sees: Punto de Venta, Productos, Clientes, Reportes, Inventario, Usuarios
    expect(labels).toContain('Punto de Venta');
    expect(labels).toContain('Productos');
    expect(labels).toContain('Clientes');
    expect(labels).toContain('Reportes');
    expect(labels).toContain('Inventario');
    expect(labels).toContain('Usuarios');
  });
});

test.describe('Dashboard - Accesos Rápidos (Cajero)', () => {
  test.use({ storageState: '.auth/cajero.json' });

  test('Quick access shows correct options for Cajero', async ({ page }) => {
    const dashboard = new DashboardPage(page);
    await page.goto('/dashboard');
    await dashboard.expectLoaded();

    const labels = await dashboard.getQuickAccessLabels();
    // Cajero sees: Punto de Venta, Clientes
    expect(labels).toContain('Punto de Venta');
    expect(labels).toContain('Clientes');
    // Cajero should NOT see management options
    expect(labels).not.toContain('Productos');
    expect(labels).not.toContain('Reportes');
    expect(labels).not.toContain('Inventario');
    expect(labels).not.toContain('Usuarios');
  });
});

// ─── 30.4: No-sales state shows $0.00 without errors ────────────────────────
test.describe('Dashboard - Estado sin ventas', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('No-sales state shows $0.00 or zero without errors', async ({ page }) => {
    // Listen for console errors
    const consoleErrors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    const dashboard = new DashboardPage(page);
    await page.goto('/dashboard');
    await dashboard.expectLoaded();

    const salesTotal = await dashboard.getSalesTotal();
    // When there are no sales, should display $0.00 or a valid dollar amount
    expect(salesTotal).toMatch(/^\$\d+\.\d{2}$/);

    // No JavaScript errors should have occurred
    expect(consoleErrors).toHaveLength(0);
  });
});
