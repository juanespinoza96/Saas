import { test } from '@playwright/test';
import { LoginPage } from '../page-objects/LoginPage';
import { ReportesPage } from '../page-objects/ReportesPage';
import { TEST_USERS } from '../fixtures/test-users';

// ─── 28.2: Plan Básico is blocked from Reportes ─────────────────────────────
test.describe('Reportes - Plan Básico', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Plan Básico is blocked from Reportes section', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.gerenteBasico.email, TEST_USERS.gerenteBasico.password);
    await loginPage.expectRedirectToDashboard();

    const reportes = new ReportesPage(page);
    await reportes.goto();
    await reportes.expectLoaded();
    await reportes.expectBlocked();
  });
});

// ─── 28.3: Plan Intermedio sees predefined reports without custom filters ────
test.describe('Reportes - Plan Intermedio', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Plan Intermedio sees 3 predefined reports without custom filters', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.gerenteIntermedio.email, TEST_USERS.gerenteIntermedio.password);
    await loginPage.expectRedirectToDashboard();

    const reportes = new ReportesPage(page);
    await reportes.goto();
    await reportes.expectLoaded();
    await reportes.expectPredefinedReportsVisible();
    await reportes.expectCustomFiltersNotVisible();
  });
});

// ─── 28.4: Plan Empresarial sees full reports with custom filters ────────────
test.describe('Reportes - Plan Empresarial', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Plan Empresarial sees reports with date, branch, category, product filters', async ({ page }) => {
    const reportes = new ReportesPage(page);
    await reportes.goto();
    await reportes.expectLoaded();
    await reportes.expectPredefinedReportsVisible();
    await reportes.expectCustomFiltersVisible();
  });
});
