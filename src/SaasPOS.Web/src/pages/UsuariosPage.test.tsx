import { render, screen, waitFor, cleanup, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { UsuariosPage } from './UsuariosPage'
import type { PendingRecoveryDto } from '../types/recuperacion'

// Mock de AuthContext: usuario con rol que puede ver la bandeja (Dueño/Gerente).
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    login: vi.fn(),
    logout: vi.fn(),
    isAuthenticated: true,
    token: null,
    claims: null,
    comercioNombre: 'Comercio Demo',
    // La bandeja de recuperación es visible para Dueño/Gerente (Req 12.1, 12.2).
    hasRole: (roles: string | string[]) => {
      const lista = Array.isArray(roles) ? roles : [roles]
      return lista.includes('Dueño') || lista.includes('Gerente')
    },
  }),
}))

// Mock del cliente API.
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

import { api } from '../lib/api'
const mockApiGet = vi.mocked(api.get)
const mockApiPost = vi.mocked(api.post)

/**
 * Construye una solicitud de recuperación de ejemplo.
 */
function crearSolicitud(overrides: Partial<PendingRecoveryDto> = {}): PendingRecoveryDto {
  return {
    requestId: 1,
    usuarioId: 10,
    nombre: 'Cajero Uno',
    email: 'cajero1@demo.com',
    rol: 'Cajero',
    estado: 'Pendiente',
    fechaSolicitud: '2026-01-01T10:00:00Z',
    fechaExpiracion: null,
    minutosRestantes: 1200,
    tienePasswordTemporalVigente: false,
    ...overrides,
  }
}

/**
 * Configura las respuestas por defecto de los GET que `UsuariosPage` dispara al
 * montar: usuarios, sucursales, límite y la bandeja de recuperación. El listado
 * de solicitudes se parametriza por prueba.
 */
function mockGets(solicitudes: PendingRecoveryDto[]) {
  mockApiGet.mockImplementation((endpoint: string) => {
    if (endpoint === '/api/tenants/usuarios') return Promise.resolve([])
    if (endpoint === '/api/tenants/sucursales') return Promise.resolve([])
    if (endpoint === '/api/tenants/usuarios/limite') {
      return Promise.resolve({ limiteUsuarios: null, usuariosActuales: 0, puedeAgregar: true })
    }
    if (endpoint === '/api/tenants/auth/password-recovery/pending') {
      return Promise.resolve(solicitudes)
    }
    return Promise.resolve([])
  })
}

/**
 * Espía el `navigator.clipboard.writeText` REAL que `@testing-library/user-event`
 * v14 instala en jsdom durante `userEvent.setup()`.
 *
 * IMPORTANTE (Req 12.7): userEvent v14 provee su propia implementación de
 * `navigator.clipboard`. Si se redefine `navigator.clipboard` con un mock manual,
 * la app (y userEvent) terminan usando objetos de clipboard distintos y el spy
 * nunca recibe la llamada. Por eso NO se reemplaza el objeto: se espía el método
 * real ya presente, garantizando que el objeto observado sea el mismo que usa la
 * aplicación en tiempo de ejecución. Debe llamarse DESPUÉS de `userEvent.setup()`.
 *
 * El `afterEach` global (src/test/setup.ts) ejecuta `vi.restoreAllMocks()`, y el
 * `afterEach` local hace `mockRestore()` explícito para no filtrar el spy entre
 * pruebas de este mismo archivo.
 */
function espiarWriteText() {
  return vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined)
}

