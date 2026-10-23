import { useCallback, useEffect, useRef, useState } from 'react'
import type { JwtClaims } from '../types/auth'
import {
  InactivityMinutesByRole,
  REFRESH_RETRY_DELAY_MS,
  computeWarningThresholds,
  type WarningThresholds,
} from '../lib/session'
import { api } from '../lib/api'

// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Reloj único de sesión (useSessionClock): ÚNICA FUENTE DE VERDAD del lado
// cliente (Req 7.1, 7.2). Gobierna a la vez la renovación silenciosa del access
// token y el disparo de las tres etapas de aviso de expiración. No existe ningún
// temporizador paralelo que compita con este reloj: `useInactivityTimeout` se
// limita a detectar actividad y a reiniciar este reloj (ver su callback).

const AUTH_TOKEN_KEY = 'saas-pos-token'

/**
 * Etapa de aviso derivada del tiempo restante hasta el `deadline`.
 * - `none`: sin aviso (operación normal).
 * - `early`: aviso temprano no intrusivo (banner/toast).
 * - `modal`: modal de confirmación con botón "Seguir conectado".
 * - `block`: bloqueo total a pantalla completa antes del cierre.
 *
 * La tarea 6.4 consumirá esta etapa para renderizar cada aviso de forma
 * secuencial y no solapada.
 */
export type WarningStage = 'none' | 'early' | 'modal' | 'block'

/**
 * Parámetros del reloj único. El hook NO se cablea aquí; queda listo para que
 * la tarea 6.4 lo conecte en `AuthContext` junto con los componentes de aviso.
 */
export interface UseSessionClockParams {
  /** Token JWT vigente (o null si no hay sesión). */
  token: string | null
  /** Claims decodificados del token vigente (o null). */
  claims: JwtClaims | null
  /** Si el reloj debe estar activo (false para usuarios no autenticados). */
  enabled: boolean
  /** Cierra la sesión (logout del AuthContext). */
  logout: () => void
  /**
   * Notifica al AuthContext que el token fue renovado silenciosamente, para que
   * actualice su estado (token + claims) a partir del nuevo JWT ya persistido en
   * localStorage. La tarea 6.4 pasará aquí el `login(token)` del contexto.
   */
  onTokenRenewed: (token: string) => void
}

/**
 * Valor devuelto por el reloj único, consumible por la tarea 6.4.
 */
export interface SessionClockState {
  /** Etapa de aviso actual derivada del tiempo restante. */
  stage: WarningStage
  /** Milisegundos restantes hasta el `deadline` (0 si ya venció). */
  remainingMs: number
  /** Umbrales de aviso calculados para la ventana del rol actual. */
  thresholds: WarningThresholds
  /**
   * Reinicia el reloj: `ultimaActividad = now`, recalcula el `deadline`, descarta
   * avisos y reprograma el ciclo completo (Req 7.3). Lo invoca el detector de
   * actividad (`useInactivityTimeout`) y el botón "Seguir conectado".
   */
  resetClock: () => void
  /**
   * Fuerza una renovación inmediata del token (p. ej. desde el botón "Seguir
   * conectado"). Reinicia el reloj al recibir el token nuevo.
   */
  renewNow: () => void
}

/**
 * Deriva la ventana de inactividad (ms) a partir del `role` del token, usando el
 * espejo `InactivityMinutesByRole`. Para un rol no listado aplica el fallback de
 * 60 min, coherente con `InactivityMinutesDefault` del backend.
 */
function resolveInactivityMs(claims: JwtClaims | null): number {
  if (!claims) return 60 * 60_000
  const minutes = InactivityMinutesByRole[claims.role] ?? 60
  return minutes * 60_000
}

/**
 * Deriva la vida del token vigente como `exp - now` (en ms) en el instante de la
 * llamada. El POS NO usa `iat`: la vida se mide siempre contra `exp` (Decisión 4
 * §Entradas del reloj). Devuelve 0 si el token ya caducó o no hay claims.
 */
function resolveAccessLifetimeMs(claims: JwtClaims | null): number {
  if (!claims) return 0
  return Math.max(0, claims.exp * 1000 - Date.now())
}

/**
 * Margen de guardia (ms) para la recomprobación al volver a primer plano: si la
 * renovación programada ya venció o vencerá dentro de este margen, se renueva de
 * inmediato (anti-throttling de timers en pestañas en segundo plano).
 */
const VISIBILITY_GUARD_MS = 5_000

