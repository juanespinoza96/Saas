/**
 * Helper compartido de contexto de autenticación para pruebas del POS.
 *
 * Centraliza el patrón ya validado en `TicketPrintBugCondition.test.tsx` y
 * `TicketPrintPreservation.test.tsx`, donde las páginas que consumen `useAuth()`
 * (p.ej. `PosNormalPage`, `PosBarPage`) se montan sin lanzar
 * `useAuth must be used within an AuthProvider`.
 *
 * Ofrece dos variantes equivalentes; cada archivo de prueba elige la que mejor
 * encaje:
 *
 *   - Variante A (mock del módulo): `createAuthContextMock()` devuelve el objeto
 *     que espera `vi.mock('../contexts/AuthContext', ...)`, exponiendo un
 *     `useAuth` de prueba con forma completa alineada con `AuthContextValue`.
 *
 *   - Variante B (render wrapper): `renderWithAuth(ui, options)` envuelve `ui` en
 *     el `<AuthProvider>` real vía `render(..., { wrapper })` de Testing Library.
 *
 * NOTA: Este helper solo debe ser importado por archivos de prueba. NO modifica
 * código de producción.
 */
import { render, type RenderOptions, type RenderResult } from '@testing-library/react'
import { vi } from 'vitest'
import type { ReactElement, ReactNode } from 'react'
import { AuthProvider } from '../contexts/AuthContext'
import type { AuthState, JwtClaims, UserRole } from '../types/auth'

/**
 * Forma del valor de `useAuth` de prueba, alineada con `AuthContextValue`
 * (definido en `src/contexts/AuthContext.tsx`): `AuthState` más `login`,
 * `logout`, `hasRole` y `comercioNombre`.
 */
export interface TestAuthContextValue extends AuthState {
  login: (token: string) => void
  logout: () => void
  hasRole: (role: UserRole | UserRole[]) => boolean
  comercioNombre: string
}

/**
 * Valores por defecto del `useAuth` de prueba. Representa una sesión autenticada
 * mínima sin token real, suficiente para que los componentes del POS rendericen.
 */
export const defaultTestAuth: TestAuthContextValue = {
  token: null,
  claims: null,
  isAuthenticated: true,
  comercioNombre: 'Comercio Demo',
  login: vi.fn(),
  logout: vi.fn(),
  hasRole: () => true,
}

/**
 * Construye el valor de prueba de `useAuth`, permitiendo sobrescribir campos
 * concretos (p.ej. `claims`, `isAuthenticated`, `hasRole`) por prueba.
 */
export function createTestAuthValue(
  overrides: Partial<TestAuthContextValue> = {},
): TestAuthContextValue {
  return {
    // Se reconstruyen las funciones mock en cada llamada para evitar que el
    // estado de una prueba se filtre a otra al reutilizar `defaultTestAuth`.
    token: null,
    claims: null,
    isAuthenticated: true,
    comercioNombre: 'Comercio Demo',
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: () => true,
    ...overrides,
  }
}

/**
 * Variante A — Factory para `vi.mock('../contexts/AuthContext', ...)`.
 *
 * Devuelve el objeto de módulo mockeado con un `useAuth` que expone la forma
 * completa de `AuthContextValue`. Uso típico en un archivo de prueba:
 *
 *   vi.mock('../contexts/AuthContext', () => createAuthContextMock())
 *
 * Para personalizar los valores en una prueba concreta:
 *
 *   vi.mock('../contexts/AuthContext', () =>
 *     createAuthContextMock({ comercioNombre: 'Otro Comercio' }),
 *   )
 */
export function createAuthContextMock(overrides: Partial<TestAuthContextValue> = {}) {
  const value = createTestAuthValue(overrides)
  return {
    // `useAuth` devuelve siempre el mismo valor de prueba dentro del archivo.
    useAuth: () => value,
    // Se reexpone `AuthProvider` como passthrough por si algún componente lo
    // importa directamente; en pruebas no aporta contexto adicional.
    AuthProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
  }
}

/**
 * Variante B — Wrapper de render que envuelve `ui` en el `<AuthProvider>` real.
 *
 * Útil cuando se prefiere ejercitar el proveedor real en lugar de mockear el
 * módulo. Acepta las mismas `options` que `render` de Testing Library.
 */
export function renderWithAuth(
  ui: ReactElement,
  options?: Omit<RenderOptions, 'wrapper'>,
): RenderResult {
  // Envuelve el árbol bajo prueba con el AuthProvider real de la aplicación.
  function Wrapper({ children }: { children: ReactNode }) {
    return <AuthProvider>{children}</AuthProvider>
  }

  return render(ui, { wrapper: Wrapper, ...options })
}

/**
 * Claims de ejemplo para pruebas que necesiten un usuario autenticado con rol.
 * Se ofrece como conveniencia; no se usa por defecto.
 */
export const testJwtClaims: JwtClaims = {
  sub: 'test-user',
  comercio_id: '1',
  role: 'Cajero',
  jti: 'test-jti',
  exp: Math.floor(Date.now() / 1000) + 60 * 60,
  comercio_nombre: 'Comercio Demo',
}
