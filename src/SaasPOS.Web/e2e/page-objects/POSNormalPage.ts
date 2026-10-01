import { type Page, type Locator, expect } from '@playwright/test';

export class POSNormalPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly productSearch: Locator;
  readonly cartSection: Locator;
  readonly totalDisplay: Locator;
  readonly confirmButton: Locator;
  readonly emptyCartMessage: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Punto de Venta' });
    this.productSearch = page.getByLabel('Buscar producto');
    this.cartSection = page.getByRole('heading', { name: 'Detalle de Venta' }).locator('..');
    this.totalDisplay = page.locator('text=Total').locator('..').locator('.text-xl');
    this.confirmButton = page.getByRole('button', { name: /confirmar venta/i });
    this.emptyCartMessage = page.getByText('Agregue productos para comenzar.');
  }

  async goto() {
    await this.page.goto('/pos');
  }

  async addProduct(productName: string) {
    await this.page.getByRole('button', { name: `Agregar ${productName} al carrito` }).click();
  }

  async searchProduct(term: string) {
    await this.productSearch.fill(term);
  }

  async updateQuantity(productName: string, newQty: number) {
    const input = this.page.getByLabel(`Cantidad de ${productName}`);
    await input.fill(String(newQty));
  }

  async increaseQuantity(productName: string) {
    await this.page.getByLabel(`Aumentar cantidad de ${productName}`).click();
  }

  async decreaseQuantity(productName: string) {
    await this.page.getByLabel(`Reducir cantidad de ${productName}`).click();
  }

  async removeItem(productName: string) {
    await this.page.getByLabel(`Eliminar ${productName} del carrito`).click();
  }

  async getTotal(): Promise<string> {
    const totalRow = this.page.locator('.border-t').filter({ hasText: 'Total' });
    const amount = totalRow.locator('.text-xl');
    return (await amount.textContent()) ?? '$0.00';
  }

  async expectProductVisible(productName: string) {
    await expect(
      this.page.getByRole('button', { name: `Agregar ${productName} al carrito` })
    ).toBeVisible();
  }

  async expectProductNotVisible(productName: string) {
    await expect(
      this.page.getByRole('button', { name: `Agregar ${productName} al carrito` })
    ).not.toBeVisible();
  }

  async getCartItemPrice(productName: string): Promise<string> {
    const item = this.page.locator(`text=${productName}`).locator('..');
    const price = item.locator('text=/\\$.*c\\/u/');
    return (await price.textContent()) ?? '';
  }

  async expectLoaded() {
    await expect(this.heading).toBeVisible();
  }
}
