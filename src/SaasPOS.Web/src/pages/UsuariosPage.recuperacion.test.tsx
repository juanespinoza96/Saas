import { render, screen, waitFor, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { UsuariosPage } from './UsuariosPage'
import type { PendingRecoveryDto } from '../types/recuperacion'

// Mock del contexto de autenticación: por defecto un aprobador (Dueño) que ve
// la bandeja de recuperación. Cada test puede ajustar `hasRole`.
const mockHasRole = vi.fn((roles: string[]) => roles.includes('Dueño'))
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    hasRole: mockHasRole,
    isAuthenticated: true,
    token: 'fake',
    claims: null,
    login: vi.fn(),
    logout: vi.fn(),
    comercioNombre: 'Test',
  }),
}))

// Mock del cliente API (GET/POST/PUT/PATCH usados por la página).
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
  },
}))

import { api } from '../lib/api'
const mockGet = vi.mocked(api.get)
const mockPost = vi.mocked(api.post)

/** Solicitud pendiente de ejemplo para la bandeja. */
const solicitudPendiente: PendingRecoveryDto = {
  requestId: 7,
  usuarioId: 42,
  nombre: 'Ana Cajera',
  email: 'ana@comercio.com',
  rol: 'Cajero',
  estado: 'Pendiente',
  fechaSolicitud: '2026-01-01T10:00:00Z',
  fechaExpiracion: null,
  minutosRestantes: 1380,
  tienePasswordTemporalVigente: false,
}

/**
 * Configura las respuestas de los GET de carga de la página. La bandeja de
 * recuperación consume el endpoint `password-recovery/pending`.
 */
function setupGets(solicitudes: PendingRecoveryDto[]) {
  mockGet.mockImplementation((endpoint: string) => {
    if (endpoint.includes('password-recovery/pending')) {
      return Promise.resolve(solicitudes)
    }
    if (endpoint.includes('/usuarios/limite')) {
      return Promise.resolve({ limiteUsuarios: null, usuariosActuales: 0, puedeAgregar: true })
    }
    // usuarios y sucursales
    return Promise.resolve([])
  })
}

function renderPage() {
  return render(
    <MemoryRouter>
      <UsuariosPage />
    </MemoryRouter>,
  )
}

describe('UsuariosPage — acciones de recuperación (tarea 21.2)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockHasRole.mockImplementation((roles: string[]) => roles.includes('Dueño'))
  })

  afterEach(() => {
    cleanup()
  })

  // Req 12.4, 12.5: el botón Aprobar muestra el aviso previo de cierre de sesión
  // y solo invoca el endpoint tras la confirmación explícita del aprobador.
  it('muestra el aviso de cierre de sesión al aprobar y solo llama al endpoint tras confirmar', async () => {
    const user = userEvent.setup()
    setupGets([solicitudPendiente])
    mockPost.mockResolvedValueOnce({
      tempPassword: 'Temp-123@abcd',
      fechaExpiracion: '2026-01-02T10:00:00Z',
    })

    renderPage()

    // Esperar a que la tarjeta de la solicitud aparezca.
    const aprobar = await screen.findByRole('button', { name: /aprobar la solicitud de ana/i })
    await user.click(aprobar)

    // El aviso previo describe el cierre de sesión inmediato (Req 12.5).
    expect(
      await screen.findByText(/su sesión se cerrará de inmediato|sesión activa/i),
    ).toBeInTheDocument()

    // Aún no se ha invocado el endpoint (solo se mostró el aviso).
    expect(mockPost).not.toHaveBeenCalled()

    // Confirmar la aprobación.
    await user.click(screen.getByRole('button', { name: /confirmar aprobación/i }))

    await waitFor(() => {
      expect(mockPost).toHaveBeenCalledWith(
        '/api/tenants/auth/password-recovery/approve/7',
      )
    })
  })

  // Req 12.8, 12.9: el botón de confirmar rechazo está deshabilitado mientras el
  // motivo esté vacío o solo tenga espacios, y se habilita con contenido real.
  it('deshabilita "Confirmar rechazo" si el motivo está vacío o solo tiene espacios', async () => {
    const user = userEvent.setup()
    setupGets([solicitudPendiente])

    renderPage()

    const rechazar = await screen.findByRole('button', { name: /rechazar la solicitud de ana/i })
    await user.click(rechazar)

    // El modal de rechazo abre con el motivo vacío → botón deshabilitado.
    const confirmar = await screen.findByRole('button', { name: /confirmar rechazo/i })
    expect(confirmar).toBeDisabled()

    const textarea = screen.getByLabelText(/motivo del rechazo/i)

    // Solo espacios → sigue deshabilitado.
    await user.type(textarea, '   ')
    expect(confirmar).toBeDisabled()

    // Motivo real → habilitado.
    await user.clear(textarea)
    await user.type(textarea, 'Solicitud no verificada')
    expect(confirmar).toBeEnabled()

    // No debe haberse llamado al endpoint mientras no se confirme.
    expect(mockPost).not.toHaveBeenCalled()
  })

  // Req 12.8, 12.9: al confirmar con un motivo válido se invoca el endpoint de
  // rechazo con el motivo en el body.
  it('envía el motivo al endpoint de rechazo al confirmar con un motivo válido', async () => {
    const user = userEvent.setup()
    setupGets([solicitudPendiente])
    mockPost.mockResolvedValueOnce(undefined)

    renderPage()

    await user.click(await screen.findByRole('button', { name: /rechazar la solicitud de ana/i }))
    await user.type(screen.getByLabelText(/motivo del rechazo/i), '  No autorizada  ')
    await user.click(screen.getByRole('button', { name: /confirmar rechazo/i }))

    await waitFor(() => {
      expect(mockPost).toHaveBeenCalledWith(
        '/api/tenants/auth/password-recovery/reject/7',
        { motivoRechazo: 'No autorizada' },
      )
    })
  })

  // Req 12.1, 12.2: la bandeja (y por tanto sus acciones) se oculta a roles no
  // autorizados; sin la sección no hay botones de aprobar/rechazar.
  it('no muestra acciones de aprobación para roles no autorizados', async () => {
    mockHasRole.mockImplementation(() => false)
    setupGets([solicitudPendiente])

    renderPage()

    // La sección de solicitudes no se renderiza para roles sin permiso.
    await waitFor(() => {
      expect(screen.queryByText(/solicitudes de recuperación/i)).not.toBeInTheDocument()
    })
    expect(screen.queryByRole('button', { name: /aprobar la solicitud/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /rechazar la solicitud/i })).not.toBeInTheDocument()
  })
})
