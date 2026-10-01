import { test, expect } from '@playwright/test';
import { POSNormalPage } from '../page-objects/POSNormalPage';
import { LoginPage } from '../page-objects/LoginPage';
import { TEST_USERS } from '../fixtures/test-users';

test.describe('Facturación SRI - Disabled', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('UsaFacturacionSRI FALSE hides Factura Electronica option', async ({ page }) => {
    // Login as gerenteBasico (Básico plan, UsaFacturacionSRI=FALSE)
    const loginPage = new LoginPage(page);
    await loginPage.goto();
    await loginPage.login(TEST_USERS.gerenteBasico.email, TEST_USERS.gerenteBasico.password);
    await loginPage.expectRedirectToDashboard();

    await page.goto('/pos');

    // Verify the Factura Electronica option is NOT in the comprobante selector
    const comprobanteSelect = page.getByLabel('Tipo de comprobante');
    await expect(comprobanteSelect).toBeVisible();
    await expect(comprobanteSelect.locator('option', { hasText: 'Factura Electrónica' })).not.toBeAttached();
  });
});

test.describe('Facturación SRI - Enabled', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('UsaFacturacionSRI TRUE shows Factura Electronica option', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Verify the Factura Electronica option IS available
    const comprobanteSelect = page.getByLabel('Tipo de comprobante');
    await expect(comprobanteSelect).toBeVisible();
    await expect(comprobanteSelect.locator('option', { hasText: 'Factura Electrónica' })).toBeAttached();
  });

  test('Selecting Factura Electronica requires client identification', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add product to cart
    await pos.addProduct('Camiseta Polo');

    // Select Factura Electronica
    await page.getByLabel('Tipo de comprobante').selectOption('Factura Electronica');

    // Try to confirm without selecting a client
    await pos.confirmButton.click();

    // Should show error about requiring client identification
    await expect(page.getByRole('alert').or(page.getByText(/cliente/i))).toBeVisible();
  });

  test('Attempting emission without client is blocked', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add product
    await pos.addProduct('Camiseta Polo');

    // Select Factura Electronica as comprobante type
    await page.getByLabel('Tipo de comprobante').selectOption('Factura Electronica');

    // Attempt to confirm sale without client selected
    await pos.confirmButton.click();

    // Expect the sale to be blocked with an error message about identification
    await expect(
      page.getByText(/identificación/i).or(page.getByText(/cliente.*requerido/i))
    ).toBeVisible();
  });
});
