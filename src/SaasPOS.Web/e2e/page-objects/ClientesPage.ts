import { type Page, type Locator, expect } from '@playwright/test';

export class ClientesPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly newClientButton: Locator;
  readonly searchInput: Locator;
  readonly searchButton: Locator;
  readonly clientTable: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: /clientes/i });
    this.newClientButton = page.getByRole('button', { name: /nuevo cliente/i });
    this.searchInput = page.getByPlaceholder('Buscar por identificación...');
    this.searchButton = page.getByRole('button', { name: 'Buscar' });
    this.clientTable = page.locator('table');
  }

  async goto() {
    await this.page.goto('/clientes');
  }

  async expectLoaded() {
    await expect(this.heading).toBeVisible();
  }

  async createClient(data: {
    identificacion: string;
    nombre: string;
    correo?: string;
    direccion?: string;
    telefono?: string;
    esConsumidorFinal?: boolean;
  }) {
    await this.newClientButton.click();

    // Fill Identificación
    const identInput = this.page.locator('label:has-text("Identificación") + input');
    await identInput.fill(data.identificacion);

    // Fill Nombre
    const nombreInput = this.page.locator('label:has-text("Nombre") + input');
    await nombreInput.fill(data.nombre);

    // Check consumidor final if needed
    if (data.esConsumidorFinal) {
      await this.page.getByLabel(/es consumidor final/i).check();
    }

    // Fill optional fields
    if (data.correo) {
      const correoInput = this.page.locator('label:has-text("Correo") + input');
      await correoInput.fill(data.correo);
    }
    if (data.direccion) {
      const dirInput = this.page.locator('label:has-text("Dirección") + input');
      await dirInput.fill(data.direccion);
    }
    if (data.telefono) {
      const telInput = this.page.locator('label:has-text("Teléfono") + input');
      await telInput.fill(data.telefono);
    }

    // Click create button
    await this.page.getByRole('button', { name: /crear cliente/i }).click();
  }

  async searchClient(identification: string) {
    await this.searchInput.fill(identification);
    await this.searchButton.click();
  }

  async expectClientInList(name: string) {
    await expect(this.page.getByRole('cell', { name })).toBeVisible();
  }

  async expectDuplicateError() {
    // The API returns a 409 for duplicates; the frontend may show an inline error
    // or an alert. We check for common duplicate-related text patterns.
    await expect(
      this.page.getByText(/ya existe|ya está registrad|duplicad/i)
    ).toBeVisible({ timeout: 5000 });
  }

  async expectEmptyState() {
    await expect(this.page.getByText('No se encontraron clientes')).toBeVisible();
  }
}
