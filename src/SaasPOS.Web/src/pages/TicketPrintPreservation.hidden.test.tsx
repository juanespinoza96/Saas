/**
 * Pruebas de PRESERVACIÓN (property-based) — Property 2 — Parte (a)
 * Spec: ticket-digital-pdf-blanco (bugfix)
 *
 * Este archivo contiene ÚNICAMENTE la propiedad (a) del conjunto de preservación.
 * Se separó de TicketPrintPreservation.test.tsx en tres archivos (a/b/c) para que,
 * con `isolate: true` en vitest.config.ts, cada propiedad corra en su propio proceso
 * y libere el heap al terminar. Así se reparte el pico de memoria (que provocaba el
 * OOM "JavaScript heap out of memory" cerca de ~4 GB al ejecutar las tres juntas en
 * un mismo worker). Se conservan EXACTAMENTE los mismos generadores y aserciones.
 *
 * OBJETIVO (a): `#ticket-print-root` permanece OCULTO en la vista de pantalla
 * (clase Tailwind `hidden`), independientemente de la venta registrada. El arreglo
 * del bug es exclusivamente CSS de `@media print`, por lo que esta preservación
 * DEBE PASAR tanto antes como después del arreglo (sin regresiones).
 *
 * NOTA sobre herramientas: jsdom (Vitest) NO evalúa media queries reales ni el
 * layout de `@media print`; el aislamiento visual al imprimir se valida en Playwright.
 *
 * **Validates: Requirements 3.1**
 */
import { render, screen, waitFor, cleanup } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import * as fc from 'fast-check'
import { PosNormalPage } from './PosNormalPage'

// Mock del módulo API (mismo patrón que PosNormalPage.test.tsx / TicketPrintBugCondition.test.tsx)
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
    // Nombre alfanumérico simple para aria-labels estables
    nombre: fc
      .array(
        fc.constantFrom(...'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'.split('')),
        { minLength: 4, maxLength: 16 },
      )
      .map((chars) => chars.join('')),
    tipoArticulo: fc.constantFrom('Venta Directa' as const, 'Ensamblado' as const),
    // Precio en centavos → 2 decimales
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
    // Garantizar ids únicos también
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

// ─── Test de Preservación (a) ───────────────────────────────────────────────

describe('Property 2: Preservation (a) — Ticket oculto en pantalla (clase `hidden`)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Evitar diálogos de impresión reales
    vi.spyOn(window, 'print').mockImplementation(() => {})
  })

  afterEach(() => {
    cleanup()
    vi.restoreAllMocks()
  })

  /**
   * **Validates: Requirements 3.1**
   *
   * Para cualquier venta aleatoria, tras registrarla el ticket se monta con la
   * clase `hidden` y expone `print:block` (activa solo bajo @media print).
   */
  it('property: tras registrar cualquier venta, el ticket conserva la clase `hidden` en pantalla', async () => {
    await fc.assert(
      fc.asyncProperty(productosArb, async (productos) => {
        cleanup()
        vi.clearAllMocks()
        // Fake timers por iteración: al confirmar la venta, PosNormalPage agenda
        // setTimeout(setSuccessMessage(''), 4000) que, con timers reales, quedaría
        // vivo tras el unmount. Con timers falsos los limpiamos al final de cada
        // iteración sin alterar generadores ni aserciones.
        vi.useFakeTimers({ shouldAdvanceTime: true })
        vi.spyOn(window, 'print').mockImplementation(() => {})
        setupApiMocks({ productos, config: baseConfig, tipoComprobante: 'Ticket Digital' })

        render(<PosNormalPage />)
        const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })

        const primer = productos[0]!
        await waitFor(() => {
          expect(screen.getByLabelText(`Agregar ${primer.nombre} al carrito`)).toBeInTheDocument()
        }, { timeout: 5000 })

        await user.click(screen.getByLabelText(`Agregar ${primer.nombre} al carrito`))
        await registrarVenta(user)

        const ticket = await waitFor(() => {
          const el = document.getElementById('ticket-print-root')
          expect(el).not.toBeNull()
          return el as HTMLElement
        }, { timeout: 5000 })

        // Preservación 3.1: la clase `hidden` mantiene el ticket fuera de la vista de pantalla.
        expect(ticket.classList.contains('hidden')).toBe(true)
        // La visibilidad para impresión se apoya en `print:block` (activa solo bajo @media print).
        expect(ticket.classList.contains('print:block')).toBe(true)

        // Limpiar temporizadores pendientes antes de desmontar.
        vi.clearAllTimers()
        vi.useRealTimers()
        cleanup()
      }),
      // Al aislar esta propiedad en su propio archivo/proceso, el pico de memoria
      // baja y se recupera cobertura respecto al mínimo previo: numRuns=3 conserva
      // los mismos generadores y aserciones.
      { numRuns: 3 },
    )
  }, 45000)
})
