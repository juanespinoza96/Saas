import { test } from '@playwright/test';
import { AdminLoginPage } from '../page-objects/admin/AdminLoginPage';
import { TEST_USERS } from '../fixtures/test-users';

// ─── 19.1: SuperAdmin login redirects to Admin Dashboard ────────────────────
test.describe('Admin Login - SuperAdmin', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('SuperAdmin login redirects to Admin Dashboard', async ({ page }) => {
    const adminLogin = new AdminLoginPage(page);
    await adminLogin.goto();
    await adminLogin.login(TEST_USERS.superadmin.email, TEST_USERS.superadmin.password);
    await adminLogin.expectRedirectToAdminDashboard();
  });
});

// ─── 19.2: POS roles attempting Admin login are blocked ─────────────────────
test.describe('Admin Login - POS Roles Blocked', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Gerente cannot login to Admin panel', async ({ page }) => {
    const adminLogin = new AdminLoginPage(page);
    await adminLogin.goto();
    await adminLogin.login(TEST_USERS.gerenteEmpresarial.email, TEST_USERS.gerenteEmpresarial.password);
    await page.waitForTimeout(2000);
    await adminLogin.expectErrorMessage();
  });

  test('Cajero cannot login to Admin panel', async ({ page }) => {
    const adminLogin = new AdminLoginPage(page);
    await adminLogin.goto();
    await adminLogin.login(TEST_USERS.cajeroEmpresarial.email, TEST_USERS.cajeroEmpresarial.password);
    await page.waitForTimeout(2000);
    await adminLogin.expectErrorMessage();
  });

  test('Dueño cannot login to Admin panel', async ({ page }) => {
    const adminLogin = new AdminLoginPage(page);
    await adminLogin.goto();
    await adminLogin.login(TEST_USERS.dueno.email, TEST_USERS.dueno.password);
    await page.waitForTimeout(2000);
    await adminLogin.expectErrorMessage();
  });

  test('Supervisor cannot login to Admin panel', async ({ page }) => {
    const adminLogin = new AdminLoginPage(page);
    await adminLogin.goto();
    await adminLogin.login(TEST_USERS.supervisor.email, TEST_USERS.supervisor.password);
    await page.waitForTimeout(2000);
    await adminLogin.expectErrorMessage();
  });
});
