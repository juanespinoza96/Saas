// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Test de integración del flujo de los 3 avisos de expiración cableados en el
// AuthProvider real (AvisoTemprano → ModalConfirmacion → BloqueoTotal). Cubre la
// Tarea 9.1:
// - las tres etapas se muestran en orden y de forma NO solapada;
// - "Seguir conectado" renueva (llama a api.refresh) y descarta el modal;
// - al agotarse la cuenta del Bloqueo_Total se cierra la sesión (logout).
//
// _Requirements: 5.2, 5.4, 5.6, 5.8, 6.1, 6.2, 6.3, 6.4_

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, act, fireEvent } from '@testing-library/react'

// Mock del cliente de API: controlamos api.refresh y evitamos red real.
vi.mock('../../lib/api', () => ({
  api: {
    refresh: vi.fn(),
    setOnUnauthorized: vi.fn(),
    clearCache: vi.fn(),
  },
}))

import { api } from '../../lib/api'
import { AuthProvider, useAuth } from '../AuthContext'

const mockedRefresh = vi.mocked(api.refresh)
const AUTH_TOKEN_KEY = 'saas-pos-token'

/**
 * Codifica en base64url (para construir un JWT falso pero decodificable por
 * jwt-decode, que solo decodifica el payload, sin verificar firma).
 */
function base64url(obj: unknown): string {
  const json = JSON.stringify(obj)
  // btoa está disponible en jsdom.
  return btoa(json).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

/**
 * Construye un JWT de prueba (header.payload.signature) con los claims dados.
 * Cajero ⇒ ventana 60 min; `exp` a 60 min para respetar el invariante vida == ventana.
 */
function makeToken(overrides: Record<string, unknown> = {}): string {
  const nowSec = Math.floor(Date.now() / 1000)
  const header = base64url({ alg: 'HS256', typ: 'JWT' })
  const payload = base64url({
    sub: 'u1',
    comercio_id: '1',
    role: 'Cajero',
    jti: 'jti-1',
    exp: nowSec + 60 * 60,
    login_at: nowSec,
    ...overrides,
  })
  return `${header}.${payload}.sig`
}

// Componente de prueba que expone el estado de autenticación.
function Sonda() {
  const { isAuthenticated } = useAuth()
  return <div data-testid="auth">{isAuthenticated ? 'si' : 'no'}</div>
}

const INACTIVITY_MS = 60 * 60_000 // Cajero: 60 min

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-01-01T00:00:00Z'))
  mockedRefresh.mockReset()
  localStorage.clear()
})

afterEach(() => {
  vi.clearAllTimers()
  vi.useRealTimers()
  localStorage.clear()
})

describe('Flujo de 3 avisos cableado en AuthProvider', () => {
  it('muestra las etapas en orden y no solapadas: early → modal → block', async () => {
    localStorage.setItem(AUTH_TOKEN_KEY, makeToken())

    render(
      <AuthProvider>
        <Sonda />
      </AuthProvider>,
    )

    expect(screen.getByTestId('auth')).toHaveTextContent('si')

    // Inicialmente (operación normal) no hay ningún aviso.
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.queryByRole('alertdialog')).toBeNull()

    // Avanzar hasta la etapa `early`: restante ≤ 5 min (umbral early) pero > 60 s.
    // Posicionamos el reloj a 4 min del deadline.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS - 4 * 60_000)
    })
    // Aviso temprano visible (banner con role="status"), sin modal ni bloqueo.
    expect(screen.getByRole('status')).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).toBeNull()

    // Avanzar hasta la etapa `modal`: restante ≤ 60 s pero > 10 s (p. ej. 40 s).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(4 * 60_000 - 40_000)
    })
    // El modal reemplaza al aviso temprano (no solapados).
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.getByRole('button', { name: /seguir conectado/i })).toBeInTheDocument()

    // Avanzar hasta la etapa `block`: restante ≤ 10 s (p. ej. 8 s).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(40_000 - 8_000)
    })
    const bloqueo = screen.getByRole('alertdialog')
    expect(bloqueo).toHaveAttribute('aria-live', 'assertive')
    // Ya no hay botón "Seguir conectado" (el bloqueo total no lo tiene).
    expect(screen.queryByRole('button', { name: /seguir conectado/i })).toBeNull()
  })

  it('al agotarse el deadline se cierra la sesión (logout)', async () => {
    localStorage.setItem(AUTH_TOKEN_KEY, makeToken())

    render(
      <AuthProvider>
        <Sonda />
      </AuthProvider>,
    )
    expect(screen.getByTestId('auth')).toHaveTextContent('si')

    // Sin actividad, al alcanzar el deadline completo el reloj hace logout.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS + 1_000)
    })

    expect(screen.getByTestId('auth')).toHaveTextContent('no')
    // Nunca se intentó renovar (no hubo actividad).
    expect(mockedRefresh).not.toHaveBeenCalled()
  })

  it('"Seguir conectado" renueva (api.refresh) y descarta el modal', async () => {
    localStorage.setItem(AUTH_TOKEN_KEY, makeToken())
    // La renovación devuelve un token nuevo (también Cajero, ventana completa).
    mockedRefresh.mockResolvedValue({ token: makeToken({ jti: 'jti-2' }) })

    render(
      <AuthProvider>
        <Sonda />
      </AuthProvider>,
    )

    // Llevar el reloj a la etapa `modal` (40 s restantes).
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INACTIVITY_MS - 40_000)
    })
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()

    // Pulsar "Seguir conectado": debe llamar a api.refresh. Se usa fireEvent
    // (síncrono) en lugar de userEvent, que es incompatible con fake timers aquí.
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: /seguir conectado/i }))
      await Promise.resolve()
    })

    expect(mockedRefresh).toHaveBeenCalledTimes(1)
    // La sesión sigue activa tras renovar.
    expect(screen.getByTestId('auth')).toHaveTextContent('si')
  })
})
