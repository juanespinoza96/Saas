import { type Page, type Locator, expect } from '@playwright/test';

export class ReportesPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly blockedMessage: Locator;
  readonly topProductosCard: Locator;
  readonly topCategoriasCard: Locator;
  readonly topSucursalesCard: Locator;
  readonly customReportButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Reportes' });
    this.blockedMessage = page.getByText(/reportes no están disponibles/i);
    this.topProductosCard = page.getByRole('heading', { name: 'Top Productos' });
    this.topCategoriasCard = page.getByRole('heading', { name: 'Top Categorías' });
    this.topSucursalesCard = page.getByRole('heading', { name: 'Top Sucursales' });
    this.customReportButton = page.getByRole('button', { name: /generar reporte personalizado/i });
  }

  async goto() {
    await this.page.goto('/reportes');
  }

  async expectLoaded() {
    await expect(this.heading).toBeVisible();
  }

  async expectBlocked() {
    await expect(this.blockedMessage).toBeVisible();
  }

  async expectPredefinedReportsVisible() {
    await expect(this.topProductosCard).toBeVisible();
    await expect(this.topCategoriasCard).toBeVisible();
    await expect(this.topSucursalesCard).toBeVisible();
  }

  async expectCustomFiltersVisible() {
    await expect(this.customReportButton).toBeVisible();
  }

  async expectCustomFiltersNotVisible() {
    await expect(this.customReportButton).not.toBeVisible();
  }

  async selectReport(name: string) {
    await this.page.getByRole('heading', { name }).click();
  }

  async applyFilter(filterLabel: string, value: string) {
    await this.page.getByLabel(new RegExp(filterLabel, 'i')).fill(value);
  }

  async exportPdf(reportName: string) {
    const card = this.page.getByRole('heading', { name: reportName }).locator('..');
    await card.getByRole('button', { name: 'PDF' }).click();
  }

  async exportCsv(reportName: string) {
    const card = this.page.getByRole('heading', { name: reportName }).locator('..');
    await card.getByRole('button', { name: 'CSV' }).click();
  }
}
