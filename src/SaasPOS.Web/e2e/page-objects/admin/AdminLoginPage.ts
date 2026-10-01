import { type Page, type Locator, expect } from '@playwright/test';

const ADMIN_BASE_URL = 'http://localhost:3001';

export class AdminLoginPage {
  readonly page: Page;
  readonly emailInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;
  readonly errorMessage: Locator;
  readonly heading: Locator;

  constructor(page: Page) {
    this.page = page;
    this.emailInput = page.getByLabel('Correo electrónico');
    this.passwordInput = page.getByLabel('Contraseña');
    this.submitButton = page.getByRole('button', { name: /ingresar/i });
    this.errorMessage = page.locator('.bg-action-danger\\/10');
    this.heading = page.getByRole('heading', { name: 'SaaS POS Admin' });
  }

  async goto() {
    await this.page.goto(`${ADMIN_BASE_URL}/login`);
  }

  async login(email: string, password: string) {
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.submitButton.click();
  }

  async expectRedirectToAdminDashboard() {
    await this.page.waitForURL(`${ADMIN_BASE_URL}/`);
  }

  async expectErrorMessage(text?: string) {
    if (text) {
      await expect(this.errorMessage.filter({ hasText: text })).toBeVisible();
    } else {
      await expect(this.errorMessage).toBeVisible();
    }
  }
}
