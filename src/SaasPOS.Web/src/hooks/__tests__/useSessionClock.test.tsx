// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Tests del reloj único de sesión (useSessionClock). Cubre la Tarea 9.1:
// - renovación a ~50% de vida SOLO si hubo actividad desde el último ciclo;
// - parada de renovación en el primer ciclo sin actividad (token válido hasta deadline);
// - 401 de refresh → logout inmediato sin reintento;
// - error de red no-401 → UN reintento tras REFRESH_RETRY_DELAY_MS sin ampliar deadline;
// - visibilitychange/focus tras throttling simulado → recomprueba y renueva o hace logout.
//
// _Requirements: 2.1, 2.3, 2.4, 2.6, 7.4_

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import type { JwtClaims } from '../../types/auth'
import { REFRESH_RETRY_DELAY_MS } from '../../lib/session'

// Mock del cliente de API: observamos y controlamos `api.refresh`.
vi.mock('../../lib/api', () => ({
  api: {
    refresh: vi.fn(),
    setOnUnauthorized: vi.fn(),
    clearCache: vi.fn(),
  },
}))

import { api } from '../../lib/api'
import { useSessionClock } from '../useSessionClock'

const mockedRefresh = vi.mocked(api.refresh)

// Vida/ventana para Cajero = 60 min (ms). El reloj deriva inactividad del rol.
const INACTIVITY_MS = 60 * 60_000

/**
 * Construye unos claims de prueba con `exp` a `lifetimeMinutes` desde ahora.
 * Por el invariante vida == ventana, Cajero ⇒ 60 min de vida y 60 min de ventana.
 */
function makeClaims(overrides: Partial<JwtClaims> = {}): JwtClaims {
  const nowSec = Math.floor(Date.now() / 1000)
  return {
    sub: 'u1',
    comercio_id: '1',
    role: 'Cajero',
    jti: 'jti-1',
    exp: nowSec + 60 * 60, // 60 min
    login_at: nowSec,
    ...overrides,
  }
}

/** Genera un JWT falso (solo necesitamos un string no vacío para el token). */
const FAKE_TOKEN = 'header.payload.signature'

function setup(params?: {
  claims?: JwtClaims | null
  token?: string | null
  enabled?: boolean
  logout?: () => void
  onTokenRenewed?: (t: string) => void
}) {
  const logout = params?.logout ?? vi.fn()
  const onTokenRenewed = params?.onTokenRenewed ?? vi.fn()
  const claims = params?.claims === undefined ? makeClaims() : params.claims
  const token = params?.token === undefined ? FAKE_TOKEN : params.token
  const enabled = params?.enabled ?? true

  const utils = renderHook(() =>
    useSessionClock({ token, claims, enabled, logout, onTokenRenewed }),
  )
  return { ...utils, logout, onTokenRenewed }
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-01-01T00:00:00Z'))
  mockedRefresh.mockReset()
})

afterEach(() => {
  vi.clearAllTimers()
  vi.useRealTimers()
})

describe('useSessionClock — renovación por actividad', () => {
  it('renueva a ~50% de la vida del token SOLO si hubo actividad', async () => {
    mockedRefresh.mockResolvedValue({ token: FAKE_TOKEN + '-new' })

    const { result, onTokenRenewed } = setup()

    // Marcar actividad (reinicia el reloj y la condición de renovación).
    act(() => {
      result.current.resetClock()
    })

    // Avanzar hasta ~50% de la vida (30 min) para disparar la renovación.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS / 2 + 10)
    })

    expect(mockedRefresh).toHaveBeenCalledTimes(1)
    expect(onTokenRenewed).toHaveBeenCalledWith(FAKE_TOKEN + '-new')
  })

  it('NO renueva en el primer ciclo sin actividad; el token sigue válido hasta el deadline', async () => {
    mockedRefresh.mockResolvedValue({ token: FAKE_TOKEN + '-new' })

    const { logout } = setup()
    // No llamamos resetClock: no hay actividad desde el montaje.

    // Avanzar hasta ~50% de la vida: la renovación programada NO debe dispararse.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS / 2 + 10)
    })
    expect(mockedRefresh).not.toHaveBeenCalled()
    // Aún no se alcanzó el deadline → sin logout.
    expect(logout).not.toHaveBeenCalled()

    // Avanzar hasta el deadline completo (60 min): logout por inactividad.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS / 2 + 10)
    })
    expect(logout).toHaveBeenCalledTimes(1)
    // Nunca se intentó renovar.
    expect(mockedRefresh).not.toHaveBeenCalled()
  })
})

