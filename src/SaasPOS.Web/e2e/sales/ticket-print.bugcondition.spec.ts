import { test, expect, type Page } from '@playwright/test';
import { POSNormalPage } from '../page-objects/POSNormalPage';

/**
 * Prueba exploratoria E2E de la Condición del Bug — Property 1
 * Spec: ticket-digital-pdf-blanco (bugfix)
 *
 * OBJETIVO: Exponer contraejemplos que demuestren que, bajo `@media print`,
 * `#ticket-print-root` NO es visible (hoja en blanco) porque su ancestro `#root`
 * queda oculto por la regla actual de aislamiento en `print.css`.
 *
 * Se usa `page.emulateMedia({ media: 'print' })` para evaluar el CSS `@media print`
 * REAL (Playwright/Chromium sí calcula layout y visibilidad de impresión).
 *
 * RESULTADO ESPERADO sobre el código SIN arreglar: estas pruebas FALLAN
 * (el ticket no es visible al imprimir), confirmando que el bug existe.
 * Tras el arreglo en `print.css`, estas MISMAS pruebas deben PASAR (tarea 3.2).
 *
 * **Validates: Requirements 1.1, 1.2, 1.3**
 */

// Helper: registra la venta con los productos actuales del carrito.
// Pulsa "Confirmar Venta" (abre el diálogo) y luego "Registrar Venta" (confirma).
async function registrarVenta(page: Page) {
  // Si el comercio tiene más de una sucursal, el POS muestra un selector y NO
  // autoselecciona. "Confirmar Venta" queda deshabilitado hasta elegir una, así
  // que seleccionamos la primera sucursal disponible cuando el selector exista.
  const sucursalSelect = page.getByLabel('Seleccionar sucursal');
  if (await sucursalSelect.isVisible().catch(() => false)) {
    await sucursalSelect.selectOption({ index: 1 });
  }

  await page.getByRole('button', { name: /confirmar venta/i }).click();
  await page.getByRole('button', { name: /registrar venta/i }).click();
  // Esperar el mensaje de éxito para asegurar que la venta se procesó y el ticket se montó
  await expect(page.getByText(/venta registrada exitosamente/i)).toBeVisible();
}

// Locator del ticket de impresión
function ticketLocator(page: Page) {
  return page.locator('#ticket-print-root');
}

test.describe('Bug Condition Exploration — Ticket visible al imprimir (Property 1)', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Caso 1 — Impresión manual, consumidor final: ticket visible con "CONSUMIDOR FINAL" y total', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // Registrar una venta sin cliente (consumidor final).
    // Usamos un producto real del comercio del usuario de prueba (Comercio Demo, Id 1).
    await pos.addProduct('Coca-Cola 500ml');
    await registrarVenta(page);

    // Emular el medio de impresión para evaluar `@media print` real
    await page.emulateMedia({ media: 'print' });

    const ticket = ticketLocator(page);

    // PROPIEDAD (Property 1): el ticket y su contenido deben ser visibles al imprimir.
    // FALLA sobre el código sin arreglar: #root está en display:none y oculta al ticket.
    await expect(ticket).toBeVisible();
    await expect(ticket.getByText('CONSUMIDOR FINAL')).toBeVisible();
    await expect(ticket.getByText('TOTAL:')).toBeVisible();
    await expect(ticket.getByText('¡Gracias por su compra!')).toBeVisible();

    // Restaurar medio de pantalla
    await page.emulateMedia({ media: 'screen' });
  });

  test('Caso 2 — Impresión manual, con cliente: ticket visible con nombre e identificación', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    await pos.addProduct('Coca-Cola 500ml');

    // Seleccionar un cliente existente del comercio de prueba (Comercio Demo, Id 1):
    // Carlos Mendoza / 1712345678.
    const clienteInput = page.getByLabel('Buscar cliente por identificación');
    if (await clienteInput.isVisible().catch(() => false)) {
      await clienteInput.fill('1712345678');
      await page.getByText('Carlos Mendoza').first().click();
    }

    await registrarVenta(page);

    await page.emulateMedia({ media: 'print' });

    const ticket = ticketLocator(page);

    // PROPIEDAD (Property 1): ticket visible con datos del cliente. FALLA sin arreglo.
    await expect(ticket).toBeVisible();
    await expect(ticket.getByText(/Carlos Mendoza/)).toBeVisible();
    await expect(ticket.getByText(/1712345678/)).toBeVisible();
    await expect(ticket.getByText('TOTAL:')).toBeVisible();

    await page.emulateMedia({ media: 'screen' });
  });

  test('Caso 3 — El resto de la UI del POS queda oculto pero el ticket NO (hoja en blanco = bug)', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    await pos.addProduct('Coca-Cola 500ml');
    await registrarVenta(page);

    await page.emulateMedia({ media: 'print' });

    const ticket = ticketLocator(page);

    // El encabezado del POS debe estar oculto al imprimir (aislamiento correcto — preservación)
    await expect(page.getByRole('heading', { name: 'Punto de Venta' })).not.toBeVisible();

    // PROPIEDAD (Property 1): pese al aislamiento, el ticket SÍ debe verse.
    // FALLA sobre el código sin arreglar (queda todo en blanco). Confirma el bug.
    await expect(ticket).toBeVisible();

    await page.emulateMedia({ media: 'screen' });
  });
});
