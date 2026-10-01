/**
 * Pruebas de PRESERVACIÓN (property-based) — Property 2 — Parte (b)
 * Spec: ticket-digital-pdf-blanco (bugfix)
 *
 * Este archivo contiene ÚNICAMENTE la propiedad (b). Se separó de
 * TicketPrintPreservation.test.tsx (junto con (a) y (c)) para que, con
 * `isolate: true`, cada propiedad corra en su propio proceso y libere el heap al
 * terminar, repartiendo el pico de memoria que provocaba el OOM al ejecutarlas
 * juntas. Se conservan EXACTAMENTE los mismos generadores y aserciones.
 *
 * OBJETIVO (b): El payload de la venta enviado a POST /api/tenants/ventas se
 * preserva de forma equivalente para ventas aleatorias (persistencia/lógica de
 * negocio). El arreglo del bug es puramente CSS, por lo que la estructura y los
 * valores del payload NO deben cambiar.
 *
 * **Validates: Requirements 3.4**
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

interface TestCliente {
  id: number
  identificacion: string
  nombre: string
  correo: string | null
}

// Forma del payload de venta enviado a POST /api/tenants/ventas (observada en handleConfirmSale)
interface VentaPayload {
  sucursalId: number
  clienteId: number | null
  tipoComprobante: string
  metodoPago: string
  cuotas: number
  referenciaTransaccion: string | null
  lineas: Array<{ productoId: number; cantidad: number; precioRealCobrado: number | null }>
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

/** Cliente opcional (con/sin cliente). */
const clienteArb: fc.Arbitrary<TestCliente> = fc.record({
  id: fc.integer({ min: 1, max: 9999 }),
  identificacion: fc
    .array(fc.constantFrom(...'0123456789'.split('')), { minLength: 10, maxLength: 13 })
    .map((chars) => chars.join('')),
  nombre: fc
    .array(fc.constantFrom(...'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'.split('')), {
      minLength: 4,
      maxLength: 20,
    })
    .map((chars) => chars.join('')),
  correo: fc.constant(null),
})

/** Tipo de comprobante habilitado. */
const tipoComprobanteArb = fc.constantFrom('Ticket Digital', 'Nota de Venta', 'Factura')

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
  clientes?: TestCliente[]
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

// ─── Test de Preservación (b) ───────────────────────────────────────────────

describe('Property 2: Preservation (b) — Payload de venta a /api/tenants/ventas se preserva', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.spyOn(window, 'print').mockImplementation(() => {})
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  /**
   * **Validates: Requirements 3.4**
   *
   * Se generan ventas aleatorias (distintos productos, con/sin cliente y tipos de
   * comprobante) y se verifica que el payload conserva su forma canónica observada.
   */
  it('property: el payload de venta conserva su forma canónica para ventas aleatorias', async () => {
    await fc.assert(
      fc.asyncProperty(
        productosArb,
        fc.option(clienteArb, { nil: null }),
        tipoComprobanteArb,
        async (productos, clienteOpt, tipoComprobante) => {
          cleanup()
          vi.clearAllMocks()
          // Fake timers por iteración (mismo motivo que en (a)): evita que el
          // setTimeout de 4000ms de PosNormalPage sobreviva al unmount. No cambia
          // generadores ni aserciones.
          vi.useFakeTimers({ shouldAdvanceTime: true })
          vi.spyOn(window, 'print').mockImplementation(() => {})

          const conCliente = clienteOpt !== null
          setupApiMocks({
            productos,
            config: { ...baseConfig, mostrarBotonCliente: conCliente },
            tipoComprobante,
            clientes: conCliente ? [clienteOpt] : [],
          })
          mockApiPost.mockResolvedValue({} as never)

          render(<PosNormalPage />)
          const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })

          const primer = productos[0]!
          await waitFor(() => {
            expect(screen.getByLabelText(`Agregar ${primer.nombre} al carrito`)).toBeInTheDocument()
          }, { timeout: 5000 })

          // Agregar cada producto una vez → cantidad 1 por línea
          for (const p of productos) {
            await user.click(screen.getByLabelText(`Agregar ${p.nombre} al carrito`))
          }

          // Seleccionar cliente si aplica
          if (conCliente) {
            const clienteInput = screen.getByLabelText('Buscar cliente por identificación')
            await user.type(clienteInput, clienteOpt.identificacion)
            await user.click(await screen.findByText(new RegExp(clienteOpt.nombre)))
          }

          await registrarVenta(user)

          await waitFor(() => {
            expect(mockApiPost).toHaveBeenCalledWith('/api/tenants/ventas', expect.any(Object))
          }, { timeout: 5000 })

          const [endpoint, payloadRaw] = mockApiPost.mock.calls[0]!
          const payload = payloadRaw as VentaPayload

          // Endpoint preservado
          expect(endpoint).toBe('/api/tenants/ventas')

          // Forma canónica del payload (persistencia sin cambios)
          expect(payload.sucursalId).toBe(1)
          expect(payload.clienteId).toBe(conCliente ? clienteOpt.id : null)
          expect(payload.tipoComprobante).toBe(tipoComprobante)
          expect(payload.metodoPago).toBe('Efectivo')
          expect(payload.cuotas).toBe(0)
          expect(payload.referenciaTransaccion).toBeNull()

          // Debe usar el campo `lineas` (no `detalles`) y NO `metodo_pago`
          expect(payload).toHaveProperty('lineas')
          expect(payload).not.toHaveProperty('detalles')
          expect(payload).not.toHaveProperty('metodo_pago')

          // Una línea por producto, en orden, con la forma esperada
          expect(payload.lineas).toHaveLength(productos.length)
          productos.forEach((p, i) => {
            const linea = payload.lineas[i]!
            expect(linea.productoId).toBe(p.id)
            expect(linea.cantidad).toBe(1)
            // Sin precio negociado → precioRealCobrado null (preservación de lógica)
            expect(linea.precioRealCobrado).toBeNull()
          })

          // Limpiar temporizadores pendientes antes de desmontar.
          vi.clearAllTimers()
          vi.useRealTimers()
          cleanup()
        },
      ),
      // Al aislar esta propiedad (la más pesada) en su propio proceso, el pico de
      // memoria baja. numRuns=2 conserva el generador multi-input y TODAS las
      // aserciones del payload; generadores y aserciones inalterados.
      { numRuns: 2 },
    )
  }, 90000)
})
