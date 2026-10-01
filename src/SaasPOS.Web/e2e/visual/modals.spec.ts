import { test, expect } from '@playwright/test';

// ─── 33.2–33.4: Tests E2E — Modales ─────────────────────────────────────────
// Verifies modal behavior:
//   - Forms open in modal (role="dialog", aria-modal="true")
//   - Modal closes with X button (aria-label="Cerrar modal")
//   - Modal closes with Escape key
//   - Unsaved changes trigger warning before closing

test.describe('Modals — Form dialogs', () => {
  test.use({ storageState: '.auth/gerente.json' });

  // ─── 33.2: Forms open in modal and close with X button ─────────────────────
  test('create form opens in a modal with role="dialog" and aria-modal="true"', async ({ page }) => {
    await page.goto('/productos');

    // Click "Nuevo Producto" to open the create modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();

    // Verify the modal is present with correct ARIA attributes
    const modal = page.getByRole('dialog');
    await expect(modal, 'Modal should appear after clicking create button').toBeVisible();
    await expect(modal).toHaveAttribute('aria-modal', 'true');

    // Verify the modal title
    await expect(page.locator('#modal-title')).toContainText(/Nuevo Producto/i);
  });

  test('modal closes when clicking the X button', async ({ page }) => {
    await page.goto('/productos');

    // Open the create modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();

    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Click the X close button
    const closeButton = page.getByRole('button', { name: 'Cerrar modal' });
    await expect(closeButton, 'Close (X) button should be visible').toBeVisible();
    await closeButton.click();

    // Verify modal is gone
    await expect(modal, 'Modal should be hidden after clicking X').not.toBeVisible();
  });

  // ─── 33.3: Modal closes with Escape key ───────────────────────────────────
  test('modal closes when pressing Escape key', async ({ page }) => {
    await page.goto('/productos');

    // Open the create modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();

    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Press Escape
    await page.keyboard.press('Escape');

    // Verify modal is gone
    await expect(modal, 'Modal should be hidden after pressing Escape').not.toBeVisible();
  });

  // ─── 33.4: Unsaved changes prompt warning before closing modal ─────────────
  test('unsaved changes trigger warning when trying to close via X button', async ({ page }) => {
    await page.goto('/productos');

    // Open the create modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();

    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Type into the form to make it dirty (triggers formDirty = true)
    const nombreInput = modal.getByRole('textbox').first();
    await nombreInput.fill('Producto Test Sin Guardar');

    // Set up dialog handler to dismiss the confirm prompt (cancel closing)
    let dialogMessage = '';
    page.on('dialog', async (dialog) => {
      dialogMessage = dialog.message();
      await dialog.dismiss(); // Click "Cancel" on the native confirm
    });

    // Click the X close button — this should trigger window.confirm
    await page.getByRole('button', { name: 'Cerrar modal' }).click();

    // Verify the confirm dialog was shown with the expected message
    expect(dialogMessage).toContain('¿Estás seguro?');
    expect(dialogMessage).toContain('cambios no guardados');

    // Modal should still be visible since we dismissed (cancelled) the confirm
    await expect(modal, 'Modal should remain open after dismissing the warning').toBeVisible();
  });

  test('unsaved changes warning allows closing when accepted', async ({ page }) => {
    await page.goto('/productos');

    // Open the create modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();

    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Type into the form to make it dirty
    const nombreInput = modal.getByRole('textbox').first();
    await nombreInput.fill('Producto Test Sin Guardar');

    // Set up dialog handler to accept the confirm prompt (proceed with closing)
    page.on('dialog', async (dialog) => {
      await dialog.accept(); // Click "OK" on the native confirm
    });

    // Click the X close button
    await page.getByRole('button', { name: 'Cerrar modal' }).click();

    // Modal should now be closed since we accepted the confirm
    await expect(modal, 'Modal should close after accepting the warning').not.toBeVisible();
  });

  test('unsaved changes warning triggers on Escape key', async ({ page }) => {
    await page.goto('/productos');

    // Open the create modal
    await page.getByRole('button', { name: /Nuevo Producto/i }).click();

    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Type into the form to make it dirty
    const nombreInput = modal.getByRole('textbox').first();
    await nombreInput.fill('Producto Test Escape');

    // Set up dialog handler to dismiss the confirm
    let dialogTriggered = false;
    page.on('dialog', async (dialog) => {
      dialogTriggered = true;
      await dialog.dismiss();
    });

    // Press Escape — this should trigger the warning
    await page.keyboard.press('Escape');

    // Verify the dialog was triggered
    expect(dialogTriggered, 'Escape on dirty form should trigger confirm dialog').toBe(true);

    // Modal should still be open since we dismissed
    await expect(modal, 'Modal should remain open after dismissing Escape warning').toBeVisible();
  });
});
