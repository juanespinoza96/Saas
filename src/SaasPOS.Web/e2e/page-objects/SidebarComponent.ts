import { type Page, type Locator, expect } from '@playwright/test';

export class SidebarComponent {
  readonly page: Page;
  readonly sidebar: Locator;
  readonly navList: Locator;
  readonly themeToggle: Locator;
  readonly logoutButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.sidebar = page.getByRole('navigation', { name: 'Navegación principal' });
    this.navList = page.getByRole('navigation', { name: 'Menú del POS' }).getByRole('list');
    this.themeToggle = page.getByRole('button', { name: /modo (oscuro|claro)/i });
    this.logoutButton = page.getByRole('button', { name: 'Cerrar sesión' });
  }

  async getVisibleOptions(): Promise<string[]> {
    const items = this.navList.getByRole('listitem');
    const count = await items.count();
    const labels: string[] = [];
    for (let i = 0; i < count; i++) {
      const text = await items.nth(i).locator('span').textContent();
      if (text) labels.push(text.trim());
    }
    return labels;
  }

  async expectOptions(expectedLabels: string[]) {
    const visibleOptions = await this.getVisibleOptions();
    expect(visibleOptions).toEqual(expectedLabels);
  }

  async expectContainsOption(label: string) {
    await expect(
      this.navList.getByRole('link', { name: label })
    ).toBeVisible();
  }

  async expectNotContainsOption(label: string) {
    await expect(
      this.navList.getByRole('link', { name: label })
    ).not.toBeVisible();
  }

  async clickOption(label: string) {
    await this.navList.getByRole('link', { name: label }).click();
  }
}
