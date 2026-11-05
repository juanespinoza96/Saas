import { test, expect, type Page } from '@playwright/test';

// Feature: sesion-inactividad-y-expiracion-por-rol — Tarea 9.2
//
// E2E del POS (Playwright) del flujo de los 3 avisos de expiración de sesión y
// la renovación. Verifica, con un RELOJ SIMULADO (`page.clock`), la secuencia
// Aviso_Temprano → Modal_Confirmacion → Bloqueo_Total, que la actividad y el
// botón "Seguir conectado" renuevan y descartan los avisos, y que el
// Bloqueo_Total a 0 cierra la sesión y redirige a `/login`.
//
// Requisitos cubiertos: 5.1, 5.3, 5.5, 5.6, 5.7, 5.8, 7.3.
//
// SESIÓN REUTILIZADA (no login por test): el spec reutiliza el estado de
// autenticación `.auth/gerente.json` que genera el proyecto `setup` UNA sola vez
// (ver `e2e/fixtures/auth.setup.ts` y el default `storageState` del proyecto
// `chromium` en `playwright.config.ts`). Igual que el resto de la suite
// (dashboard, clients, rbac, bar-escolar…), NO se hace login dentro del archivo:
// loguear la misma cuenta Gerente en cada test dispara la protección de login del
// backend (rate limit / lockout) y cuelga los últimos tests en la pantalla de
// "Cuenta bloqueada temporalmente".
//
// Nota de diseño sobre los tiempos (NO se inventan): los umbrales se derivan del
// reloj único real (`useSessionClock` + `computeWarningThresholds`) a partir del
// rol del token. Para el rol Gerente la ventana de inactividad es de 30 min, por
// lo que `computeWarningThresholds` produce early = 5 min, modal = 60 s,
// block = 10 s (valores fijos para ventanas ≥ 15 min). El `deadline` del reloj
// único NO se deriva del `exp` del token, sino de `Date.now()` en el instante en
// que el efecto de `useSessionClock` arranca al montar el Dashboard con la sesión
// restaurada (`deadline = montaje + 30 min` para el rol Gerente). Como sin
// actividad NO se dispara la renovación programada, el reloj corre hasta ese
// `deadline` y podemos adelantar el reloj simulado hasta cada banda de aviso.

// Ventana de inactividad del rol Gerente (espejo de appsettings/lib/session.ts).
const GERENTE_WINDOW_MS = 30 * 60_000;
// Umbrales reales derivados por computeWarningThresholds(30 min).
const EARLY_MS = 5 * 60_000; // 300_000
const MODAL_MS = 60_000;
const BLOCK_MS = 10_000;

// Selectores REALES derivados de los componentes de aviso:
// - AvisoTemprano: role="status" (banner no intrusivo) con el texto de inactividad.
// - ModalConfirmacion: role="alertdialog" titulado "Tu sesión está por expirar"
//   con el botón "Seguir conectado".
// - BloqueoTotal: role="alertdialog" titulado "Sesión bloqueada por inactividad".
function avisoTemprano(page: Page) {
  return page.getByRole('status').filter({ hasText: /tu sesión expirará en/i });
}
function modalConfirmacion(page: Page) {
  return page
    .getByRole('alertdialog')
    .filter({ hasText: /tu sesión está por expirar/i });
}
function botonSeguirConectado(page: Page) {
  return page.getByRole('button', { name: /seguir conectado/i });
}
function bloqueoTotal(page: Page) {
  return page
    .getByRole('alertdialog')
    .filter({ hasText: /sesión bloqueada por inactividad/i });
}

/**
 * Navega al Dashboard REUTILIZANDO la sesión autenticada ya restaurada por el
 * `storageState` del proyecto (NO hace login) y SOLO DESPUÉS toma el control del
 * tiempo con el reloj simulado, dejando la página en el Dashboard y SIN generar
 * actividad posterior (para no reiniciar el reloj ni disparar la renovación).
 * Devuelve el instante base del reloj simulado (epoch ms) para calcular los
 * avances.
 *
 * IMPORTANTE (orden navegación → instalación del reloj): la navegación se hace
 * con el reloj REAL. Si se congela el reloj ANTES, el arranque del cliente
 * (timers/promesas) no completa el aterrizaje en `/dashboard` y la prueba
 * expira. Solo tras estar en el Dashboard tomamos control del tiempo.
 *
 * El `deadline` del reloj único NO se deriva del `exp` del token, sino de
 * `Date.now()` en el instante en que el efecto de `useSessionClock` arranca al
 * montar el Dashboard (`deadline = montaje + 30 min` para el rol Gerente). Por
 * eso instalar el reloj justo después de aterrizar en el Dashboard deja el
 * `deadline` ≈ 30 min por delante del instante `base` que devolvemos.
 */
