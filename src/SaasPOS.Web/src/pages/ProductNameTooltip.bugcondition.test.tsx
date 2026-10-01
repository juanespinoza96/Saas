/**
 * Test de exploración de Bug Condition — Property 1
 * 
 * Verifica que los elementos <span> que renderizan producto.nombre en PosNormalPage
 * y PosBarPage tienen el atributo `title` igual a producto.nombre.
 * 
 * ESPERADO: Este test DEBE FALLAR en código sin corregir (confirma que el bug existe).
 * Cuando se implemente el fix (agregar title={producto.nombre}), el test pasará.
 * 
 * **Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.2, 2.3**
 */
import { render, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import * as fc from 'fast-check'
import { PosNormalPage } from './PosNormalPage'
import { PosBarPage } from './PosBarPage'

// Mock del módulo API
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

// Mock del AuthContext para que PosNormalPage/PosBarPage se monten con contexto
// de autenticación en cada iteración de fast-check sin lanzar
// "useAuth must be used within an AuthProvider". Se usa una factory inline
// (mismo patrón validado en TicketPrintBugCondition.test.tsx) porque vi.mock se
// eleva al top del archivo y no puede referenciar importaciones de nivel superior.
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

import { api } from '../lib/api'
const mockApiGet = vi.mocked(api.get)

// ─── Generadores de datos ─────────────────────────────────────────────────────

// Generador de nombres de producto con al menos 1 carácter no-whitespace
// (nombres puramente de whitespace no son nombres de producto realistas)
const productoNombreArb = fc.oneof(
  // Nombres cortos (1-10 chars) con al menos un carácter visible
  fc.string({ minLength: 1, maxLength: 10 }).filter(s => s.trim().length > 0),
  // Nombres medianos (11-30 chars) con al menos un carácter visible
  fc.string({ minLength: 11, maxLength: 30 }).filter(s => s.trim().length > 0),
  // Nombres largos que se truncarían (31-80 chars) con al menos un carácter visible
  fc.string({ minLength: 31, maxLength: 80 }).filter(s => s.trim().length > 0),
  // Nombres realistas con espacios
  fc.array(fc.constantFrom(
    'Almuerzo', 'Escolar', 'Lunes', 'Martes', 'Sopa', 'Verduras',
    'Combo', 'Especial', 'Estudiantil', 'Jugo', 'Natural', 'Postre',
    'Empanada', 'Queso', 'Carne', 'Pollo', 'Arroz', 'con', 'de', 'y',
  ), { minLength: 2, maxLength: 8 }).map(words => words.join(' ')),
)

// Generador de un producto completo
const productoArb = fc.record({
  id: fc.integer({ min: 1, max: 9999 }),
  nombre: productoNombreArb,
  tipoArticulo: fc.constantFrom('Venta Directa' as const, 'Ensamblado' as const),
  precioLista: fc.float({ min: Math.fround(0.01), max: Math.fround(999.99), noNaN: true }),
  categoriaId: fc.option(fc.integer({ min: 1, max: 100 }), { nil: null }),
  categoriaNombre: fc.option(
    fc.constantFrom('Bebidas', 'Snacks', 'Almuerzos', 'Postres'),
    { nil: null },
  ),
})

// ─── Configuración de mocks ───────────────────────────────────────────────────

const mockSucursales = [{ id: 1, nombre: 'Principal' }]

const configNormal = {
  mostrarBotonCliente: false,
  impresionAutomaticaTicket: false,
  usaFacturacionSRI: false,
  permitePrecioNegociado: false,
  esBarEscolar: false,
}

const configBarEscolar = {
  esBarEscolar: true,
}

function setupMocksPosNormal(productos: unknown[]) {
  mockApiGet.mockImplementation((url: string) => {
    if (url.includes('precios-volumen')) return Promise.resolve({}) as never
    if (url.includes('productos')) return Promise.resolve(productos) as never
    // El endpoint de comprobantes espera un ARRAY; debe evaluarse ANTES que la
    // condición genérica 'configuracion' para no recibir el objeto de config general
    // (evita "comprobantesData.filter is not a function" en PosNormalPage).
    if (url.includes('comprobantes/configuracion'))
      return Promise.resolve([{ tipoComprobante: 'Ticket Digital', habilitado: true }]) as never
    if (url.includes('configuracion')) return Promise.resolve(configNormal) as never
    if (url.includes('sucursales')) return Promise.resolve(mockSucursales) as never
    return Promise.resolve([]) as never
  })
}

function setupMocksPosBar(productos: unknown[]) {
  mockApiGet.mockImplementation((url: string) => {
    if (url.includes('productos')) return Promise.resolve(productos) as never
    // El endpoint de comprobantes espera un ARRAY; se enruta antes que la genérica.
    if (url.includes('comprobantes/configuracion'))
      return Promise.resolve([{ tipoComprobante: 'Ticket Digital', habilitado: true }]) as never
    if (url.includes('configuracion')) return Promise.resolve(configBarEscolar) as never
    if (url.includes('sucursales')) return Promise.resolve(mockSucursales) as never
    if (url.includes('ventas')) return Promise.resolve([]) as never
    return Promise.resolve([]) as never
  })
}

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('Bug Condition Exploration — Product Name Tooltip', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('PosNormalPage — span[title=producto.nombre] present', () => {
    it('para cualquier producto renderizado, el span del nombre tiene title={producto.nombre}', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          vi.clearAllMocks()

          const productos = [producto]
          setupMocksPosNormal(productos)

          const { container, unmount } = render(<PosNormalPage />)

          // Esperar a que se renderice al menos un span con atributo title
          // (o un span con clase truncate que muestre el nombre)
          await waitFor(() => {
            const spans = container.querySelectorAll('span[title]')
            // Si no hay spans con title, buscar que al menos el producto se cargó
            // verificando que existe un botón de producto en el DOM
            if (spans.length === 0) {
              const buttons = container.querySelectorAll('button[aria-label]')
              expect(buttons.length).toBeGreaterThan(0)
            }
          })

          // Buscar todos los spans con atributo title en el contenedor
          const spansWithTitle = container.querySelectorAll('span[title]')

          // PROPIEDAD: Debe existir al menos un span con title igual a producto.nombre
          const matchingSpan = Array.from(spansWithTitle).find(
            span => span.getAttribute('title') === producto.nombre
          )
          expect(matchingSpan).toBeDefined()
          expect(matchingSpan?.getAttribute('title')).toBe(producto.nombre)

          unmount()
        }),
        // Acotado (20→8) para reducir el pico de memoria por worker: cada iteración
        // monta PosNormalPage y hace unmount(). Se conserva el generador productoArb
        // y las aserciones (multi-input preservado).
        { numRuns: 8 },
      )
    }, 30000)
  })

  describe('PosBarPage — span[title=producto.nombre] present', () => {
    it('para cualquier producto renderizado, el span del nombre tiene title={producto.nombre}', async () => {
      await fc.assert(
        fc.asyncProperty(productoArb, async (producto) => {
          vi.clearAllMocks()

          const productos = [producto]
          setupMocksPosBar(productos)

          const { container, unmount } = render(<PosBarPage />)

          // Esperar a que se renderice al menos un span con atributo title
          // (o un botón de producto que indique que la carga terminó)
          await waitFor(() => {
            const spans = container.querySelectorAll('span[title]')
            if (spans.length === 0) {
              const buttons = container.querySelectorAll('button[aria-label]')
              expect(buttons.length).toBeGreaterThan(0)
            }
          })

          // Buscar todos los spans con atributo title en el contenedor
          const spansWithTitle = container.querySelectorAll('span[title]')

          // PROPIEDAD: Debe existir al menos un span con title igual a producto.nombre
          const matchingSpan = Array.from(spansWithTitle).find(
            span => span.getAttribute('title') === producto.nombre
          )
          expect(matchingSpan).toBeDefined()
          expect(matchingSpan?.getAttribute('title')).toBe(producto.nombre)

          unmount()
        }),
        // Acotado (20→8) para reducir el pico de memoria por worker: cada iteración
        // monta PosBarPage y hace unmount(). Se conserva el generador productoArb
        // y las aserciones (multi-input preservado).
        { numRuns: 8 },
      )
    }, 30000)
  })
})
