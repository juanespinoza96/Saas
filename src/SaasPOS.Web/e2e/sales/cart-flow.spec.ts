import { test, expect } from '@playwright/test';
import { POSNormalPage } from '../page-objects/POSNormalPage';

test.describe('Cart Flow', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Add product shows correct price in cart', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add "Camiseta Polo" at $15.00
    await pos.addProduct('Camiseta Polo');

    // Verify total reflects the product price
    const total = await pos.getTotal();
    expect(total).toContain('15.00');
  });

  test('Modify quantity recalculates total', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add product and change quantity to 3
    await pos.addProduct('Camiseta Polo');
    await pos.updateQuantity('Camiseta Polo', 3);

    // Total should be 3 * $15.00 = $45.00
    const total = await pos.getTotal();
    expect(total).toContain('45.00');
  });

  test('Removing product updates total', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add two different products
    await pos.addProduct('Camiseta Polo');
    await pos.addProduct('Pantalón Jean');

    // Total should be $15.00 + $35.00 = $50.00
    let total = await pos.getTotal();
    expect(total).toContain('50.00');

    // Remove one product — total should drop to $35.00
    await pos.removeItem('Camiseta Polo');
    total = await pos.getTotal();
    expect(total).toContain('35.00');
  });

  test('Insumo products not visible in selection', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // VentaDirecta products should be visible
    await pos.expectProductVisible('Camiseta Polo');

    // Insumo products should NOT appear in the POS grid
    await pos.expectProductNotVisible('Harina (kg)');
    await pos.expectProductNotVisible('Azúcar (kg)');
  });
});
