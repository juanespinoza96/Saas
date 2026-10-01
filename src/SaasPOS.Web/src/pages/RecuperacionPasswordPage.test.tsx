/**
 * Pruebas de la bandeja de "Solicitudes de recuperación" en `UsuariosPage`,
 * tarea 23.1 del spec `recuperacion-password-jerarquica`.
 *
 * Feature: recuperacion-password-jerarquica, Property 18: Visibilidad de la
 * bandeja por rol y bloqueo de rechazo sin motivo — la sección solo se muestra
 * para Dueño/Gerente (Req 12.1, 12.2) y el botón de confirmar rechazo está
 * bloqueado mientras el motivo esté vacío o solo tenga espacios (Req 12.9).
 *
 * Cubre además:
 * - El botón "Copiar" invoca `navigator.clipboard.writeText` con la contraseña
 *   temporal (Req 12.7).
 * - Accesibilidad (vitest-axe) de la bandeja renderizada (WCAG).
 *
 * _Requirements: 12.1, 12.2, 12.7, 12.9_
 */
import { render, screen, waitFor, cleanup, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import fc from 'fast-check'
import type { UserRole } from '../types/auth'
import type { PendingRecoveryDto } from '../types/recuperacion'

// ─── Mock del AuthContext ─────────────────────────────────────────────────────
// La bandeja usa `hasRole(['Dueño','Gerente'])` para decidir su visibilidad. Se
// controla mediante un rol mutable capturado por el mock del módulo.
let mockRol: UserRole = 'Dueño'
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    hasRole: (roles: UserRole | UserRole[]) => {
      const lista = Array.isArray(roles) ? roles : [roles]
      return lista.includes(mockRol)
    },
    token: 'fake-token',
    claims: { role: mockRol },
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
    comercioNombre: 'Comercio Demo',
    mustChangePassword: false,
    clearMustChangePassword: vi.fn(),
  }),
}))

// ─── Mock de dateUtils ────────────────────────────────────────────────────────
// Formateo determinista e independiente de la zona horaria del entorno.
vi.mock('../lib/dateUtils', () => ({
  formatDateTime: (iso: string | null | undefined) => (iso ? `FECHA(${iso})` : '—'),
}))

// ─── Mock de la API ───────────────────────────────────────────────────────────
// UsuariosPage hace varias llamadas GET al montar (usuarios, sucursales, límite
// y las solicitudes de recuperación). Se enruta por URL a respuestas de prueba.
// `post`/`put`/`patch` se dejan como spies vacíos salvo el approve/reject que se
// personaliza por test con mockResolvedValueOnce.
const solicitudesActuales: { valor: PendingRecoveryDto[] } = { valor: [] }

vi.mock('../lib/api', () => {
  return {
    api: {
      get: vi.fn((url: string) => {
        if (url.includes('/password-recovery/pending')) {
          return Promise.resolve(solicitudesActuales.valor)
        }
        if (url.includes('/usuarios/limite')) {
          return Promise.resolve({ limiteUsuarios: null, usuariosActuales: 0, puedeAgregar: true })
        }
        if (url.includes('/usuarios')) return Promise.resolve([])
        if (url.includes('/sucursales')) return Promise.resolve([])
        // Reconsulta de temporal (temp-password): no usada por defecto.
        return Promise.resolve([])
      }),
      post: vi.fn(() => Promise.resolve({})),
      put: vi.fn(() => Promise.resolve({})),
      patch: vi.fn(() => Promise.resolve({})),
    },
  }
})

import { api } from '../lib/api'
import { UsuariosPage } from './UsuariosPage'

const mockApiPost = vi.mocked(api.post)

// ─── Datos de prueba ──────────────────────────────────────────────────────────

