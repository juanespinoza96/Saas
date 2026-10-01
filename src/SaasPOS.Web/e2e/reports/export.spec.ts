import { test, expect } from '@playwright/test';
import { ReportesPage } from '../page-objects/ReportesPage';

test.describe('Reports - Export', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('PDF export triggers file download', async ({ page }) => {
    const reportes = new ReportesPage(page);
    await reportes.goto();
    await reportes.expectLoaded();
    await reportes.expectPredefinedReportsVisible();

    const downloadPromise = page.waitForEvent('download');
    await reportes.exportPdf('Top Productos');
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toContain('.pdf');
  });

  test('CSV export triggers file download', async ({ page }) => {
    const reportes = new ReportesPage(page);
    await reportes.goto();
    await reportes.expectLoaded();
    await reportes.expectPredefinedReportsVisible();

    const downloadPromise = page.waitForEvent('download');
    await reportes.exportCsv('Top Productos');
    const download = await downloadPromise;

    expect(download.suggestedFilename()).toContain('.csv');
  });
});