export function useSessionClock(params: UseSessionClockParams): SessionClockState {
  const { token, claims, enabled, logout, onTokenRenewed } = params

  // Refs de callbacks para no re-ejecutar efectos cuando cambian de identidad.
  const logoutRef = useRef(logout)
  const onTokenRenewedRef = useRef(onTokenRenewed)
  logoutRef.current = logout
  onTokenRenewedRef.current = onTokenRenewed

  // Instante del `deadline` de inactividad (epoch ms). Fuente de verdad del fin
  // de sesión por inactividad.
  const deadlineRef = useRef<number>(0)
  // Flag: ¿hubo actividad desde el último ciclo de renovación? La renovación solo
  // se dispara si es true (un ausente deja de renovar en el primer ciclo).
  const huboActividadRef = useRef<boolean>(false)
  // Instante (epoch ms) en que debe dispararse la próxima renovación programada.
  const nextRenewAtRef = useRef<number>(0)
  // ¿Ya se consumió el único reintento por error de red no-401 del ciclo actual?
  const retryUsedRef = useRef<boolean>(false)

  // Temporizadores activos (renovación, deadline, reintento).
  const renewTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const deadlineTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const retryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  // Tick de UI para recomputar la etapa de aviso y el tiempo restante.
  const tickTimerRef = useRef<ReturnType<typeof setInterval> | null>(null)

  // Ventana de inactividad y umbrales derivados del rol vigente.
  const inactivityMs = resolveInactivityMs(claims)
  const thresholdsRef = useRef<WarningThresholds>(computeWarningThresholds(inactivityMs))
  thresholdsRef.current = computeWarningThresholds(inactivityMs)

  // Estado expuesto a la UI (etapa + restante). Se recalcula en cada tick.
  const [stage, setStage] = useState<WarningStage>('none')
  const [remainingMs, setRemainingMs] = useState<number>(inactivityMs)

  // --- Helpers de limpieza de temporizadores ---------------------------------

  const clearRenewTimer = useCallback(() => {
    if (renewTimerRef.current) {
      clearTimeout(renewTimerRef.current)
      renewTimerRef.current = null
    }
  }, [])

  const clearRetryTimer = useCallback(() => {
    if (retryTimerRef.current) {
      clearTimeout(retryTimerRef.current)
      retryTimerRef.current = null
    }
  }, [])

  const clearDeadlineTimer = useCallback(() => {
    if (deadlineTimerRef.current) {
      clearTimeout(deadlineTimerRef.current)
      deadlineTimerRef.current = null
    }
  }, [])

  // --- Renovación silenciosa -------------------------------------------------

  // Declaración adelantada: `doRefresh` y `scheduleRenew` se referencian entre sí.
  const scheduleRenewRef = useRef<() => void>(() => {})

  /**
   * Ejecuta la renovación silenciosa contra POST /api/tenants/auth/refresh.
   * - 401 → logout inmediato, sin reintentos (Req 2.6).
   * - error de red no-401 → UN único reintento tras REFRESH_RETRY_DELAY_MS, sin
   *   ampliar el `deadline` (la cuenta de inactividad sigue corriendo).
   * - éxito → guarda el token (saas-pos-token), notifica al AuthContext para
   *   re-decodificar claims y reprograma el ciclo.
   */
  const doRefresh = useCallback(async () => {
    try {
      const { token: nuevoToken } = await api.refresh()

      // Persistir el token nuevo bajo la clave del POS.
      try {
        localStorage.setItem(AUTH_TOKEN_KEY, nuevoToken)
      } catch {
        // localStorage no disponible: el AuthContext igualmente recibe el token.
      }

      // Tras renovar: se consume la actividad acumulada y se habilita de nuevo el
      // reintento para el próximo ciclo.
      huboActividadRef.current = false
      retryUsedRef.current = false

      // Notificar al contexto para que re-decodifique claims y reprograme. El
      // cambio de `token`/`claims` reinicia el efecto principal (reprogramación).
      onTokenRenewedRef.current(nuevoToken)
    } catch (err) {
      const status = (err as { status?: number } | null)?.status

      // 401: sesión terminada (tope absoluto, revocación o caducidad). El
      // interceptor de `api` ya pudo disparar onUnauthorized; forzamos logout
      // explícito e inmediato, sin reintentos (Req 2.6).
      if (status === 401) {
        logoutRef.current()
        return
      }

      // Error de red no-401: UN solo reintento tras REFRESH_RETRY_DELAY_MS, sin
      // tocar el `deadline` (no concede tiempo extra de sesión).
      if (!retryUsedRef.current) {
        retryUsedRef.current = true
        clearRetryTimer()
        retryTimerRef.current = setTimeout(() => {
          // Reintentar solo si sigue habiendo sesión y no se agotó el deadline.
          if (Date.now() < deadlineRef.current) {
            void doRefresh()
          }
        }, REFRESH_RETRY_DELAY_MS)
      }
      // Si el reintento ya se usó: no hay más reintentos. La cuenta de inactividad
      // decidirá (logout al alcanzar el deadline).
    }
  }, [clearRetryTimer])

  /**
   * Programa la próxima renovación a ~50% de la vida del token vigente. La
   * renovación solo se disparará si `huboActividad` desde el último ciclo; de lo
   * contrario el token caduca por sí solo al llegar al `deadline`.
   */
  const scheduleRenew = useCallback(() => {
    clearRenewTimer()

    const lifetimeMs = resolveAccessLifetimeMs(claims)
    if (lifetimeMs <= 0) return

    // ~50% de la vida del token. Nunca negativo.
    const renewInMs = Math.max(0, Math.floor(lifetimeMs * 0.5))
    nextRenewAtRef.current = Date.now() + renewInMs

    renewTimerRef.current = setTimeout(() => {
      // Condición de parada única: renovar SOLO si hubo actividad desde el último
      // ciclo (un ausente deja de renovar en el primer ciclo sin actividad).
      if (huboActividadRef.current && Date.now() < deadlineRef.current) {
        void doRefresh()
      }
      // Si no hubo actividad, no se renueva: el token caduca al llegar al deadline.
    }, renewInMs)
  }, [claims, clearRenewTimer, doRefresh])

  scheduleRenewRef.current = scheduleRenew

  // --- Reinicio del reloj (actividad / "Seguir conectado") -------------------

  /**
   * Reinicia el reloj: fija `deadline = now + inactivityMs`, marca actividad,
   * reprograma la renovación y el disparo del `deadline`, y descarta avisos.
   */
  const resetClock = useCallback(() => {
    if (!enabled || !claims) return

    const now = Date.now()
    deadlineRef.current = now + inactivityMs
    huboActividadRef.current = true

    // Reprogramar fin por inactividad.
    clearDeadlineTimer()
    deadlineTimerRef.current = setTimeout(() => {
      // Al alcanzar el deadline sin actividad renovadora: logout por inactividad.
      logoutRef.current()
    }, inactivityMs)

    // Reprogramar la renovación silenciosa.
    scheduleRenewRef.current()

    // Descartar avisos: volvemos a operación normal.
    setStage('none')
    setRemainingMs(inactivityMs)
  }, [enabled, claims, inactivityMs, clearDeadlineTimer])

  /**
   * Renovación inmediata a demanda (botón "Seguir conectado"). Marca actividad y
   * fuerza el refresh; el `deadline` se reinicia al recibir el token nuevo.
   */
  const renewNow = useCallback(() => {
    if (!enabled || !claims) return
    huboActividadRef.current = true
    void doRefresh()
  }, [enabled, claims, doRefresh])

  // --- Recomprobación al volver a primer plano (anti-throttling) -------------

  const recheckOnVisible = useCallback(() => {
    if (!enabled || !claims) return
    if (document.visibilityState === 'hidden') return

    const now = Date.now()

    // Si el deadline ya pasó mientras la pestaña estaba en segundo plano: logout.
    if (now >= deadlineRef.current) {
      logoutRef.current()
      return
    }

    // Si la renovación programada ya venció (o vencerá dentro del margen de
    // guardia) y hubo actividad: renovar de inmediato (el timer pudo quedar
    // retrasado por throttling del navegador).
    if (huboActividadRef.current && now >= nextRenewAtRef.current - VISIBILITY_GUARD_MS) {
      void doRefresh()
    }
  }, [enabled, claims, doRefresh])

  // --- Efecto principal: arranque/reprogramación del ciclo -------------------

  useEffect(() => {
    if (!enabled || !claims || !token) {
      // Sin sesión: limpiar todo y resetear estado visible.
      clearRenewTimer()
      clearRetryTimer()
      clearDeadlineTimer()
      if (tickTimerRef.current) {
        clearInterval(tickTimerRef.current)
        tickTimerRef.current = null
      }
      setStage('none')
      return
    }

    // Arranque / reprogramación: fija deadline, renovación y fin por inactividad.
    const now = Date.now()
    deadlineRef.current = now + inactivityMs

    clearDeadlineTimer()
    deadlineTimerRef.current = setTimeout(() => {
      logoutRef.current()
    }, inactivityMs)

    scheduleRenewRef.current()

    // Tick de UI: recomputa etapa y tiempo restante ~cada segundo.
    if (tickTimerRef.current) {
      clearInterval(tickTimerRef.current)
    }
    tickTimerRef.current = setInterval(() => {
      const restante = Math.max(0, deadlineRef.current - Date.now())
      setRemainingMs(restante)

      const { early, modal, block } = thresholdsRef.current
      let nuevaEtapa: WarningStage = 'none'
      if (restante <= block) nuevaEtapa = 'block'
      else if (restante <= modal) nuevaEtapa = 'modal'
      else if (restante <= early) nuevaEtapa = 'early'
      setStage(nuevaEtapa)
    }, 1_000)

    // Suscripción anti-throttling: recomprobar/renovar al volver a primer plano.
    document.addEventListener('visibilitychange', recheckOnVisible)
    window.addEventListener('focus', recheckOnVisible)

    return () => {
      clearRenewTimer()
      clearRetryTimer()
      clearDeadlineTimer()
      if (tickTimerRef.current) {
        clearInterval(tickTimerRef.current)
        tickTimerRef.current = null
      }
      document.removeEventListener('visibilitychange', recheckOnVisible)
      window.removeEventListener('focus', recheckOnVisible)
    }
    // El efecto se reprograma cuando cambian el token/claims (p. ej. tras renovar)
    // o la ventana de inactividad (cambio de rol).
  }, [
    enabled,
    token,
    claims,
    inactivityMs,
    clearRenewTimer,
    clearRetryTimer,
    clearDeadlineTimer,
    recheckOnVisible,
  ])

  return {
    stage,
    remainingMs,
    thresholds: thresholdsRef.current,
    resetClock,
    renewNow,
  }
}
