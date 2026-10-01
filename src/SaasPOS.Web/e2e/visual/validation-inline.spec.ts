import { test, expect } from '@playwright/test';

// ─── 34.1–34.4: Tests E2E — Validación Inline y Botones ─────────────────────
// Verifies:
//   - Submitting invalid form shows inline error messages (text-action-danger)
//   - Action button is disabled during pending request
//   - Double-click on submit only sends one request
//   - Disabled state shows loading indicator (spinner)

test.describe('Validation & Button States — Inline errors and loading', () => {
  test.use({ storageState: '.auth/gerente.json' });

  // ─── 34.1: Submitting invalid form shows inline errors ─────────────────────
  test('submitting empty required fields shows inline error messages', async ({ page }) => {
    await page.goto('/productos');

    // Open the "Nuevo Producto" modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();
    const modal = page.getByRole('dialog');
    await expect(modal, 'Modal should be visible').toBeVisible();

    // Click the submit button without filling required fields
    // Precio Lista defaults to 0 which triggers "El precio debe ser mayor a 0"
    // Nombre is empty → "El nombre es requerido"
    // Categoría is empty → "Seleccione una categoría"
    await modal.getByRole('button', { name: /Crear Producto/i }).click();

    // Verify inline error messages appear near invalid fields
    await expect(
      modal.locator('.text-action-danger', { hasText: 'El nombre es requerido' }),
      'Inline error for empty Nombre field should be visible'
    ).toBeVisible();

    await expect(
      modal.locator('.text-action-danger', { hasText: 'El precio debe ser mayor a 0' }),
      'Inline error for zero Precio Lista should be visible'
    ).toBeVisible();

    await expect(
      modal.locator('.text-action-danger', { hasText: 'Seleccione una categoría' }),
      'Inline error for empty Categoría should be visible'
    ).toBeVisible();
  });

  // ─── 34.2: Button disabled during pending request ──────────────────────────
  test('action button is disabled during pending request', async ({ page }) => {
    await page.goto('/productos');

    // Open the "Nuevo Producto" modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();
    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Fill valid data so validation passes
    await modal.locator('input[type="text"]').first().fill('Producto Test E2E');
    await modal.locator('input[type="number"]').first().fill('10.00');

    // Select first available category (if any exists)
    const categorySelect = modal.locator('select').nth(1); // second select (first is tipoArticulo)
    const options = await categorySelect.locator('option').count();
    if (options > 1) {
      await categorySelect.selectOption({ index: 1 });
    }

    // Intercept the API call and delay it so we can observe disabled state
    await page.route('**/api/tenants/productos', async (route) => {
      await new Promise(resolve => setTimeout(resolve, 2000));
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'test-id' }) });
    });

    // Click submit
    const submitButton = modal.getByRole('button', { name: /Crear Producto/i });
    await submitButton.click();

    // Verify button is disabled during the request
    await expect(
      submitButton,
      'Submit button should be disabled during pending request'
    ).toBeDisabled();

    // Verify aria-disabled or aria-busy is set
    await expect(
      submitButton,
      'Submit button should have aria-busy="true" during loading'
    ).toHaveAttribute('aria-busy', 'true');
  });

  // ─── 34.3: Double-click on submit only sends once ─────────────────────────
  test('double-click on submit only sends one request', async ({ page }) => {
    await page.goto('/productos');

    // Open the "Nuevo Producto" modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();
    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Fill valid data
    await modal.locator('input[type="text"]').first().fill('Producto Doble Click');
    await modal.locator('input[type="number"]').first().fill('15.00');

    // Select first available category
    const categorySelect = modal.locator('select').nth(1);
    const options = await categorySelect.locator('option').count();
    if (options > 1) {
      await categorySelect.selectOption({ index: 1 });
    }

    // Track how many times the API is called
    let requestCount = 0;
    await page.route('**/api/tenants/productos', async (route) => {
      requestCount++;
      await new Promise(resolve => setTimeout(resolve, 1500));
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'test-id-2' }) });
    });

    // Click submit twice rapidly
    const submitButton = modal.getByRole('button', { name: /Crear Producto/i });
    await submitButton.click();
    await submitButton.click({ force: true }); // force: true bypasses disabled check from Playwright

    // Wait for the delayed response to complete
    await page.waitForTimeout(2000);

    // Verify only one request was sent (button disables after first click)
    expect(requestCount, 'API should only be called once despite double-click').toBe(1);
  });

  // ─── 34.4: Disabled state shows loading indicator ──────────────────────────
  test('disabled state shows loading indicator (spinner)', async ({ page }) => {
    await page.goto('/productos');

    // Open the "Nuevo Producto" modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();
    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Fill valid data
    await modal.locator('input[type="text"]').first().fill('Producto Spinner Test');
    await modal.locator('input[type="number"]').first().fill('20.00');

    // Select first available category
    const categorySelect = modal.locator('select').nth(1);
    const options = await categorySelect.locator('option').count();
    if (options > 1) {
      await categorySelect.selectOption({ index: 1 });
    }

    // Intercept API with delay to observe loading state
    await page.route('**/api/tenants/productos', async (route) => {
      await new Promise(resolve => setTimeout(resolve, 3000));
      await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 'test-id-3' }) });
    });

    // Click submit
    const submitButton = modal.getByRole('button', { name: /Crear Producto/i });
    await submitButton.click();

    // Verify the spinner SVG with animate-spin class is visible inside the button
    const spinner = submitButton.locator('svg.animate-spin');
    await expect(
      spinner,
      'Loading spinner (animate-spin SVG) should be visible in button during request'
    ).toBeVisible();

    // Verify aria-busy is set to true
    await expect(
      submitButton,
      'Button should have aria-busy="true" while loading'
    ).toHaveAttribute('aria-busy', 'true');

    // Verify the button has disabled attribute
    await expect(
      submitButton,
      'Button should have aria-disabled="true" while loading'
    ).toHaveAttribute('aria-disabled', 'true');
  });
});
