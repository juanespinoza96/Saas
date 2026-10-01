import { type Page, type Locator, expect } from '@playwright/test';

export class DashboardPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly salesCard: Locator;
  readonly notificationButton: Locator;
  readonly notificationBadge: Locator;
  readonly quickAccessSection: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Dashboard' });
    this.salesCard = page.getByText('Ventas del Día').locator('..').locator('..');
    this.notificationButton = page.locator('button[aria-label*="Notificaciones"]');
    this.notificationBadge = page.locator('button[aria-label*="Notificaciones"] span.bg-action-danger');
    this.quickAccessSection = page.getByRole('heading', { name: 'Accesos Rápidos' }).locator('..');
  }

  async expectLoaded() {
    await expect(this.heading).toBeVisible();
  }

  async getSalesTotal(): Promise<string> {
    const card = this.page.getByText('Ventas del Día').locator('..').locator('..');
    const value = card.locator('p.text-xl');
    return (await value.textContent()) ?? '$0.00';
  }

  async getNotificationCount(): Promise<number> {
    if (await this.notificationBadge.isVisible()) {
      const text = await this.notificationBadge.textContent();
      return parseInt(text ?? '0', 10);
    }
    return 0;
  }

  async getQuickAccessLabels(): Promise<string[]> {
    const buttons = this.quickAccessSection.locator('button span.text-sm');
    return buttons.allTextContents();
  }
}
