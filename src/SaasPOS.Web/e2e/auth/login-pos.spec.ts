import { test } from '@playwright/test';
import { LoginPage } from '../page-objects/LoginPage';
import { TEST_USERS } from '../fixtures/test-users';

// ─── 18.1 & 18.2: Successful login for all POS roles ───────────────────────
test.describe('POS Login - Success', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Gerente login redirects to Dashboard', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.gerenteEmpresarial.email, TEST_USERS.gerenteEmpresarial.password);
    await loginPage.expectRedirectToDashboard();
  });

  test('Cajero login redirects to Dashboard', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.cajeroEmpresarial.email, TEST_USERS.cajeroEmpresarial.password);
    await loginPage.expectRedirectToDashboard();
  });

  test('Dueño login redirects to Dashboard', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.dueno.email, TEST_USERS.dueno.password);
    await loginPage.expectRedirectToDashboard();
  });

  test('Supervisor login redirects to Dashboard', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.supervisor.email, TEST_USERS.supervisor.password);
    await loginPage.expectRedirectToDashboard();
  });

  test('Bodeguero login redirects to Dashboard', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.bodeguero.email, TEST_USERS.bodeguero.password);
    await loginPage.expectRedirectToDashboard();
  });
});

// ─── 18.3: Invalid credentials show generic error ───────────────────────────
test.describe('POS Login - Invalid Credentials', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Wrong email shows generic error', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login('nonexistent@test.com', 'Test123!');
    await loginPage.expectErrorMessage('Credenciales inválidas');
  });

  test('Wrong password shows generic error', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.gerenteEmpresarial.email, 'WrongPassword!');
    await loginPage.expectErrorMessage('Credenciales inválidas');
  });

  test('Inactive account shows generic error', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login('inactive@test.com', 'Test123!');
    await loginPage.expectErrorMessage('Credenciales inválidas');
  });
});

// ─── 18.4: Suspended comercio and inline validation ─────────────────────────
test.describe('POS Login - Validation', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('Suspended comercio shows access error', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login('suspended@test.com', 'Test123!');
    await loginPage.expectErrorMessage('Credenciales inválidas');
  });

  test('Empty email triggers inline validation', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.passwordInput.fill('Test123!');
    await loginPage.submitButton.click();
    await loginPage.expectValidationError('El correo es obligatorio');
  });

  test('Empty password triggers inline validation', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.emailInput.fill('test@test.com');
    await loginPage.submitButton.click();
    await loginPage.expectValidationError('La contraseña es obligatoria');
  });

  test('Empty fields trigger both inline validations', async ({ page }) => {
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.submitButton.click();
    await loginPage.expectValidationError('El correo es obligatorio');
    await loginPage.expectValidationError('La contraseña es obligatoria');
  });
});
