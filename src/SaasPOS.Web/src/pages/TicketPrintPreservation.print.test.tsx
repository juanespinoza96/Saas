/**
 * Pruebas de PRESERVACIÓN (property-based) — Property 2 — Parte (c)
 * Spec: ticket-digital-pdf-blanco (bugfix)
 *
 * Este archivo contiene ÚNICAMENTE la propiedad (c). Se separó de
 * TicketPrintPreservation.test.tsx (junto con (a) y (b)) para que, con
 * `isolate: true`, cada propiedad corra en su propio proceso y libere el heap al
 * terminar, repartiendo el pico de memoria que provocaba el OOM al ejecutarlas
 * juntas. Se conservan EXACTAMENTE los mismos generadores y aserciones.
 *
 * OBJETIVO (c): El disparo de `window.print()` se preserva según la configuración
 * del comercio. Con `impresionAutomaticaTicket = true`, tras registrar la venta se
 * invoca `window.print()` automáticamente; con `false`, NO se dispara sola. El
 * arreglo del bug (CSS) no altera esta lógica de disparo.
 *
 * **Validates: Requirements 3.2, 3.4**
 */
import { render, screen, waitFor, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import * as fc from 'fast-check'
import { PosNormalPage } from './PosNormalPage'

// Mock del módulo API
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

// Mock del AuthContext para proveer comercioNombre sin necesidad del provider
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    comercioNombre: 'Comercio Demo',
    isAuthenticated: true,
    claims: null,
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: () => true,
  }),
}))

import { api } from '../lib/api'
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

// ─── Generadores (fast-check) ───────────────────────────────────────────────

/** Nombres de producto deterministas y únicos (evita colisiones de aria-label). */
function productoArbFactory(): fc.Arbitrary<TestProducto> {
  return fc.record({
    id: fc.integer({ min: 1, max: 9999 }),
    nombre: fc
      .array(
        fc.constantFrom(...'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'.split('')),
        { minLength: 4, maxLength: 16 },
      )
      .map((chars) => chars.join('')),
    tipoArticulo: fc.constantFrom('Venta Directa' as const, 'Ensamblado' as const),
    precioLista: fc.integer({ min: 1, max: 50000 }).map((v) => v / 100),
    categoriaId: fc.constant(null),
    categoriaNombre: fc.constant(null),
  })
}

/** Lista de productos con ids y nombres únicos. */
const productosArb: fc.Arbitrary<TestProducto[]> = fc
  .uniqueArray(productoArbFactory(), {
    minLength: 1,
    maxLength: 4,
    selector: (p) => p.nombre,
  })
  .map((productos) => {
    const seen = new Set<number>()
    return productos.map((p) => {
      let id = p.id
      while (seen.has(id)) id += 1
      seen.add(id)
      return { ...p, id }
    })
  })

// ─── Helpers de setup ─────────────────────────────────────────────────────────

interface SetupOptions {
  productos: TestProducto[]
  config: {
    mostrarBotonCliente: boolean
    impresionAutomaticaTicket: boolean
    usaFacturacionSRI: boolean
    permitePrecioNegociado: boolean
    esBarEscolar: boolean
  }
  tipoComprobante: string
  clientes?: Array<{ id: number; identificacion: string; nombre: string; correo: string | null }>
}

function setupApiMocks(opts: SetupOptions) {
  const preciosMap: Record<number, never[]> = {}
  opts.productos.forEach((p) => {
    preciosMap[p.id] = []
  })

  mockApiGet.mockImplementation(((url: string) => {
    if (url.includes('precios-volumen')) return Promise.resolve(preciosMap)
    if (url.includes('comprobantes/configuracion'))
      return Promise.resolve([{ tipoComprobante: opts.tipoComprobante, habilitado: true }])
    if (url.includes('clientes'))
      return Promise.resolve({ items: opts.clientes ?? [], total: (opts.clientes ?? []).length })
    if (url.includes('productos')) return Promise.resolve(opts.productos)
    if (url.includes('configuracion')) return Promise.resolve(opts.config)
    if (url.includes('sucursales')) return Promise.resolve([{ id: 1, nombre: 'Principal' }])
    return Promise.resolve([])
  }) as typeof api.get)

  mockApiPost.mockResolvedValue({} as never)
}

const baseConfig = {
  mostrarBotonCliente: false,
  impresionAutomaticaTicket: false,
  usaFacturacionSRI: false,
  permitePrecioNegociado: false,
  esBarEscolar: false,
}

async function registrarVenta(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: /confirmar venta/i }))
  await user.click(await screen.findByRole('button', { name: /registrar venta/i }))
}

// ─── Test de Preservación (c) ───────────────────────────────────────────────

describe('Property 2: Preservation (c) — Disparo de impresión según `impresionAutomaticaTicket`', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.spyOn(window, 'print').mockImplementation(() => {})
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  /**
   * **Validates: Requirements 3.2, 3.4**
   *
   * Con `impresionAutomaticaTicket = true` se invoca `window.print()` automáticamente;
   * con `false`, NO se dispara sola.
   */
  it('property: window.print() se dispara automáticamente sii impresionAutomaticaTicket es true', async () => {
    await fc.assert(
      fc.asyncProperty(productosArb, fc.boolean(), async (productos, autoPrint) => {
        cleanup()
        vi.clearAllMocks()
        const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {})
        vi.useFakeTimers({ shouldAdvanceTime: true })

        setupApiMocks({
          productos,
          config: { ...baseConfig, impresionAutomaticaTicket: autoPrint },
          tipoComprobante: 'Ticket Digital',
        })
        mockApiPost.mockResolvedValue({} as never)

        const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
        render(<PosNormalPage />)

        const primer = productos[0]!
        await waitFor(() => {
          expect(screen.getByLabelText(`Agregar ${primer.nombre} al carrito`)).toBeInTheDocument()
        }, { timeout: 5000 })

        await user.click(screen.getByLabelText(`Agregar ${primer.nombre} al carrito`))
        await registrarVenta(user)

        // Esperar a que la venta se procese (mensaje de éxito)
        await waitFor(() => {
          expect(screen.getByText(/venta registrada exitosamente/i)).toBeInTheDocument()
        }, { timeout: 5000 })

        // El disparo automático usa setTimeout(..., 100) → avanzar timers
        await vi.advanceTimersByTimeAsync(200)

        if (autoPrint) {
          expect(printSpy).toHaveBeenCalled()
        } else {
          expect(printSpy).not.toHaveBeenCalled()
        }

        vi.clearAllTimers()
        vi.useRealTimers()
        cleanup()
      }),
      // Al aislar esta propiedad en su propio proceso, el pico de memoria baja.
      // numRuns=3 conserva el generador (incluye fc.boolean() para ambas ramas de
      // autoPrint) y las aserciones.
      { numRuns: 3 },
    )
  }, 90000)
})
