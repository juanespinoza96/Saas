import { test, expect } from '@playwright/test';
import { POSNormalPage } from '../page-objects/POSNormalPage';
import { LoginPage } from '../page-objects/LoginPage';
import { TEST_USERS } from '../fixtures/test-users';

// --- 24.1: MostrarBotonCliente TRUE shows client search field ---
test.describe('Client Association - Visible', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('MostrarBotonCliente TRUE shows client search field', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // When MostrarBotonCliente is TRUE, the client search field should be visible
    await expect(page.getByLabel('Buscar cliente por identificación')).toBeVisible();
    await expect(page.getByText('Cliente (opcional)')).toBeVisible();
  });

  // --- 24.3: Search finds existing client by identification ---
  test('Search finds existing client by identification', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Type identification in the client search
    await page.getByLabel('Buscar cliente por identificación').fill('1712345678');

    // Wait for results and verify "Juan Pérez" appears
    await expect(page.getByText('Juan Pérez')).toBeVisible();
  });

  // --- 24.4: Payment summary shows method, installments, total before confirming ---
  test('Payment summary shows method, installments, total before confirming', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add product to the cart
    await pos.addProduct('Camiseta Polo');

    // Click confirm to trigger payment summary
    await pos.confirmButton.click();

    // Verify summary shows total amount
    await expect(page.getByText('$15.00')).toBeVisible();

    // Verify payment method information is shown
    await expect(
      page.getByText(/método/i).or(page.getByText(/pago/i))
    ).toBeVisible();
  });
});

// --- 24.2: MostrarBotonCliente FALSE hides client field completely ---
test.describe('Client Association - Hidden', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('MostrarBotonCliente FALSE hides client field', async ({ page }) => {
    // Login as gerente basico (Básico plan, MostrarBotonCliente=FALSE)
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.gerenteBasico.email, TEST_USERS.gerenteBasico.password);
    await loginPage.expectRedirectToDashboard();

    // Navigate to POS
    await page.goto('/pos');

    // Client field should NOT be visible
    await expect(page.getByLabel('Buscar cliente por identificación')).not.toBeVisible();
    await expect(page.getByText('Cliente (opcional)')).not.toBeVisible();
  });
});
