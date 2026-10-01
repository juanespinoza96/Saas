import { test, expect, type Page } from '@playwright/test';
import { POSNormalPage } from '../page-objects/POSNormalPage';

/**
 * Pruebas de PRESERVACIÓN E2E — Property 2
 * Spec: ticket-digital-pdf-blanco (bugfix)
 *
 * OBJETIVO: Capturar el comportamiento base (a preservar) sobre el código SIN
 * arreglar, para las entradas donde `isBugCondition` retorna `false`. En concreto,
 * los invariantes que jsdom (Vitest) NO puede evaluar y que requieren `@media print`
 * real (Playwright/Chromium sí calcula layout y visibilidad de impresión):
 *   (a) En vista de pantalla (medio `screen`), `#ticket-print-root` está OCULTO
 *       (clase Tailwind `hidden`).
 *   (b) Bajo `@media print`, el resto de la UI del POS (encabezados, carrito,
 *       productos) NO es visible (aislamiento).
 *   (c) La regla `@page` mantiene `margin: 5mm` y `size: 80mm auto` (formato de
 *       impresora térmica).
 *
 * METODOLOGÍA (observation-first): estas aserciones codifican el comportamiento
 * OBSERVADO sobre el código sin arreglar. Como el arreglo es exclusivamente el
 * bloque `@media print` de print.css y NO cambia el aislamiento del resto de la
 * UI ni la regla `@page`, estas pruebas DEBEN PASAR antes y después del arreglo.
 *
 * NOTA: La visibilidad del PROPIO ticket bajo `@media print` es la Property 1
 * (condición del bug) y se cubre en ticket-print.bugcondition.spec.ts — NO se
 * afirma aquí, porque falla sobre el código sin arreglar por diseño.
 *
 * **Validates: Requirements 3.1, 3.2, 3.3**
 */

// Enfoque property-based acotado: conjunto de escenarios de venta generados
// (con/sin cliente) sobre los que deben cumplirse los MISMOS invariantes de
// preservación. Determinista para reproducibilidad E2E.
interface EscenarioVenta {
  descripcion: string;
  conCliente: boolean;
}

const escenarios: EscenarioVenta[] = [
  { descripcion: 'consumidor final (sin cliente)', conCliente: false },
  { descripcion: 'con cliente seleccionado', conCliente: true },
];

// Helper: si el comercio tiene más de una sucursal, el POS muestra un selector
// y el botón "Confirmar Venta" permanece deshabilitado hasta elegir una. Este
// helper selecciona la primera sucursal disponible cuando el selector existe.
async function seleccionarSucursalSiAplica(page: Page) {
  const selector = page.getByLabel('Seleccionar sucursal');
  if (await selector.isVisible().catch(() => false)) {
    // Elegir la primera opción real (índice 1; el índice 0 es "Seleccionar sucursal").
    const opciones = selector.locator('option');
    const valor = await opciones.nth(1).getAttribute('value');
    if (valor) await selector.selectOption(valor);
  }
}

// Helper: registra la venta con los productos actuales del carrito.
async function registrarVenta(page: Page) {
  await seleccionarSucursalSiAplica(page);
  await page.getByRole('button', { name: /confirmar venta/i }).click();
  await page.getByRole('button', { name: /registrar venta/i }).click();
  await expect(page.getByText(/venta registrada exitosamente/i)).toBeVisible();
}

function ticketLocator(page: Page) {
  return page.locator('#ticket-print-root');
}

/**
 * Extrae el texto crudo del bloque `@page` desde el CSS cargado en la página.
 *
 * NOTA: La propiedad `size` de `@page` NO se expone de forma fiable en la CSSOM
 * de Chromium (`CSSPageRule.style.getPropertyValue('size')` y `cssText` la omiten).
 * Por eso, en lugar de leer la CSSOM, recolectamos el CSS crudo servido: el texto
 * de los `<style>` inyectados por Vite en desarrollo más el contenido de las hojas
 * enlazadas (`<link rel="stylesheet">`) obtenido por fetch. Luego buscamos el
 * bloque `@page { ... }` y sus declaraciones `margin` y `size`.
 */
