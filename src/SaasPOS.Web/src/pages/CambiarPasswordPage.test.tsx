/**
 * Pruebas de la pantalla de cambio de contraseña obligatorio
 * (`CambiarPasswordPage`), tarea 23.1 del spec
 * `recuperacion-password-jerarquica`.
 *
 * Cubre:
 * - Accesibilidad (vitest-axe) de la pantalla, en estado inicial y con errores
 *   de validación mostrados (WCAG).
 * - Validación en cliente con el espejo `passwordPolicy` (Req 11.1–11.6) antes
 *   de invocar el backend, y flujo de éxito (Req 9.3, 10.1).
 *
 * _Requirements: 9.3, 10.1, 11.6_
 */
import { render, screen, waitFor, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { MemoryRouter } from 'react-router-dom'

// ─── Mock de react-router-dom (navigate) ─────────────────────────────────────
const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

// ─── Mock del AuthContext ─────────────────────────────────────────────────────
const mockClearMustChangePassword = vi.fn()
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    clearMustChangePassword: mockClearMustChangePassword,
    token: 'fake-token',
    claims: null,
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: () => true,
    comercioNombre: 'Comercio Demo',
    mustChangePassword: true,
  }),
}))

// ─── Mock de la API ───────────────────────────────────────────────────────────
vi.mock('../lib/api', () => ({
  api: {
    post: vi.fn(),
  },
}))

import { api } from '../lib/api'
import { CambiarPasswordPage } from './CambiarPasswordPage'

const mockApiPost = vi.mocked(api.post)

function renderPage() {
  return render(
    <MemoryRouter>
      <CambiarPasswordPage />
    </MemoryRouter>,
  )
}

describe('CambiarPasswordPage (Req 9.3, 10.1, 11.6)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  // ─── Accesibilidad (vitest-axe) ─────────────────────────────────────────────
  it('no tiene violaciones de accesibilidad en el estado inicial', async () => {
    const { container } = renderPage()
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('no tiene violaciones de accesibilidad con errores de validación visibles', async () => {
    const user = userEvent.setup()
    const { container } = renderPage()

    // Enviar el formulario vacío/ inválido para mostrar los mensajes de error.
    await user.type(screen.getByLabelText('Nueva contraseña'), 'abc')
    await user.click(screen.getByRole('button', { name: /guardar contraseña/i }))

    // Pueden aparecer uno o varios mensajes de error (contraseña y
    // confirmación). Solo esperamos a que aparezca al menos uno antes de
    // auditar la accesibilidad con axe.
    await screen.findAllByRole('alert')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  // ─── Validación en cliente (Req 11.6) ───────────────────────────────────────
  it('no invoca el backend cuando la nueva contraseña incumple la política', async () => {
    const user = userEvent.setup()
    renderPage()

    // Contraseña inválida (sin dígito ni especial) + confirmación coincidente.
    await user.type(screen.getByLabelText('Nueva contraseña'), 'abcdef')
    await user.type(screen.getByLabelText('Confirmar contraseña'), 'abcdef')
    await user.click(screen.getByRole('button', { name: /guardar contraseña/i }))

    // Se muestra un error de política y NO se llama a la API. Puede haber uno
    // o más alerts; basta con que exista al menos uno.
    expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0)
    expect(mockApiPost).not.toHaveBeenCalled()
  })

  it('bloquea el envío si la confirmación no coincide', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.type(screen.getByLabelText('Nueva contraseña'), 'Abc1@xyz')
    await user.type(screen.getByLabelText('Confirmar contraseña'), 'Otra1@xyz')
    await user.click(screen.getByRole('button', { name: /guardar contraseña/i }))

    expect(await screen.findByText(/no coinciden/i)).toBeInTheDocument()
    expect(mockApiPost).not.toHaveBeenCalled()
  })

  // ─── Flujo de éxito (Req 9.3, 10.1) ─────────────────────────────────────────
  it('con una contraseña válida: envía al backend, libera la navegación y redirige al dashboard', async () => {
    const user = userEvent.setup()
    mockApiPost.mockResolvedValueOnce({ message: 'ok' })
    renderPage()

    const nueva = 'Abc1@xyz'
    await user.type(screen.getByLabelText('Nueva contraseña'), nueva)
    await user.type(screen.getByLabelText('Confirmar contraseña'), nueva)
    await user.click(screen.getByRole('button', { name: /guardar contraseña/i }))

    // Envía solo la nueva contraseña (el userId sale del claim del JWT).
    await waitFor(() => {
      expect(mockApiPost).toHaveBeenCalledWith('/api/tenants/auth/change-password', {
        NewPassword: nueva,
      })
    })

    // Libera la navegación (apaga el claim) y redirige al dashboard (Req 9.3).
    expect(mockClearMustChangePassword).toHaveBeenCalledTimes(1)
    expect(mockNavigate).toHaveBeenCalledWith('/dashboard', { replace: true })
  })

  // ─── Error 400 del backend (política incumplida server-side) ────────────────
  it('muestra el mensaje del backend cuando responde 400 (política)', async () => {
    const user = userEvent.setup()
    mockApiPost.mockRejectedValueOnce({
      status: 400,
      details: { message: 'La contraseña no cumple la política.' },
    })
    renderPage()

    const nueva = 'Abc1@xyz'
    await user.type(screen.getByLabelText('Nueva contraseña'), nueva)
    await user.type(screen.getByLabelText('Confirmar contraseña'), nueva)
    await user.click(screen.getByRole('button', { name: /guardar contraseña/i }))

    expect(await screen.findByText('La contraseña no cumple la política.')).toBeInTheDocument()
    // No se libera la navegación si el backend rechaza el cambio.
    expect(mockClearMustChangePassword).not.toHaveBeenCalled()
  })
})
