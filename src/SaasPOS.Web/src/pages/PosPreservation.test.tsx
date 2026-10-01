/**
 * Tests de preservación (Property 2) para el bugfix de tooltip en nombres de producto.
 * Validan que el comportamiento existente (clics, CSS, aria-labels) se mantiene sin cambios.
 *
 * Validates: Requirements 3.1, 3.2, 3.3, 3.4
 *
 * IMPORTANTE: Estos tests DEBEN PASAR en código sin corregir y después del fix.
 */
import { render, screen, waitFor, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import * as fc from 'fast-check'

// ─── Mocks ────────────────────────────────────────────────────────────────────

// Mock del módulo api ANTES de importar los componentes
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

// Mock del AuthContext para que PosNormalPage/PosBarPage puedan montarse sin
// lanzar "useAuth must be used within an AuthProvider". Se usa un factory inline
// (mismo patrón validado en TicketPrintBugCondition.test.tsx) en lugar del helper
// compartido, porque `vi.mock` se hoistea al top del archivo y no puede referenciar
// imports que aún no están inicializados (el helper importa @testing-library/react).
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: null,
    claims: null,
    isAuthenticated: true,
    comercioNombre: 'Comercio Demo',
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: () => true,
  }),
}))

// Mock window.print para evitar errores en PosNormalPage
vi.stubGlobal('print', vi.fn())

import { api } from '../lib/api'
import { PosNormalPage } from './PosNormalPage'
import { PosBarPage } from './PosBarPage'

const mockApiGet = vi.mocked(api.get)
const mockApiPost = vi.mocked(api.post)

// ─── Tipos internos para tests ────────────────────────────────────────────────

interface TestProducto {
  id: number
  nombre: string
  tipoArticulo: 'Venta Directa' | 'Ensamblado'
  precioLista: number
  categoriaId: number | null
  categoriaNombre: string | null
}

// ─── Generadores de datos ─────────────────────────────────────────────────────

/** Generador de nombres de producto con longitudes variadas (sin espacios múltiples consecutivos).
 * Usa .map() en vez de .filter() para garantizar que el shrinking no reintroduzca espacios dobles. */
const productNameArb = fc
  .array(
    fc.oneof(
      { weight: 10, arbitrary: fc.constantFrom(
        ...'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'.split('')
      ) },
      { weight: 2, arbitrary: fc.constant(' ') }
    ),
    { minLength: 5, maxLength: 40 }
  )
  .map((chars) => {
    // Normalizar: colapsar espacios múltiples, trim, y asegurar largo mínimo
    const normalized = chars.join('').replace(/\s{2,}/g, ' ').trim()
    // Si queda muy corto después de normalizar, rellenar con caracteres
    return normalized.length >= 3 ? normalized : normalized + 'Abc'
  })

/** Generador de un producto válido */
const productoArb: fc.Arbitrary<TestProducto> = fc.record({
  id: fc.integer({ min: 1, max: 9999 }),
  nombre: productNameArb,
  tipoArticulo: fc.constantFrom('Venta Directa' as const, 'Ensamblado' as const),
  precioLista: fc.integer({ min: 1, max: 9999 }).map((v) => v / 100),
  categoriaId: fc.constant(null),
  categoriaNombre: fc.constant(null),
})

// ─── Helpers ──────────────────────────────────────────────────────────────────

function setupMocksForPosNormal(productos: TestProducto[]) {
  // Construir mapa de precios volumen vacío
  const preciosMap: Record<number, never[]> = {}
  productos.forEach((p) => { preciosMap[p.id] = [] })

  mockApiGet.mockImplementation(((endpoint: string) => {
    if (endpoint.includes('/precios-volumen')) {
      return Promise.resolve(preciosMap)
    }
    // El endpoint de comprobantes espera un ARRAY; se evalúa ANTES que la
    // condición genérica '/configuracion' (evita "comprobantesData.filter is not a function").
    if (endpoint.includes('/comprobantes/configuracion')) {
      return Promise.resolve([{ tipoComprobante: 'Ticket Digital', habilitado: true }])
    }
    if (endpoint.includes('/configuracion')) {
      return Promise.resolve({
        mostrarBotonCliente: false,
        impresionAutomaticaTicket: false,
        usaFacturacionSRI: false,
        permitePrecioNegociado: false,
        esBarEscolar: false,
      })
    }
    if (endpoint.includes('/sucursales')) {
      return Promise.resolve([{ id: 1, nombre: 'Sucursal Principal' }])
    }
    if (endpoint.includes('/productos')) {
      return Promise.resolve(productos)
    }
    return Promise.resolve([])
  }) as typeof api.get)
}