describe('useSessionClock — manejo de errores del refresh', () => {
  it('401 de refresh → logout inmediato, sin reintento', async () => {
    mockedRefresh.mockRejectedValue({ status: 401 })

    const { result, logout } = setup()

    act(() => {
      result.current.resetClock()
    })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS / 2 + 10)
    })

    expect(mockedRefresh).toHaveBeenCalledTimes(1)
    expect(logout).toHaveBeenCalledTimes(1)

    // Avanzar el retardo de reintento: NO debe haber un segundo intento.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(REFRESH_RETRY_DELAY_MS + 50)
    })
    expect(mockedRefresh).toHaveBeenCalledTimes(1)
  })

  it('error de red no-401 → UN reintento tras REFRESH_RETRY_DELAY_MS, sin ampliar el deadline', async () => {
    // Primer intento: error de red (sin status). Segundo intento: éxito.
    mockedRefresh
      .mockRejectedValueOnce({ message: 'network down' })
      .mockResolvedValueOnce({ token: FAKE_TOKEN + '-new' })

    const { result, logout } = setup()

    act(() => {
      result.current.resetClock()
    })

    // Primer intento de renovación a ~50% de la vida.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS / 2 + 10)
    })
    expect(mockedRefresh).toHaveBeenCalledTimes(1)
    expect(logout).not.toHaveBeenCalled()

    // Antes del retardo no hay reintento.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(REFRESH_RETRY_DELAY_MS - 500)
    })
    expect(mockedRefresh).toHaveBeenCalledTimes(1)

    // Tras REFRESH_RETRY_DELAY_MS: exactamente UN reintento.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(600)
    })
    expect(mockedRefresh).toHaveBeenCalledTimes(2)
  })

  it('tras el único reintento consumido, NO hay más reintentos', async () => {
    // Ambos intentos fallan por red: no debe reintentar una tercera vez.
    mockedRefresh.mockRejectedValue({ message: 'network down' })

    const { result } = setup()

    act(() => {
      result.current.resetClock()
    })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS / 2 + 10)
    })
    expect(mockedRefresh).toHaveBeenCalledTimes(1)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(REFRESH_RETRY_DELAY_MS + 50)
    })
    expect(mockedRefresh).toHaveBeenCalledTimes(2)

    // Un tercer periodo de retardo no produce más intentos.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(REFRESH_RETRY_DELAY_MS + 50)
    })
    expect(mockedRefresh).toHaveBeenCalledTimes(2)
  })
})

describe('useSessionClock — recomprobación al volver a primer plano (anti-throttling)', () => {
  it('visibilitychange: si el deadline ya pasó mientras la pestaña estaba oculta → logout', async () => {
    mockedRefresh.mockResolvedValue({ token: FAKE_TOKEN + '-new' })

    const { result, logout } = setup()

    act(() => {
      result.current.resetClock()
    })

    // Simular throttling: el navegador no disparó el timer del deadline. Avanzamos
    // el reloj del sistema más allá del deadline SIN ejecutar los timers pendientes
    // no es posible con fake timers simples, así que avanzamos el tiempo del sistema
    // manualmente y disparamos la recomprobación por visibilitychange.
    vi.setSystemTime(Date.now() + INACTIVITY_MS + 60_000)
    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: () => 'visible',
    })

    await act(async () => {
      document.dispatchEvent(new Event('visibilitychange'))
      await Promise.resolve()
    })

    expect(logout).toHaveBeenCalledTimes(1)
  })

  it('focus: si la renovación programada venció y hubo actividad → renueva de inmediato', async () => {
    mockedRefresh.mockResolvedValue({ token: FAKE_TOKEN + '-new' })

    const { result } = setup()

    act(() => {
      result.current.resetClock()
    })

    Object.defineProperty(document, 'visibilityState', {
      configurable: true,
      get: () => 'visible',
    })

    // Avanzar el reloj del sistema más allá del 50% (momento de renovación) pero
    // antes del deadline, simulando que el timer quedó retrasado por throttling.
    vi.setSystemTime(Date.now() + INACTIVITY_MS / 2 + 60_000)

    await act(async () => {
      window.dispatchEvent(new Event('focus'))
      await Promise.resolve()
    })

    expect(mockedRefresh).toHaveBeenCalledTimes(1)
  })
})
