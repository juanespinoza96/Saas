import { test, expect } from '@playwright/test';
import { POSNormalPage } from '../page-objects/POSNormalPage';

test.describe('Volume Pricing', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Below CantidadMinima shows regular price', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add "Camiseta Polo" (regular price $15.00, volume: ≥10 → $12.00, ≥50 → $10.00)
    await pos.addProduct('Camiseta Polo');

    // Set quantity to 5 — below any volume threshold
    await pos.updateQuantity('Camiseta Polo', 5);

    // Total should be 5 * $15.00 = $75.00 (regular price)
    const total = await pos.getTotal();
    expect(total).toContain('75.00');
  });

  test('Reaching CantidadMinima applies PrecioEspecial', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Add "Camiseta Polo" and set quantity to 10 (triggers ≥10 → $12.00)
    await pos.addProduct('Camiseta Polo');
    await pos.updateQuantity('Camiseta Polo', 10);

    // Total should be 10 * $12.00 = $120.00 (volume price applied)
    const total = await pos.getTotal();
    expect(total).toContain('120.00');
  });
});
