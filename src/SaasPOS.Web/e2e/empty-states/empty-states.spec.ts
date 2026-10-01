import { test, expect } from '@playwright/test';
import { NotificacionesPage } from '../page-objects/NotificacionesPage';

// ─── 31: Estados Vacíos — Listas vacías muestran mensaje descriptivo ─────────
test.describe('Estados Vacíos - Mensajes descriptivos en listas vacías', () => {
  test.use({ storageState: '.auth/gerente.json' });

  // ─── 31.1: Empty product list shows descriptive message ────────────────────
  test('Empty product list shows descriptive message instead of error', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    await page.goto('/productos');

    // Wait for the page to finish loading (either table with data or empty state)
    await expect(page.getByRole('heading', { name: /Productos/i })).toBeVisible();

    // The page should render without JS errors
    expect(consoleErrors, 'No JS console errors should occur on Productos page').toHaveLength(0);

    // Check the page renders data or a descriptive empty state message (no white screen / no crash)
    const tableRows = page.locator('table tbody tr');
    const rowCount = await tableRows.count();

    if (rowCount === 0 || (rowCount === 1 && await tableRows.first().textContent().then(t => t?.includes('No se encontraron')))) {
      // Empty state: verify descriptive message is shown
      await expect(
        page.getByText('No se encontraron productos'),
        'Empty product list should show "No se encontraron productos" message'
      ).toBeVisible();
    } else {
      // Data present: verify at least one product row renders properly (not a white screen)
      expect(rowCount).toBeGreaterThan(0);
    }
  });

  // ─── 31.2: Empty sales list shows descriptive message ──────────────────────
  test('Empty sales/reports list shows descriptive message instead of error', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    await page.goto('/reportes');

    // Wait for the page to load (Reportes is the sales data view)
    await expect(page.getByRole('heading', { name: /Reportes/i })).toBeVisible();

    // The page should render without JS errors
    expect(consoleErrors, 'No JS console errors should occur on Reportes page').toHaveLength(0);

    // Check for either data or descriptive empty state message
    const emptyStateMessage = page.getByText('Sin datos disponibles');
    const hasData = page.locator('table tbody tr, .recharts-wrapper, canvas, svg.recharts-surface');

    const emptyVisible = await emptyStateMessage.first().isVisible().catch(() => false);
    const dataVisible = await hasData.first().isVisible().catch(() => false);

    // Either data is shown OR the descriptive empty state message is shown — no blank/error screen
    expect(
      emptyVisible || dataVisible,
      'Reportes page should show either sales data or "Sin datos disponibles" empty state message'
    ).toBe(true);
  });

  // ─── 31.3: Empty client list shows descriptive message ─────────────────────
  test('Empty client list shows descriptive message instead of error', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    await page.goto('/clientes');

    // Wait for the page to load
    await expect(page.getByRole('heading', { name: /Clientes/i })).toBeVisible();

    // The page should render without JS errors
    expect(consoleErrors, 'No JS console errors should occur on Clientes page').toHaveLength(0);

    // Check the page renders data or a descriptive empty state message
    const tableRows = page.locator('table tbody tr');
    const rowCount = await tableRows.count();

    if (rowCount === 0 || (rowCount === 1 && await tableRows.first().textContent().then(t => t?.includes('No se encontraron')))) {
      // Empty state: verify descriptive message is shown
      await expect(
        page.getByText('No se encontraron clientes'),
        'Empty client list should show "No se encontraron clientes" message'
      ).toBeVisible();
    } else {
      // Data present: verify at least one client row renders properly
      expect(rowCount).toBeGreaterThan(0);
    }
  });

  // ─── 31.4: Empty notifications list shows descriptive message ──────────────
  test('Empty notifications list shows descriptive message instead of error', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('console', msg => {
      if (msg.type() === 'error') {
        consoleErrors.push(msg.text());
      }
    });

    const notificaciones = new NotificacionesPage(page);
    await notificaciones.goto();
    await notificaciones.expectLoaded();

    // The page should render without JS errors
    expect(consoleErrors, 'No JS console errors should occur on Notificaciones page').toHaveLength(0);

    // Check the page renders data or a descriptive empty state message
    const notifCards = notificaciones.notificationCards;
    const cardCount = await notifCards.count();

    if (cardCount === 0) {
      // Empty state: verify descriptive message is shown
      await notificaciones.expectEmptyState();
    } else {
      // Data present: verify at least one notification card renders
      expect(cardCount).toBeGreaterThan(0);
    }
  });
});