async function irAlDashboardConRelojSimulado(page: Page): Promise<number> {
  await page.goto('/dashboard');
  await page.waitForURL('**/dashboard');

  // Ya en el Dashboard con la sesión restaurada: AHORA tomamos control del
  // tiempo. El reloj se fija en el instante real actual, de modo que el
  // `deadline` (que el efecto acaba de fijar en `now + 30 min` con el reloj real)
  // queda ≈ 30 min por delante del tiempo simulado.
  const base = Date.now();
  await page.clock.install({ time: base });

  return base;
}

test.describe('Sesión por inactividad — flujo de 3 avisos y renovación (POS)', () => {
  // Reutiliza la sesión Gerente generada UNA sola vez por el proyecto `setup`
  // (`.auth/gerente.json`). Es también el default del proyecto `chromium`, pero
  // se declara aquí explícitamente para dejar constancia de que estos tests NO
  // hacen login propio (evita disparar el lockout del backend).
  test.use({ storageState: '.auth/gerente.json' });

  // ─── Escenario 1: secuencia completa early → modal → block (Req 5.1, 5.3, 5.5, 5.8) ───
  //
  // Todos los escenarios usan `clock.pauseAt(instante)`: salta el reloj al instante
  // absoluto indicado, dispara los timers vencidos (incluido el `setInterval` de 1 s
  // de `useSessionClock` que recalcula `stage`/`remainingMs`) y DEJA EL RELOJ PAUSADO.
  // Congelar el reloj mantiene `remainingMs` estable mientras corren los `expect`,
  // evitando que un tick posterior cruce al siguiente umbral durante la aserción.
  // En los escenarios con interacción (click de actividad o "Seguir conectado"), la
  // propia interacción reinicia el ciclo (resetClock/renewNow); el siguiente `pauseAt`
  // vuelve a saltar y a disparar el tick que recalcula la etapa, por lo que no hace
  // falta un reloj corriendo libremente. (Nota: el Clock API de Playwright NO expone
  // `fastForwardTo`; sus métodos de avance son `pauseAt`, `fastForward` y `runFor`.)
  test('secuencia secuencial Aviso_Temprano → Modal_Confirmacion → Bloqueo_Total', async ({
    page,
  }) => {
    const base = await irAlDashboardConRelojSimulado(page);

    // Al inicio (Dashboard recién cargado) no hay ningún aviso visible.
    await expect(avisoTemprano(page)).toHaveCount(0);
    await expect(modalConfirmacion(page)).toHaveCount(0);
    await expect(bloqueoTotal(page)).toHaveCount(0);

    // --- Etapa early: avanzar hasta ~1 s después del umbral `early` (quedan
    // ~5 min para el deadline). Debe verse SOLO el Aviso_Temprano (Req 5.1). ---
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - EARLY_MS) + 1_000);
    await expect(avisoTemprano(page)).toBeVisible();
    // No solapamiento: el modal y el bloqueo aún no aparecen (Req 5.8).
    await expect(modalConfirmacion(page)).toHaveCount(0);
    await expect(bloqueoTotal(page)).toHaveCount(0);

    // --- Etapa modal: avanzar hasta ~1 s después del umbral `modal` (quedan
    // ~60 s). Debe verse SOLO el Modal_Confirmacion (Req 5.3). ---
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - MODAL_MS) + 1_000);
    await expect(modalConfirmacion(page)).toBeVisible();
    await expect(botonSeguirConectado(page)).toBeVisible();
    // El aviso temprano ya no está y el bloqueo aún no (Req 5.8).
    await expect(avisoTemprano(page)).toHaveCount(0);
    await expect(bloqueoTotal(page)).toHaveCount(0);

    // --- Etapa block: avanzar hasta ~1 s después del umbral `block` (quedan
    // ~10 s). Debe verse SOLO el Bloqueo_Total (Req 5.5). ---
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - BLOCK_MS) + 1_000);
    await expect(bloqueoTotal(page)).toBeVisible();
    // El modal ya no está (Req 5.8).
    await expect(modalConfirmacion(page)).toHaveCount(0);
  });

  // ─── Escenario 2: la actividad durante un aviso lo descarta y renueva (Req 7.3) ───
  test('la actividad del usuario durante el Aviso_Temprano lo descarta y reinicia el reloj', async ({
    page,
  }) => {
    const base = await irAlDashboardConRelojSimulado(page);

    // Entrar en la banda del aviso temprano (~5 min restantes).
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - EARLY_MS) + 1_000);
    await expect(avisoTemprano(page)).toBeVisible();

    // Actividad real del usuario (un click en el cuerpo). Cualquier evento de
    // actividad reinicia el reloj único vía useInactivityTimeout → resetClock,
    // que descarta los avisos y recoloca el deadline a +30 min (Req 7.3).
    await page.mouse.click(10, 10);

    // El aviso temprano debe desaparecer tras reiniciarse el reloj.
    await expect(avisoTemprano(page)).toHaveCount(0);

    // Comprobar que el ciclo realmente se reinició: avanzar una cantidad que,
    // sin reinicio, ya habría superado el deadline original (p. ej. +6 min desde
    // la banda early). Como el reloj se reinició, seguimos en operación normal
    // (sin avisos) y en el Dashboard.
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - EARLY_MS) + 6 * 60_000);
    await expect(avisoTemprano(page)).toHaveCount(0);
    await expect(modalConfirmacion(page)).toHaveCount(0);
    await expect(bloqueoTotal(page)).toHaveCount(0);
    await expect(page).toHaveURL(/\/dashboard/);
  });

  // ─── Escenario 3: "Seguir conectado" renueva, cierra el modal y restaura la operación (Req 7.3) ───
  test('el botón "Seguir conectado" del Modal renueva y restaura la operación normal', async ({
    page,
  }) => {
    const base = await irAlDashboardConRelojSimulado(page);

    // Entrar en la banda del modal (~60 s restantes).
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - MODAL_MS) + 1_000);
    await expect(modalConfirmacion(page)).toBeVisible();

    // Pulsar "Seguir conectado": fuerza una renovación inmediata (renewNow), que
    // al recibir el token nuevo reinicia el reloj y descarta el modal.
    await botonSeguirConectado(page).click();

    // El modal debe cerrarse tras la renovación.
    await expect(modalConfirmacion(page)).toHaveCount(0);
    await expect(bloqueoTotal(page)).toHaveCount(0);
    await expect(page).toHaveURL(/\/dashboard/);

    // Verificar que la operación normal se restauró: avanzar más allá del
    // deadline original (p. ej. +2 min desde la banda del modal). Con la sesión
    // renovada seguimos autenticados y sin bloqueo.
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - MODAL_MS) + 2 * 60_000);
    await expect(bloqueoTotal(page)).toHaveCount(0);
    await expect(page).toHaveURL(/\/dashboard/);
  });

  // ─── Escenario 4: Bloqueo_Total a 0 sin renovación → redirige a /login (Req 5.6) ───
  test('el Bloqueo_Total con la cuenta a 0 cierra la sesión y redirige a /login', async ({
    page,
  }) => {
    const base = await irAlDashboardConRelojSimulado(page);

    // Entrar en la banda de bloqueo (~10 s restantes) sin ninguna actividad.
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - BLOCK_MS) + 1_000);
    await expect(bloqueoTotal(page)).toBeVisible();

    // Agotar el tiempo: avanzar hasta pasado el deadline. Al llegar a 0, el reloj
    // único hace logout y el Guard_Router redirige a /login (Req 5.6).
    await page.clock.pauseAt(base + GERENTE_WINDOW_MS + 2_000);
    await page.waitForURL('**/login', { timeout: 5_000 });
    await expect(page).toHaveURL(/\/login/);
  });

  // ─── Escenario 5: el Bloqueo_Total impide interacción con el contenido detrás (Req 5.7) ───
  test('el Bloqueo_Total es un overlay opaco modal que tapa el contenido detrás', async ({
    page,
  }) => {
    const base = await irAlDashboardConRelojSimulado(page);

    // Entrar en la banda de bloqueo (~10 s restantes).
    await page.clock.pauseAt(base + (GERENTE_WINDOW_MS - BLOCK_MS) + 1_000);
    const overlay = bloqueoTotal(page);
    await expect(overlay).toBeVisible();

    // Es un diálogo modal a pantalla completa: aria-modal="true" y cubre el
    // viewport completo (fixed inset-0), de modo que intercepta la interacción
    // con cualquier elemento situado detrás (Req 5.7).
    await expect(overlay).toHaveAttribute('aria-modal', 'true');

    const caja = await overlay.boundingBox();
    const viewport = page.viewportSize();
    expect(caja).not.toBeNull();
    expect(viewport).not.toBeNull();
    if (caja && viewport) {
      // El overlay ocupa todo el viewport (tolerancia de 1px por redondeos).
      expect(caja.x).toBeLessThanOrEqual(1);
      expect(caja.y).toBeLessThanOrEqual(1);
      expect(caja.width).toBeGreaterThanOrEqual(viewport.width - 1);
      expect(caja.height).toBeGreaterThanOrEqual(viewport.height - 1);
    }

    // Playwright confirma que el overlay intercepta los eventos de puntero en el
    // centro del viewport: un click en el centro impacta el overlay, no el
    // contenido del Dashboard que queda detrás.
    const puntoCentral = {
      x: (viewport?.width ?? 0) / 2,
      y: (viewport?.height ?? 0) / 2,
    };
    const elementoEnCentro = await page.evaluate(
      ({ x, y }) => {
        const el = document.elementFromPoint(x, y);
        // Busca hacia arriba el contenedor con role="alertdialog".
        let actual: Element | null = el;
        while (actual) {
          if (actual.getAttribute('role') === 'alertdialog') return true;
          actual = actual.parentElement;
        }
        return false;
      },
      puntoCentral,
    );
    expect(elementoEnCentro).toBe(true);
  });
});
