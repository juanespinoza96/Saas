import { test } from '@playwright/test';
import { POSBarPage } from '../page-objects/POSBarPage';

test.describe('Bar Escolar - Grid Sales', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Product grid is visible in Bar Escolar mode', async ({ page }) => {
    const bar = new POSBarPage(page);
    await bar.goto();
    await bar.expectGridVisible();
  });

  test('Clicking product registers sale immediately', async ({ page }) => {
    const bar = new POSBarPage(page);
    await bar.goto();
    await bar.expectGridVisible();
    await bar.clickProduct('Camiseta Polo');
    await bar.expectSaleSuccess('Camiseta Polo');
  });

  test('No client field in bar escolar mode', async ({ page }) => {
    const bar = new POSBarPage(page);
    await bar.goto();
    await bar.expectGridVisible();
    await bar.expectNoClientField();
  });

  test('No comprobante selection in bar escolar mode', async ({ page }) => {
    const bar = new POSBarPage(page);
    await bar.goto();
    await bar.expectGridVisible();
    await bar.expectNoComprobanteSelection();
  });

  test('Insumo products not visible in grid', async ({ page }) => {
    const bar = new POSBarPage(page);
    await bar.goto();
    await bar.expectGridVisible();
    await bar.expectProductNotInGrid('Harina');
    await bar.expectProductNotInGrid('Azúcar');
    await bar.expectProductInGrid('Camiseta Polo');
  });
});
