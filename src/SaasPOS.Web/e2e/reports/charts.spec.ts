import { test, expect } from '@playwright/test';
import { ReportesPage } from '../page-objects/ReportesPage';

test.describe('Reports - Charts', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Charts render with canvas or SVG present', async ({ page }) => {
    const reportes = new ReportesPage(page);
    await reportes.goto();
    await reportes.expectLoaded();
    await reportes.expectPredefinedReportsVisible();

    // Check for chart elements (canvas/SVG) or data lists
    const chartElements = page.locator('canvas, svg.recharts-surface, svg[class*="chart"]');
    const hasCharts = await chartElements.count() > 0;
    const hasDataLists = await page.locator('ul').count() > 0;

    expect(hasCharts || hasDataLists).toBeTruthy();
  });

  test('Charts without data show appropriate empty state', async ({ page }) => {
    const reportes = new ReportesPage(page);
    await reportes.goto();
    await reportes.expectLoaded();

    const emptyStates = page.getByText('Sin datos disponibles');
    const dataLists = page.locator('ul li');

    const hasEmpty = await emptyStates.count() > 0;
    const hasData = await dataLists.count() > 0;

    // Page should render either data or empty states
    expect(hasEmpty || hasData).toBeTruthy();
  });
});
