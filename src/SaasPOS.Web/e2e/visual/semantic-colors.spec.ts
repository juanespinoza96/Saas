import { test, expect } from '@playwright/test';

// ─── 33.1: Tests E2E — Colores Semánticos ───────────────────────────────────
// Verifies that semantic button colors follow the design system:
//   - Confirm buttons → green (bg-action-confirm)
//   - Edit buttons → yellow (bg-action-edit)
//   - Danger/Delete buttons → red (bg-action-danger)

test.describe('Semantic Colors — Button Variants', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('confirm buttons use green (bg-action-confirm)', async ({ page }) => {
    await page.goto('/productos');

    // The "Nuevo Producto" button is variant="confirm"
    const confirmButton = page.getByRole('button', { name: /Nuevo Producto/i });
    await expect(confirmButton, 'Confirm button should be visible').toBeVisible();

    // Verify it has the green confirm class
    await expect(confirmButton).toHaveClass(/bg-action-confirm/);
  });

  test('edit buttons use yellow (bg-action-edit)', async ({ page }) => {
    await page.goto('/productos');

    // Wait for the product table to load
    const editButton = page.getByRole('button', { name: /Editar/i }).first();

    // If there are no products, skip gracefully
    if (await editButton.isVisible({ timeout: 5000 }).catch(() => false)) {
      await expect(editButton).toHaveClass(/bg-action-edit/);
    } else {
      // Navigate to usuarios which also has edit buttons
      await page.goto('/usuarios');
      const userEditButton = page.getByRole('button', { name: /Editar/i }).first();
      await expect(userEditButton, 'Edit button should be visible').toBeVisible();
      await expect(userEditButton).toHaveClass(/bg-action-edit/);
    }
  });

  test('delete/danger buttons use red (bg-action-danger)', async ({ page }) => {
    await page.goto('/productos');

    // Wait for the product table to load
    const deleteButton = page.getByRole('button', { name: /Eliminar/i }).first();

    // If there are no products, try usuarios which also has danger buttons
    if (await deleteButton.isVisible({ timeout: 5000 }).catch(() => false)) {
      await expect(deleteButton).toHaveClass(/bg-action-danger/);
    } else {
      await page.goto('/usuarios');
      const deactivateButton = page.getByRole('button', { name: /Desactivar/i }).first();
      await expect(deactivateButton, 'Danger button should be visible').toBeVisible();
      await expect(deactivateButton).toHaveClass(/bg-action-danger/);
    }
  });

  test('all three semantic colors coexist on the same page', async ({ page }) => {
    await page.goto('/productos');

    // Confirm: "Nuevo Producto" button
    const confirmButton = page.getByRole('button', { name: /Nuevo Producto/i });
    await expect(confirmButton).toBeVisible();
    await expect(confirmButton).toHaveClass(/bg-action-confirm/);

    // If the page has product rows with Edit and Delete buttons
    const editButtons = page.getByRole('button', { name: /Editar/i });
    const deleteButtons = page.getByRole('button', { name: /Eliminar/i });

    if (await editButtons.first().isVisible({ timeout: 5000 }).catch(() => false)) {
      await expect(editButtons.first()).toHaveClass(/bg-action-edit/);
      await expect(deleteButtons.first()).toHaveClass(/bg-action-danger/);
    }
  });
});
