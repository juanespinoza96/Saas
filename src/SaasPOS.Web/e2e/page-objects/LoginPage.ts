import { type Page, type Locator, expect } from '@playwright/test';

export class LoginPage {
  readonly page: Page;
  readonly emailInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;
  readonly errorMessage: Locator;
  readonly validationMessages: Locator;

  constructor(page: Page) {
    this.page = page;
    this.emailInput = page.getByLabel('Correo electrónico');
    this.passwordInput = page.getByLabel('Contraseña');
    this.submitButton = page.getByRole('button', { name: /ingresar/i });
    this.errorMessage = page.getByRole('alert').filter({ hasText: /credenciales/i });
    this.validationMessages = page.getByRole('alert');
  }

  async goto() {
    await this.page.goto('/login');
  }

  async login(email: string, password: string) {
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.submitButton.click();
  }

  async expectRedirectToDashboard() {
    await this.page.waitForURL('**/dashboard');
  }

  async expectErrorMessage(text?: string) {
    const errorEl = text
      ? this.page.getByRole('alert').filter({ hasText: text })
      : this.errorMessage;
    await expect(errorEl).toBeVisible();
  }

  async expectValidationError(text: string) {
    await expect(
      this.page.getByRole('alert').filter({ hasText: text })
    ).toBeVisible();
  }
}
