/**
 * Pruebas del Guard_Router de cambio de contraseña obligatorio
 * (`RequirePasswordChangeGuard`), tarea 23.1 del spec
 * `recuperacion-password-jerarquica`.
 *
 * Feature: recuperacion-password-jerarquica, Property 17: El guard de frontend
 * fuerza el cambio de contraseña — cuando `mustChangePassword` está activo,
 * cualquier ruta protegida distinta de `/cambiar-password` resuelve a
 * `/cambiar-password`; cuando está inactivo, la navegación fluye normal.
 *
 * _Requirements: 9.1, 9.2_
 */
import { render, screen, cleanup } from '@testing-library/react'
import { describe, it, expect, vi, afterEach } from 'vitest'
import fc from 'fast-check'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { RequirePasswordChangeGuard } from './RequirePasswordChangeGuard'

// ─── Mock del AuthContext ─────────────────────────────────────────────────────
// El guard solo lee `mustChangePassword`. Se controla mediante una variable
// mutable capturada por el mock del módulo, que cada test ajusta antes de
// renderizar (mismo patrón validado en otros tests del POS).
let mockMustChangePassword = false
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    mustChangePassword: mockMustChangePassword,
    // Resto de la forma del contexto, no usada por el guard.
    token: 'fake-token',
    claims: null,
    isAuthenticated: true,
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: () => true,
    comercioNombre: 'Comercio Demo',
    clearMustChangePassword: vi.fn(),
  }),
}))

/**
 * Conjunto de rutas protegidas representativas del POS (además de la propia
 * `/cambiar-password`). Se usa como espacio de entrada para la generación
 * ligera de la Property 17.
 */
const RUTAS_PROTEGIDAS = [
  '/dashboard',
  '/pos',
  '/pos-bar',
  '/productos',
  '/categorias',
  '/inventario',
  '/clientes',
  '/reportes',
  '/notificaciones',
  '/usuarios',
  '/sucursales',
  '/configuracion',
  '/configuracion/comprobantes',
  '/chat-ia',
]

/** Texto de marca para cada pantalla, para verificar qué se renderizó. */
const MARCA_PROTEGIDA = 'CONTENIDO_PROTEGIDO'
const MARCA_CAMBIAR = 'PANTALLA_CAMBIAR_PASSWORD'

/**
 * Monta el guard dentro de un router en memoria arrancando en `rutaInicial`.
 * Replica la estructura de `routes.tsx`: `/cambiar-password` queda fuera del
 * guard y el resto de rutas protegidas quedan dentro de él.
 */
function renderConGuard(rutaInicial: string) {
  return render(
    <MemoryRouter initialEntries={[rutaInicial]}>
      <Routes>
        {/* La pantalla de cambio va fuera del guard para no crear bucle. */}
        <Route path="/cambiar-password" element={<div>{MARCA_CAMBIAR}</div>} />

        {/* El resto de rutas protegidas quedan bajo el guard. */}
        <Route element={<RequirePasswordChangeGuard />}>
          {RUTAS_PROTEGIDAS.map(ruta => (
            <Route
              key={ruta}
              path={ruta}
              element={<div>{`${MARCA_PROTEGIDA}:${ruta}`}</div>}
            />
          ))}
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('RequirePasswordChangeGuard — Property 17 (Req 9.1, 9.2)', () => {
  afterEach(() => {
    cleanup()
    mockMustChangePassword = false
  })

  // Property 17 (parte a): con el claim activo, cualquier ruta protegida
  // distinta de /cambiar-password resuelve a la pantalla de cambio (Req 9.1, 9.2).
  it('con mustChangePassword=true, cualquier ruta protegida resuelve a /cambiar-password', () => {
    fc.assert(
      fc.property(fc.constantFrom(...RUTAS_PROTEGIDAS), (ruta) => {
        cleanup()
        mockMustChangePassword = true

        renderConGuard(ruta)

        // Se renderiza la pantalla de cambio y NO el contenido protegido.
        expect(screen.getByText(MARCA_CAMBIAR)).toBeInTheDocument()
        expect(screen.queryByText(`${MARCA_PROTEGIDA}:${ruta}`)).not.toBeInTheDocument()
      }),
      { numRuns: RUTAS_PROTEGIDAS.length },
    )
  })

  // Property 17 (parte b): con el claim activo y estando ya en /cambiar-password,
  // el guard NO interfiere (evita el bucle de redirección). Esta ruta está fuera
  // del guard en routes.tsx, pero se verifica que resuelve a la pantalla de cambio.
  it('con mustChangePassword=true, /cambiar-password se renderiza sin redirección en bucle', () => {
    mockMustChangePassword = true

    renderConGuard('/cambiar-password')

    expect(screen.getByText(MARCA_CAMBIAR)).toBeInTheDocument()
  })

  // Property 17 (parte c): con el claim inactivo, la navegación fluye normal y
  // cada ruta protegida renderiza su propio contenido (no se fuerza el cambio).
  it('con mustChangePassword=false, cada ruta protegida renderiza su contenido normal', () => {
    fc.assert(
      fc.property(fc.constantFrom(...RUTAS_PROTEGIDAS), (ruta) => {
        cleanup()
        mockMustChangePassword = false

        renderConGuard(ruta)

        // Se renderiza el contenido protegido y NO la pantalla de cambio.
        expect(screen.getByText(`${MARCA_PROTEGIDA}:${ruta}`)).toBeInTheDocument()
        expect(screen.queryByText(MARCA_CAMBIAR)).not.toBeInTheDocument()
      }),
      { numRuns: RUTAS_PROTEGIDAS.length },
    )
  })

  // Caso concreto adicional (tabla): un par de rutas frecuentes, para dejar
  // ejemplos legibles junto a la propiedad generada.
  it('caso concreto: /dashboard con el claim activo redirige a /cambiar-password', () => {
    mockMustChangePassword = true
    renderConGuard('/dashboard')
    expect(screen.getByText(MARCA_CAMBIAR)).toBeInTheDocument()
    expect(screen.queryByText(`${MARCA_PROTEGIDA}:/dashboard`)).not.toBeInTheDocument()
  })

  it('caso concreto: /usuarios con el claim inactivo se muestra normal', () => {
    mockMustChangePassword = false
    renderConGuard('/usuarios')
    expect(screen.getByText(`${MARCA_PROTEGIDA}:/usuarios`)).toBeInTheDocument()
  })
})