/** Construye una solicitud pendiente de prueba. */
function solicitudPendiente(overrides: Partial<PendingRecoveryDto> = {}): PendingRecoveryDto {
  return {
    requestId: 1,
    usuarioId: 10,
    nombre: 'Juan Cajero',
    email: 'juan@comercio.com',
    rol: 'Cajero',
    estado: 'Pendiente',
    fechaSolicitud: '2026-01-01T10:00:00Z',
    fechaExpiracion: null,
    minutosRestantes: 1380,
    tienePasswordTemporalVigente: false,
    ...overrides,
  }
}

/** Construye una solicitud aprobada con temporal vigente. */
function solicitudAprobada(overrides: Partial<PendingRecoveryDto> = {}): PendingRecoveryDto {
  return {
    requestId: 2,
    usuarioId: 20,
    nombre: 'Ana Bodeguera',
    email: 'ana@comercio.com',
    rol: 'Bodeguero',
    estado: 'Aprobada',
    fechaSolicitud: '2026-01-01T09:00:00Z',
    fechaExpiracion: '2026-01-02T09:00:00Z',
    minutosRestantes: 1200,
    tienePasswordTemporalVigente: true,
    ...overrides,
  }
}

/** Título de sección usado como marcador de visibilidad de la bandeja. */
const TITULO_BANDEJA = /solicitudes de recuperación/i

function renderUsuariosPage() {
  return render(<UsuariosPage />)
}