async function leerReglaAtPage(page: Page): Promise<{ margin: string; size: string } | null> {
  const cssCrudo: string = await page.evaluate(async () => {
    const partes: string[] = [];
    // Estilos inyectados inline (Vite dev sirve el CSS como <style>).
    for (const style of Array.from(document.querySelectorAll('style'))) {
      partes.push(style.textContent ?? '');
    }
    // Hojas enlazadas (build de producción): obtener su contenido por fetch.
    const links = Array.from(
      document.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]'),
    );
    for (const link of links) {
      try {
        const res = await fetch(link.href);
        partes.push(await res.text());
      } catch {
        // Ignorar hojas inaccesibles.
      }
    }
    return partes.join('\n');
  });

  // Buscar el bloque @page { ... } y extraer margin y size.
  const pageMatch = /@page\s*\{([^}]*)\}/i.exec(cssCrudo);
  if (!pageMatch) return null;
  const cuerpo = pageMatch[1];
  const marginMatch = /margin:\s*([^;]+)/i.exec(cuerpo);
  const sizeMatch = /size:\s*([^;]+)/i.exec(cuerpo);
  return {
    margin: marginMatch ? marginMatch[1].trim() : '',
    size: sizeMatch ? sizeMatch[1].trim() : '',
  };
}

test.describe('Preservation — Aislamiento de UI y formato @page (Property 2)', () => {
  test.use({ storageState: '.auth/gerente.json' });

  for (const escenario of escenarios) {
    test(`(a) Pantalla — #ticket-print-root oculto en medio screen [${escenario.descripcion}]`, async ({ page }) => {
      const pos = new POSNormalPage(page);
      await pos.goto();
      await pos.expectLoaded();

      await pos.addProduct('Audífonos Bluetooth');

      if (escenario.conCliente) {
        const clienteInput = page.getByLabel('Buscar cliente por identificación');
        if (await clienteInput.isVisible().catch(() => false)) {
          await clienteInput.fill('1712345678');
          await page.getByText('Carlos Mendoza').first().click();
        }
      }

      await registrarVenta(page);

      // Asegurar medio de pantalla (no impresión)
      await page.emulateMedia({ media: 'screen' });

      const ticket = ticketLocator(page);

      // PRESERVACIÓN 3.1: en pantalla el ticket permanece oculto (clase `hidden`).
      await expect(ticket).toBeHidden();
      await expect(ticket).toHaveClass(/hidden/);

      // El resto de la UI del POS SÍ es visible en pantalla (normalidad).
      await expect(page.getByRole('heading', { name: 'Punto de Venta' })).toBeVisible();
    });

    test(`(b) Impresión — resto de la UI del POS oculto bajo @media print [${escenario.descripcion}]`, async ({ page }) => {
      const pos = new POSNormalPage(page);
      await pos.goto();
      await pos.expectLoaded();

      await pos.addProduct('Audífonos Bluetooth');

      if (escenario.conCliente) {
        const clienteInput = page.getByLabel('Buscar cliente por identificación');
        if (await clienteInput.isVisible().catch(() => false)) {
          await clienteInput.fill('1712345678');
          await page.getByText('Carlos Mendoza').first().click();
        }
      }

      await registrarVenta(page);

      // Emular el medio de impresión para evaluar `@media print` real
      await page.emulateMedia({ media: 'print' });

      // PRESERVACIÓN 3.2: el resto de la interfaz del POS queda oculto al imprimir.
      // (El encabezado "Punto de Venta" es un hijo de #root, ocultado por el aislamiento.)
      await expect(page.getByRole('heading', { name: 'Punto de Venta' })).not.toBeVisible();

      // Restaurar medio de pantalla
      await page.emulateMedia({ media: 'screen' });
    });
  }

  test('(c) Formato @page — margin 5mm y size 80mm auto se preservan', async ({ page }) => {
    const pos = new POSNormalPage(page);
    await pos.goto();
    await pos.expectLoaded();

    // La regla @page vive en print.css (importado por la app); basta con la app cargada.
    const regla = await leerReglaAtPage(page);

    // PRESERVACIÓN 3.3: el formato de impresora térmica se mantiene intacto.
    // Los valores pueden normalizarse por el navegador (p. ej. margin shorthand),
    // por lo que verificamos que contengan los valores esperados.
    expect(regla).not.toBeNull();
    expect(regla!.margin).toContain('5mm');
    expect(regla!.size).toContain('80mm');
    expect(regla!.size).toContain('auto');
  });
});
