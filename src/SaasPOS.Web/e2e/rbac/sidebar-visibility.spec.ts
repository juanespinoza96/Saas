import { test } from '@playwright/test';
import { SidebarComponent } from '../page-objects/SidebarComponent';

// 20.1 — Cajero sees only Dashboard, POS, POS Bar, Clientes, Notificaciones
test.describe('Sidebar - Cajero', () => {
  test.use({ storageState: '.auth/cajero.json' });

  test('Cajero sees Dashboard, POS, POS Bar, Clientes, Notificaciones', async ({ page }) => {
    await page.goto('/dashboard');
    const sidebar = new SidebarComponent(page);
    await sidebar.expectOptions([
      'Dashboard',
      'Punto de Venta',
      'Punto de Venta (Bar)',
      'Clientes',
      'Notificaciones',
    ]);
  });

  test('Cajero does NOT see Productos, Reportes, Usuarios, Configuración', async ({ page }) => {
    await page.goto('/dashboard');
    const sidebar = new SidebarComponent(page);
    await sidebar.expectNotContainsOption('Productos');
    await sidebar.expectNotContainsOption('Reportes');
    await sidebar.expectNotContainsOption('Usuarios');
    await sidebar.expectNotContainsOption('Configuración');
  });
});

// 20.2 — Gerente and Dueño see all sidebar options
test.describe('Sidebar - Gerente', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Gerente sees all sidebar options', async ({ page }) => {
    await page.goto('/dashboard');
    const sidebar = new SidebarComponent(page);
    await sidebar.expectOptions([
      'Dashboard',
      'Punto de Venta',
      'Punto de Venta (Bar)',
      'Productos',
      'Categorías y Atributos',
      'Inventario / Stock',
      'Clientes',
      'Reportes',
      'Notificaciones',
      'Usuarios',
      'Sucursales',
      'Configuración',
    ]);
  });
});

test.describe('Sidebar - Dueño', () => {
  test.use({ storageState: '.auth/dueno.json' });

  test('Dueño sees all sidebar options', async ({ page }) => {
    await page.goto('/dashboard');
    const sidebar = new SidebarComponent(page);
    await sidebar.expectOptions([
      'Dashboard',
      'Punto de Venta',
      'Punto de Venta (Bar)',
      'Productos',
      'Categorías y Atributos',
      'Inventario / Stock',
      'Clientes',
      'Reportes',
      'Notificaciones',
      'Usuarios',
      'Sucursales',
      'Configuración',
    ]);
  });
});

// 20.3 — Supervisor sees only Dashboard, Reportes, Notificaciones
test.describe('Sidebar - Supervisor', () => {
  test.use({ storageState: '.auth/supervisor.json' });

  test('Supervisor sees only Dashboard, Reportes, Notificaciones', async ({ page }) => {
    await page.goto('/dashboard');
    const sidebar = new SidebarComponent(page);
    await sidebar.expectOptions([
      'Dashboard',
      'Reportes',
      'Notificaciones',
    ]);
  });
});

// 20.4 — Bodeguero sees only Dashboard, Inventario / Stock, Notificaciones
test.describe('Sidebar - Bodeguero', () => {
  test.use({ storageState: '.auth/bodeguero.json' });

  test('Bodeguero sees only Dashboard, Inventario / Stock, Notificaciones', async ({ page }) => {
    await page.goto('/dashboard');
    const sidebar = new SidebarComponent(page);
    await sidebar.expectOptions([
      'Dashboard',
      'Inventario / Stock',
      'Notificaciones',
    ]);
  });
});