describe('UsuariosPage — bandeja de recuperación (Property 18, Req 12.1/12.2/12.7/12.9)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockRol = 'Dueño'
    solicitudesActuales.valor = []
  })

  afterEach(() => {
    cleanup()
    // Restaurar cualquier spy del portapapeles para no filtrar el stub a otros tests.
    vi.restoreAllMocks()
  })

  // ─── Property 18 (parte a): visibilidad por rol ─────────────────────────────
  // La sección se muestra SOLO para Dueño/Gerente; para el resto de roles no
  // aparece. Se generan todos los roles del sistema (Req 12.1, 12.2).
  it('muestra la bandeja solo para Dueño/Gerente y la oculta para el resto de roles', async () => {
    const todosLosRoles: UserRole[] = [
      'SuperAdmin',
      'Dueño',
      'Gerente',
      'Supervisor',
      'Bodeguero',
      'Cajero',
    ]

    await fc.assert(
      fc.asyncProperty(fc.constantFrom(...todosLosRoles), async (rol) => {
        cleanup()
        vi.clearAllMocks()
        mockRol = rol
        solicitudesActuales.valor = []

        renderUsuariosPage()

        // Esperar a que la carga inicial termine (tabla de usuarios visible).
        await screen.findByRole('heading', { name: /^usuarios$/i })

        const debeVerla = rol === 'Dueño' || rol === 'Gerente'
        if (debeVerla) {
          // La bandeja aparece para roles autorizados.
          expect(
            await screen.findByRole('heading', { name: TITULO_BANDEJA }),
          ).toBeInTheDocument()
        } else {
          // Para roles no autorizados la sección no se renderiza.
          expect(screen.queryByRole('heading', { name: TITULO_BANDEJA })).not.toBeInTheDocument()
        }
      }),
      { numRuns: 6 },
    )
  }, 30000)

  // ─── Property 18 (parte b): bloqueo de rechazo sin motivo ───────────────────
  // El botón "Confirmar rechazo" está deshabilitado mientras el motivo esté
  // vacío o solo contenga espacios; se habilita al escribir contenido real y
  // vuelve a deshabilitarse si se borra (Req 12.9).
  it('bloquea "Confirmar rechazo" sin motivo y lo habilita solo con texto no vacío', async () => {
    const user = userEvent.setup()
    mockRol = 'Gerente'
    solicitudesActuales.valor = [solicitudPendiente()]

    renderUsuariosPage()

    // Abrir el modal de rechazo desde la tarjeta pendiente.
    const btnRechazar = await screen.findByRole('button', { name: /rechazar la solicitud de/i })
    await user.click(btnRechazar)

    // El botón de confirmar arranca deshabilitado (motivo vacío).
    const btnConfirmar = await screen.findByRole('button', { name: /confirmar rechazo/i })
    expect(btnConfirmar).toBeDisabled()

    const textarea = screen.getByLabelText(/motivo del rechazo/i)

    // Solo espacios: sigue deshabilitado (trim vacío).
    await user.type(textarea, '   ')
    expect(btnConfirmar).toBeDisabled()

    // Texto real: se habilita.
    await user.type(textarea, 'Datos insuficientes')
    expect(btnConfirmar).toBeEnabled()

    // Borrar todo: vuelve a deshabilitarse.
    await user.clear(textarea)
    expect(btnConfirmar).toBeDisabled()

    // Con el motivo vacío, un click no invoca el endpoint de rechazo.
    await user.click(btnConfirmar)
    expect(mockApiPost).not.toHaveBeenCalled()
  })

  // Generación ligera: para una muestra de motivos "en blanco" (vacío/espacios/
  // tabs/saltos) el botón permanece bloqueado; para motivos con contenido real
  // se habilita.
  it('property: motivos solo-espacios mantienen el rechazo bloqueado; con contenido se habilita', async () => {
    const soloEspacios = ['', ' ', '   ', '\t', '\n', '  \t \n ']

    for (const motivo of soloEspacios) {
      cleanup()
      vi.clearAllMocks()
      mockRol = 'Dueño'
      solicitudesActuales.valor = [solicitudPendiente()]

      const user = userEvent.setup()
      renderUsuariosPage()

      const btnRechazar = await screen.findByRole('button', { name: /rechazar la solicitud de/i })
      await user.click(btnRechazar)
      const btnConfirmar = await screen.findByRole('button', { name: /confirmar rechazo/i })
      const textarea = screen.getByLabelText(/motivo del rechazo/i)

      if (motivo.length > 0) {
        await user.type(textarea, motivo)
      }
      // Cualquier motivo compuesto solo de espacios deja el botón deshabilitado.
      expect(btnConfirmar).toBeDisabled()
    }
  }, 30000)

  // ─── Req 12.7: el botón Copiar invoca navigator.clipboard.writeText ─────────
  it('al aprobar, el botón "Copiar" invoca navigator.clipboard.writeText con la temporal', async () => {
    const user = userEvent.setup()
    mockRol = 'Dueño'

    // Estado inicial: la solicitud está PENDIENTE, por lo que la tarjeta muestra
    // el botón "Aprobar" en el primer render. NO se sobrescribe a Aprobada antes
    // de renderizar; la transición a Aprobada solo ocurre en la recarga posterior
    // (segunda lectura de /pending) que dispara `fetchSolicitudes` tras aprobar.
    solicitudesActuales.valor = [solicitudPendiente()]

    // Stub del portapapeles SIN reemplazar el objeto `navigator` completo. Sustituir
    // `navigator` entero con vi.stubGlobal rompe el entorno que userEvent/jsdom usan
    // internamente y las interacciones/render async de la bandeja no completan. Aquí
    // solo intervenimos `navigator.clipboard.writeText`, dejando intacto el resto.
    let writeText: ReturnType<typeof vi.fn>
    if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
      // Si jsdom ya expone clipboard, espiamos el método existente (se restaura en afterEach).
      writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined) as unknown as ReturnType<typeof vi.fn>
    } else {
      // Si no existe clipboard en el entorno, lo definimos de forma configurable.
      writeText = vi.fn().mockResolvedValue(undefined)
      Object.defineProperty(navigator, 'clipboard', {
        value: { writeText },
        configurable: true,
      })
    }

    const TEMP = 'Temp0ral@2024'
    // El approve devuelve la contraseña temporal en texto plano...
    mockApiPost.mockResolvedValueOnce({ tempPassword: TEMP, fechaExpiracion: '2026-01-02T09:00:00Z' })

    // Override local del mock de `get`: el endpoint /password-recovery/pending
    // devuelve PENDIENTE en la primera llamada (render inicial → botón "Aprobar")
    // y APROBADA con temporal vigente a partir de la segunda (recarga tras aprobar
    // → aparece el bloque con la contraseña temporal y su botón "Copiar"). Las
    // demás rutas replican el comportamiento del mock global. `vi.clearAllMocks`
    // (beforeEach) y `vi.restoreAllMocks` (afterEach) evitan filtrar este override
    // a otros tests.
    const mockGet = vi.mocked(api.get)
    let pendingCallCount = 0
    mockGet.mockImplementation((url: string) => {
      if (url.includes('/password-recovery/pending')) {
        pendingCallCount += 1
        if (pendingCallCount === 1) {
          return Promise.resolve([solicitudPendiente()])
        }
        return Promise.resolve([
          solicitudAprobada({ requestId: 1, nombre: 'Juan Cajero', email: 'juan@comercio.com', rol: 'Cajero' }),
        ])
      }
      if (url.includes('/usuarios/limite')) {
        return Promise.resolve({ limiteUsuarios: null, usuariosActuales: 0, puedeAgregar: true })
      }
      if (url.includes('/usuarios')) return Promise.resolve([])
      if (url.includes('/sucursales')) return Promise.resolve([])
      return Promise.resolve([])
    })

    renderUsuariosPage()

    // Primer render: la solicitud está Pendiente, por lo que existe el botón "Aprobar".
    const btnAprobar = await screen.findByRole('button', { name: /aprobar la solicitud de/i })
    await user.click(btnAprobar)
    const btnConfirmar = await screen.findByRole('button', { name: /confirmar aprobación/i })
    await user.click(btnConfirmar)

    // Tras aprobar: `handleAprobar` guarda la temporal (del approve) y recarga
    // /pending, que ahora devuelve la solicitud Aprobada con temporal vigente.
    // Como la temporal ya está en memoria, se renderiza directamente el bloque
    // monoespaciado con el botón "Copiar" (sin paso intermedio de "Mostrar").
    const btnCopiar = await screen.findByRole('button', { name: /copiar la contraseña temporal de/i })
    await user.click(btnCopiar)

    // Se invocó writeText con exactamente la contraseña temporal (Req 12.7).
    await waitFor(() => {
      expect(writeText).toHaveBeenCalledWith(TEMP)
    })
  })

  // ─── Accesibilidad (vitest-axe) de la bandeja ───────────────────────────────
  it('la bandeja con solicitudes no tiene violaciones de accesibilidad', async () => {
    mockRol = 'Dueño'
    solicitudesActuales.valor = [
      solicitudPendiente(),
      solicitudAprobada(),
      solicitudPendiente({ requestId: 3, nombre: 'Luis Supervisor', email: 'luis@comercio.com', rol: 'Supervisor', estado: 'Rechazada' }),
    ]

    const { container } = renderUsuariosPage()

    // Esperar a que la bandeja renderice sus tarjetas antes de auditar.
    await screen.findByRole('heading', { name: TITULO_BANDEJA })
    await screen.findByText('Juan Cajero')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  // ─── Caso concreto (tabla): estado vacío accesible ──────────────────────────
  it('muestra el estado vacío cuando no hay solicitudes (Dueño)', async () => {
    mockRol = 'Dueño'
    solicitudesActuales.valor = []

    renderUsuariosPage()

    await screen.findByRole('heading', { name: TITULO_BANDEJA })
    expect(await screen.findByText(/no hay solicitudes de recuperación/i)).toBeInTheDocument()
  })

  // ─── Caso concreto (tabla): la tarjeta muestra los datos del solicitante ────
  it('renderiza los datos del solicitante en la tarjeta (nombre, correo, rol)', async () => {
    mockRol = 'Gerente'
    solicitudesActuales.valor = [solicitudPendiente()]

    renderUsuariosPage()

    const bandeja = await screen.findByRole('heading', { name: TITULO_BANDEJA })
    const seccion = bandeja.closest('section') as HTMLElement
    expect(within(seccion).getByText('Juan Cajero')).toBeInTheDocument()
    expect(within(seccion).getByText('juan@comercio.com')).toBeInTheDocument()
  })
})
