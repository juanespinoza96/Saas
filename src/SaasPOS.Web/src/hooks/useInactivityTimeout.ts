import { useEffect, useRef } from 'react'

// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Eventos de actividad humana que cuentan como "Actividad" del usuario. Misma
// definición que la del reloj único (no diverge): mousedown, mousemove, keydown,
// scroll, touchstart y click.
const ACTIVITY_EVENTS: (keyof DocumentEventMap)[] = [
  'mousedown',
  'mousemove',
  'keydown',
  'scroll',
  'touchstart',
  'click',
]

/**
 * Detector de actividad del usuario (Req 2.5, 7.2, 7.3).
 *
 * ADAPTACIÓN (reloj único): este hook YA NO mantiene un temporizador propio que
 * cierre la sesión a los 30 min. Ese temporizador competía con el reloj de
 * sesión y provocaba la incoherencia 30/60 (el 30 del front vs el 60 del back).
 * Ahora su ÚNICA responsabilidad es detectar actividad humana y, en cada evento,
 * invocar `onActivity`, que el `AuthContext` conecta a `useSessionClock.resetClock`
 * para REINICIAR el reloj único (reinicia `ultimaActividad`/`deadline`). No hay
 * ningún `setTimeout` paralelo: la caducidad de la sesión la gobierna en exclusiva
 * el reloj único (Req 7.1, 7.2).
 *
 * @param onActivity - Callback invocado en cada evento de actividad (reinicia el reloj único).
 * @param enabled - Si el detector está activo (desactívalo para usuarios no autenticados).
 */
export function useInactivityTimeout(
  onActivity: () => void,
  enabled: boolean = true,
): void {
  // Ref para mantener el callback actual sin re-ejecutar el efecto de suscripción.
  const onActivityRef = useRef(onActivity)
  onActivityRef.current = onActivity

  useEffect(() => {
    if (!enabled) {
      return
    }

    // En cada evento de actividad: reiniciar el reloj único.
    const handleActivity = () => onActivityRef.current()

    for (const event of ACTIVITY_EVENTS) {
      document.addEventListener(event, handleActivity, { passive: true })
    }

    return () => {
      for (const event of ACTIVITY_EVENTS) {
        document.removeEventListener(event, handleActivity)
      }
    }
  }, [enabled])
}
