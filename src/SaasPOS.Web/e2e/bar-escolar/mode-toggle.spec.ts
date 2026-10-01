import { test } from '@playwright/test';
import { LoginPage } from '../page-objects/LoginPage';
import { POSBarPage } from '../page-objects/POSBarPage';
import { TEST_USERS } from '../fixtures/test-users';

test.describe('Bar Escolar - Mode Toggle', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('EsBarEscolar FALSE shows unavailable message', async ({ page }) => {
    // Login as gerenteBasico (Básico plan, EsBarEscolar=FALSE)
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.gerenteBasico.email, TEST_USERS.gerenteBasico.password);
    await loginPage.expectRedirectToDashboard();

    const bar = new POSBarPage(page);
    await bar.goto();
    await bar.expectUnavailable();
  });
});