describe('UsuariosPage — bloque de contraseña temporal (tarea 21.3)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
    // Restaurar cualquier spy sobre navigator.clipboard.writeText instalado en la
    // prueba, dejando intacto el clipboard de jsdom/userEvent para otros archivos.
    vi.restoreAllMocks()
  })

  // Req 12.6, 12.7: al confirmar la aprobación se revela un bloque monoespaciado
  // con la temporal y un botón "Copiar" que invoca navigator.clipboard.writeText.
  it('revela la temporal en bloque monoespaciado tras aprobar y la copia al portapapeles', async () => {
    const solicitud = crearSolicitud({ requestId: 7, estado: 'Pendiente' })
    mockGets([solicitud])
    // Al aprobar, el backend devuelve la temporal en texto plano (Req 12.6).
    mockApiPost.mockResolvedValueOnce({
      tempPassword: 'Abc-123@Xyz9',
      fechaExpiracion: '2026-01-02T10:00:00Z',
    })
    // Tras aprobar, fetchSolicitudes vuelve a consultar: ahora Aprobada y vigente.
    const solicitudAprobada = crearSolicitud({
      requestId: 7,
      estado: 'Aprobada',
      minutosRestantes: 1439,
      fechaExpiracion: '2026-01-02T10:00:00Z',
      tienePasswordTemporalVigente: true,
    })

    // Primero se inicializa userEvent (instala el clipboard de jsdom) y LUEGO se
    // espía el writeText real, para observar el mismo objeto que usa la app.
    const user = userEvent.setup()
    const writeText = espiarWriteText()
    render(<UsuariosPage />)

    // Esperar a que cargue la tarjeta pendiente.
    await screen.findByText('Cajero Uno')

    // Aprobar: abre el aviso previo y luego se confirma. A partir del segundo
    // fetch de solicitudes, la solicitud aparece como Aprobada con temporal.
    mockGets([solicitudAprobada])
    await user.click(screen.getByRole('button', { name: /aprobar la solicitud de/i }))
    await user.click(screen.getByRole('button', { name: /confirmar aprobación/i }))

    // Se revela el bloque monoespaciado con la temporal (Req 12.6). Se usa el
    // aria-label exacto del <code> para no coincidir también con el botón
    // "Copiar la contraseña temporal de...".
    const bloque = await screen.findByLabelText('Contraseña temporal de Cajero Uno')
    expect(bloque).toHaveTextContent('Abc-123@Xyz9')
    // Tipografía monoespaciada.
    expect(bloque).toHaveClass('font-mono')

    // Copiar invoca navigator.clipboard.writeText con la temporal (Req 12.7).
    await user.click(screen.getByRole('button', { name: /copiar la contraseña temporal de/i }))
    expect(writeText).toHaveBeenCalledWith('Abc-123@Xyz9')

    // Feedback visible "Copiado" en el botón.
    expect(await screen.findByRole('button', { name: /copiar la contraseña temporal de/i })).toHaveTextContent(
      /copiado/i,
    )
  })

  // Req 12.10: mientras la temporal aprobada no expira, se mantiene visible el
  // bloque. Tras recargar (temporal no en memoria) se ofrece revelarla mediante
  // la reconsulta al backend.
  it('mantiene el bloque para una temporal vigente y la revela reconsultando el backend', async () => {
    const solicitudAprobada = crearSolicitud({
      requestId: 9,
      nombre: 'Bodeguero Dos',
      email: 'bodega2@demo.com',
      rol: 'Bodeguero',
      estado: 'Aprobada',
      minutosRestantes: 600,
      fechaExpiracion: '2026-01-02T10:00:00Z',
      tienePasswordTemporalVigente: true,
    })
    mockGets([solicitudAprobada])
    // Reconsulta de la temporal vigente (GET temp-password).
    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === '/api/tenants/usuarios') return Promise.resolve([])
      if (endpoint === '/api/tenants/sucursales') return Promise.resolve([])
      if (endpoint === '/api/tenants/usuarios/limite') {
        return Promise.resolve({ limiteUsuarios: null, usuariosActuales: 0, puedeAgregar: true })
      }
      if (endpoint === '/api/tenants/auth/password-recovery/pending') {
        return Promise.resolve([solicitudAprobada])
      }
      if (endpoint === '/api/tenants/auth/password-recovery/9/temp-password') {
        return Promise.resolve({ tempPassword: 'Zzz-999@Kbd2', fechaExpiracion: '2026-01-02T10:00:00Z' })
      }
      return Promise.resolve([])
    })

    // userEvent.setup() antes de espiar writeText, por el mismo motivo que arriba.
    const user = userEvent.setup()
    const writeText = espiarWriteText()
    render(<UsuariosPage />)

    // La tarjeta Aprobada con temporal vigente ofrece revelar la temporal (Req 12.10).
    const tarjeta = (await screen.findByText('Bodeguero Dos')).closest('article') as HTMLElement
    const revelar = within(tarjeta).getByRole('button', { name: /mostrar la contraseña temporal de/i })
    await user.click(revelar)

    // Se revela el bloque con la temporal reconsultada. aria-label exacto del
    // <code> para no colisionar con el botón "Copiar la contraseña temporal de...".
    const bloque = await within(tarjeta).findByLabelText('Contraseña temporal de Bodeguero Dos')
    expect(bloque).toHaveTextContent('Zzz-999@Kbd2')

    // Copiar sigue funcionando tras revelar por reconsulta (Req 12.7).
    await user.click(within(tarjeta).getByRole('button', { name: /copiar la contraseña temporal de/i }))
    expect(writeText).toHaveBeenCalledWith('Zzz-999@Kbd2')
  })

  // Una solicitud Aprobada cuya temporal ya no está vigente NO muestra el bloque
  // (coherente con Req 12.10 / leyenda de expiración).
  it('no muestra el bloque de temporal si la solicitud aprobada ya no está vigente', async () => {
    const solicitudExpirada = crearSolicitud({
      requestId: 11,
      nombre: 'Cajero Tres',
      estado: 'Aprobada',
      minutosRestantes: 0,
      fechaExpiracion: '2026-01-01T10:00:00Z',
      tienePasswordTemporalVigente: false,
    })
    mockGets([solicitudExpirada])

    render(<UsuariosPage />)

    await screen.findByText('Cajero Tres')
    await waitFor(() => {
      expect(
        screen.queryByRole('button', { name: /mostrar la contraseña temporal de/i }),
      ).not.toBeInTheDocument()
    })
  })
})
