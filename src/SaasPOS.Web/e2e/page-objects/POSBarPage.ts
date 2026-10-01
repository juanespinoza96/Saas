import { type Page, type Locator, expect } from '@playwright/test';

export class POSBarPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly productGrid: Locator;
  readonly productSearch: Locator;
  readonly dailySalesCounter: Locator;
  readonly unavailableMessage: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Bar Escolar' });
    this.productGrid = page.locator('.grid');
    this.productSearch = page.getByLabel('Buscar producto');
    this.dailySalesCounter = page.getByText(/ventas hoy/i);
    this.unavailableMessage = page.getByRole('heading', { name: 'Modo Bar Escolar no disponible' });
  }

  async goto() {
    await this.page.goto('/pos-bar');
  }

  async clickProduct(productName: string) {
    await this.page.getByRole('button', { name: new RegExp(`Vender ${productName}`) }).click();
  }

  async expectGridVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.productGrid.first()).toBeVisible();
  }

  async expectProductInGrid(productName: string) {
    await expect(
      this.page.getByRole('button', { name: new RegExp(`Vender ${productName}`) })
    ).toBeVisible();
  }

  async expectProductNotInGrid(productName: string) {
    await expect(
      this.page.getByRole('button', { name: new RegExp(`Vender ${productName}`) })
    ).not.toBeVisible();
  }

  async expectNoClientField() {
    await expect(this.page.getByLabel('Buscar cliente por identificación')).not.toBeVisible();
    await expect(this.page.getByText('Cliente (opcional)')).not.toBeVisible();
  }

  async expectNoComprobanteSelection() {
    await expect(this.page.getByLabel('Tipo de comprobante')).not.toBeVisible();
  }

  async expectUnavailable() {
    await expect(this.unavailableMessage).toBeVisible();
  }

  async expectSaleSuccess(productName: string) {
    await expect(this.page.getByText(new RegExp(`✓ ${productName}`))).toBeVisible();
  }
}
