import type { AdminRole } from '../types/auth'

/**
 * Constantes de sesión del panel SuperAdmin.
 *
 * El Admin solo admite el rol SuperAdmin (Nivel_Estricto): ventana de
 * inactividad de 15 min y tope absoluto de 4 h (240 min). Se espeja aquí
 * exactamente el valor del backend (`JwtSettings:InactivityMinutesByRole`
 * en `appsettings.json`). La fuente de verdad es el backend: la vida del
 * access token y la ventana de inactividad se resuelven allí por rol.
 *
 * ADVERTENCIA (ESPEJO DEL BACKEND): debe mantenerse SINCRONIZADO con el
 * backend. Si cambian los minutos por rol en `appsettings.json`, hay que
 * actualizar también este valor. Idealmente, este espejo debería moverse/
 * mantenerse junto al backend (p. ej. expuesto por un endpoint de
 * configuración) para eliminar la duplicación.
 *
 * Invariante del backend: vida del access token == ventana de inactividad.
 */
export const InactivityMinutesByRole: Record<AdminRole, number> = {
  SuperAdmin: 15,
}

// Feature: sesion-inactividad-y-expiracion-por-rol

/**
 * Retardo del único reintento de refresh ante un error de RED no-401.
 *
 * Valor compartido POS/Admin: cuando la renovación silenciosa falla por un
 * error de red transitorio (no un 401), el reloj de sesión reintenta UNA sola
 * vez tras este retardo, SIN ampliar el `deadline` de inactividad (el reintento
 * no concede tiempo extra de sesión). Un 401 NO se reintenta: se trata como
 * sesión terminada.
 */
export const REFRESH_RETRY_DELAY_MS = 3000

/**
 * Umbrales de aviso (en ms restantes hasta el `deadline`) en los que disparan
 * las tres etapas de aviso de expiración de sesión.
 */
export interface WarningThresholds {
  /** Aviso temprano no intrusivo (banner/toast). */
  early: number
  /** Modal de confirmación centrado ("Seguir conectado"). */
  modal: number
  /** Bloqueo total a pantalla completa antes del cierre. */
  block: number
}

/**
 * Acota `x` al rango [lo, hi]. Helper local (no exportado).
 */
function clamp(x: number, lo: number, hi: number): number {
  return Math.min(Math.max(x, lo), hi)
}

/**
 * Calcula de forma determinista los tres umbrales de aviso a partir de la
 * ventana de inactividad `inactivityMs` (W). Función PURA (misma entrada →
 * misma salida, sin efectos secundarios). Misma lógica que el POS.
 *
 * Algoritmo del diseño (Decisión 4, "Algoritmo de escalado determinista"):
 * - early: 5 min fijos para ventanas "normales" (W ≥ 15 min); en ventanas
 *   cortas, la mitad de la ventana acotada a [30 s, 5 min].
 * - modal: 60 s, o el 15% de la ventana si esta es más corta.
 * - block: 10 s, o la mitad del umbral del modal.
 *
 * Garantía de ORDEN ESTRICTO `early > modal > block > 0` para toda W > 0: si la
 * configuración rompe el orden, se recorta proporcionalmente (modal a la mitad
 * de early, y block a la mitad de modal).
 */
export function computeWarningThresholds(inactivityMs: number): WarningThresholds {
  const W = inactivityMs

  // early: 5 min fijos para ventanas normales (≥15 min); ventanas cortas →
  // mitad de la ventana acotada a [30 s, 5 min].
  const early = W >= 15 * 60_000
    ? 5 * 60_000
    : clamp(W * 0.5, 30_000, 5 * 60_000)

  // modal: 60 s, o el 15% de la ventana si es más corta.
  let modal = Math.min(60_000, W * 0.15)

  // block: 10 s, o la mitad del umbral del modal.
  let block = Math.min(10_000, modal * 0.5)

  // Garantía de orden estricto early > modal > block > 0: recorte proporcional
  // si la configuración rompiera el orden.
  if (modal >= early) modal = early * 0.5
  if (block >= modal) block = modal * 0.5

  return { early, modal, block }
}