function setupMocksForPosBar(productos: TestProducto[]) {
  mockApiGet.mockImplementation(((endpoint: string) => {
    if (endpoint.includes('/productos')) {
      return Promise.resolve(productos)
    }
    if (endpoint.includes('/configuracion')) {
      return Promise.resolve({ esBarEscolar: true })
    }
    if (endpoint.includes('/sucursales')) {
      return Promise.resolve([{ id: 1, nombre: 'Sucursal Principal' }])
    }
    return Promise.resolve([])
  }) as typeof api.get)
}

// ─── Tests de Preservación ────────────────────────────────────────────────────

describe('Property 2: Preservation — Funcionalidad de clic y estructura visual sin cambios', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Reestablecer el stub global de print en cada test: el afterEach global de
    // setup.ts ahora llama vi.unstubAllGlobals(), que elimina el stub definido a
    // nivel de archivo tras el primer caso. PosNormalPage puede invocar print al
    // montar/registrar venta, así que se re-stubbea aquí para que persista por test.
    vi.stubGlobal('print', vi.fn())
    // Estrategia de timers única y coherente: se usan timers reales en todo el
    // archivo. Las pruebas dependen de `waitFor` sobre promesas del mock de la
    // API, no de temporizadores simulados. Usar fake timers aquí bloqueaba la
    // resolución de esas promesas y provocaba timeouts y "Error: Timers are not
    // mocked" en el afterEach.
  })

  afterEach(() => {
    cleanup()
  })

  /**
   * **Validates: Requirements 3.1, 3.2**
   *
   * Para cualquier producto renderizado en PosNormalPage, hacer clic en el botón
   * del producto invoca el handler de agregar al carrito con el producto correcto.
   */
  describe('PosNormalPage — clic invoca add-to-cart con producto.id correcto', () => {
    it('property: clicking any product button adds it to cart state', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          const productos = [producto]
          setupMocksForPosNormal(productos)

          const { container } = render(<PosNormalPage />)

          // Esperar a que carguen los productos usando querySelector (evita normalización de espacios de findByRole)
          await waitFor(() => {
            const btn = container.querySelector('button[aria-label^="Agregar"]')
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          const button = container.querySelector('button[aria-label^="Agregar"]')!

          // Hacer clic en el producto
          const user = userEvent.setup()
          await user.click(button)

          // Verificar que el producto aparece en el detalle de venta (carrito)
          // Usamos textContent en lugar de getByText para evitar problemas
          // de normalización de espacios en Testing Library
          await waitFor(() => {
            const cartHeading = screen.getByText('Detalle de Venta')
            const cartPanel = cartHeading.closest('div')!.parentElement!
            const allText = cartPanel.textContent || ''
            expect(allText).toContain(producto.nombre)
          }, { timeout: 5000 })

          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)
  })

  /**
   * **Validates: Requirements 3.3**
   *
   * Para cualquier producto renderizado en PosBarPage, hacer clic en el botón
   * del producto invoca el handler de venta directa con el producto.id correcto.
   */
  describe('PosBarPage — clic invoca direct-sale handler con producto.id correcto', () => {
    it('property: clicking any product button calls bar-escolar API with correct productoId', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          const productos = [producto]
          setupMocksForPosBar(productos)
          mockApiPost.mockResolvedValue(undefined as never)

          const { container, unmount } = render(<PosBarPage />)

          // Esperar a que carguen los productos
          await waitFor(() => {
            const btn = container.querySelector(`button[aria-label^="Vender"]`)
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          const button = container.querySelector(`button[aria-label^="Vender"]`)!

          // Hacer clic en el producto
          const user = userEvent.setup()
          await user.click(button)

          // Verificar que se llamó a la API con los datos correctos
          await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith(
              '/api/tenants/ventas/bar-escolar',
              expect.objectContaining({
                productoId: producto.id,
                sucursalId: 1,
              })
            )
          }, { timeout: 5000 })

          // unmount() explícito por iteración: el clic dispara el toast de éxito y
          // su temporizador de auto-cierre. Al desmontar se ejecuta el cleanup del
          // componente que cancela ese setTimeout, liberando el fiber/estado en cada
          // iteración en lugar de dejarlo vivo hasta el cleanup() global entre tests.
          unmount()
          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)
  })

  /**
   * **Validates: Requirements 3.4**
   *
   * Para cualquier producto renderizado, las clases CSS de truncamiento
   * (truncate en PosNormalPage, line-clamp-2 en PosBarPage) permanecen aplicadas.
   */
  describe('CSS truncation classes permanecen aplicadas a spans de nombre', () => {
    it('property: PosNormalPage — all product name spans have "truncate" class', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          setupMocksForPosNormal([producto])

          const { container } = render(<PosNormalPage />)

          // Esperar a que aparezca el botón usando waitFor + querySelector
          await waitFor(() => {
            const btn = container.querySelector('button[aria-label^="Agregar"]')
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          // Verificar que el span de nombre tiene la clase "truncate"
          const productButtons = container.querySelectorAll('button[aria-label^="Agregar"]')
          expect(productButtons.length).toBeGreaterThan(0)
          for (const btn of productButtons) {
            const nameSpan = btn.querySelector('span.truncate')
            expect(nameSpan).not.toBeNull()
          }

          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)

    it('property: PosBarPage — all product name spans have "line-clamp-2" class', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          setupMocksForPosBar([producto])

          const { container } = render(<PosBarPage />)

          // Esperar a que aparezca el botón usando waitFor + querySelector
          await waitFor(() => {
            const btn = container.querySelector('button[aria-label^="Vender"]')
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          // Verificar que el span de nombre tiene la clase "line-clamp-2"
          const productButtons = container.querySelectorAll('button[aria-label^="Vender"]')
          expect(productButtons.length).toBeGreaterThan(0)
          for (const btn of productButtons) {
            const nameSpan = btn.querySelector('.line-clamp-2')
            expect(nameSpan).not.toBeNull()
          }

          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)
  })

  /**
   * **Validates: Requirements 3.4**
   *
   * Los aria-labels en los botones de producto permanecen presentes y correctos.
   * Nota: todo el archivo usa timers reales (ver beforeEach) para evitar conflictos
   * con waitFor que causaban timeouts por las Promises del mock API que no se resolvían.
   */
  describe('Aria-labels en botones de producto permanecen correctos', () => {
    it('property: PosNormalPage — all product buttons have correct aria-label', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          setupMocksForPosNormal([producto])

          const { container } = render(<PosNormalPage />)

          // Esperar a que aparezca el botón usando waitFor + querySelector
          await waitFor(() => {
            const btn = container.querySelector('button[aria-label^="Agregar"]')
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          // Verificar aria-label correcto
          const buttons = container.querySelectorAll('button[aria-label^="Agregar"]')
          expect(buttons.length).toBe(1)
          expect(buttons[0]!.getAttribute('aria-label')).toBe(
            `Agregar ${producto.nombre} al carrito`
          )

          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)

    it('property: PosBarPage — all product buttons have correct aria-label', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          setupMocksForPosBar([producto])

          const { container } = render(<PosBarPage />)

          // Esperar a que aparezca el botón usando waitFor + querySelector
          await waitFor(() => {
            const btn = container.querySelector('button[aria-label^="Vender"]')
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          // Verificar aria-label correcto
          const buttons = container.querySelectorAll('button[aria-label^="Vender"]')
          expect(buttons.length).toBe(1)
          const expectedLabel = `Vender ${producto.nombre} por $${producto.precioLista.toFixed(2)}`
          expect(buttons[0]!.getAttribute('aria-label')).toBe(expectedLabel)

          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)
  })

  /**
   * **Validates: Requirements 3.4**
   *
   * El grid de productos mantiene las clases CSS responsivas.
   */
  describe('Grid responsivo mantiene clases CSS correctas', () => {
    it('property: PosNormalPage — product grid has responsive grid-cols classes', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          setupMocksForPosNormal([producto])

          const { container } = render(<PosNormalPage />)

          // Esperar a que aparezcan los productos
          await waitFor(() => {
            const btn = container.querySelector('button[aria-label^="Agregar"]')
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          // Buscar el grid de productos (grid-cols-2 sm:grid-cols-3 md:grid-cols-4)
          const grid = container.querySelector('.grid.grid-cols-2')
          expect(grid).not.toBeNull()
          expect(grid!.classList.contains('grid-cols-2')).toBe(true)
          expect(grid!.className).toContain('sm:grid-cols-3')
          expect(grid!.className).toContain('md:grid-cols-4')

          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)

    it('property: PosBarPage — product grid has responsive grid-cols classes', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          cleanup()
          vi.clearAllMocks()
          setupMocksForPosBar([producto])

          const { container } = render(<PosBarPage />)

          // Esperar a que aparezcan los productos
          await waitFor(() => {
            const btn = container.querySelector('button[aria-label^="Vender"]')
            expect(btn).not.toBeNull()
          }, { timeout: 5000 })

          // Buscar el grid de productos
          const grid = container.querySelector('.grid.grid-cols-2')
          expect(grid).not.toBeNull()
          expect(grid!.classList.contains('grid-cols-2')).toBe(true)
          expect(grid!.className).toContain('sm:grid-cols-3')
          expect(grid!.className).toContain('md:grid-cols-4')
          expect(grid!.className).toContain('lg:grid-cols-5')
          expect(grid!.className).toContain('xl:grid-cols-6')

          cleanup()
        }),
        { numRuns: 1 }
      )
    }, 60000)
  })
})
