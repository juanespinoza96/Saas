/**
 * Prueba exploratoria de la Condición del Bug — Property 1 (parte estructural, Vitest)
 * Spec: ticket-digital-pdf-blanco (bugfix)
 *
 * OBJETIVO: Exponer la precondición estructural de la causa raíz y afirmar el
 * comportamiento esperado (Property 1): tras registrar una venta, el ticket
 * `#ticket-print-root` se monta anidado dentro de `#root` (hijo directo de <body>)
 * y su contenido completo está presente en el DOM.
 *
 * NOTA sobre herramientas: jsdom (Vitest) NO evalúa media queries reales ni calcula
 * el layout de `@media print`, por lo que la validación fuerte de la VISIBILIDAD
 * impresa se realiza en la prueba E2E de Playwright (ticket-print.bugcondition.spec.ts,
 * usando `page.emulateMedia({ media: 'print' })`). Aquí verificamos:
 *   (a) la precondición estructural del bug: `#ticket-print-root` es descendiente de `#root`.
 *   (b) el contenido completo del ticket esperado en el resultado impreso (Expected Behavior).
 *
 * ALCANCE DE ESTA PRUEBA (Vitest/jsdom): dado que jsdom NO aplica `print.css`
 * ni evalúa `@media print`, aquí NO se valida la visibilidad efectiva impresa
 * (eso lo hace la prueba E2E de Playwright, que sí evalúa el CSS real). Esta
 * prueba verifica de forma fiable en jsdom:
 *   (a) el contenido completo del ticket esperado tras registrar la venta
 *       (Expected Behavior: encabezado, cliente/CONSUMIDOR FINAL, items, total,
 *       comprobante, método de pago, fecha, mensaje final).
 *   (b) la precondición estructural de la causa raíz: `#ticket-print-root` es
 *       descendiente de `#root`, hijo directo de `<body>`.
 *
 * **Validates: Requirements 1.1, 1.2, 1.3**
 */
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { PosNormalPage } from './PosNormalPage'

// Mock del módulo API (mismo patrón que PosNormalPage.test.tsx)
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

// ─── Fixtures ───────────────────────────────────────────────────────────────

const mockProductos = [
  { id: 1, nombre: 'Coca-Cola 500ml', tipoArticulo: 'Venta Directa', precioLista: 1.5, categoriaId: 1, categoriaNombre: 'Bebidas' },
  { id: 2, nombre: 'Empanada', tipoArticulo: 'Venta Directa', precioLista: 2.0, categoriaId: 2, categoriaNombre: 'Snacks' },
]

const mockSucursales = [{ id: 1, nombre: 'Principal' }]

// Configuración base: sin cliente visible, impresión manual
const configConsumidorFinal = {
  mostrarBotonCliente: false,
  impresionAutomaticaTicket: false,
  usaFacturacionSRI: false,
  permitePrecioNegociado: false,
  esBarEscolar: false,
}

// Configuración con cliente visible
const configConCliente = {
  ...configConsumidorFinal,
  mostrarBotonCliente: true,
}

// Configuración con impresión automática activada (Req 1.3 / 2.3)
const configImpresionAutomatica = {
  ...configConsumidorFinal,
  impresionAutomaticaTicket: true,
}

const mockCliente = {
  id: 10,
  identificacion: '1712345678',
  nombre: 'Juan Pérez',
  correo: null,
}

function setupApiMocks(config: typeof configConsumidorFinal, clientes: unknown[] = []) {
  mockApiGet.mockImplementation((url: string) => {
    if (url.includes('precios-volumen')) return Promise.resolve({}) as never
    if (url.includes('comprobantes/configuracion'))
      return Promise.resolve([{ tipoComprobante: 'Ticket Digital', habilitado: true }]) as never
    if (url.includes('productos')) return Promise.resolve(mockProductos) as never
    if (url.includes('configuracion')) return Promise.resolve(config) as never
    if (url.includes('sucursales')) return Promise.resolve(mockSucursales) as never
    if (url.includes('clientes')) return Promise.resolve({ items: clientes, total: clientes.length }) as never
    return Promise.resolve([]) as never
  })
  mockApiPost.mockResolvedValue({} as never)
}

async function addProducto(nombre: string) {
  await userEvent.click(screen.getByLabelText(`Agregar ${nombre} al carrito`))
}

// Registra una venta: abre el diálogo de confirmación y pulsa "Registrar Venta"
async function registrarVenta() {
  await userEvent.click(screen.getByRole('button', { name: /confirmar venta/i }))
  await userEvent.click(screen.getByRole('button', { name: /registrar venta/i }))
}

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('Bug Condition Exploration — Ticket visible al imprimir (Property 1)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Evitar diálogos de impresión reales
    vi.spyOn(window, 'print').mockImplementation(() => {})
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('Caso 1 — Impresión manual, consumidor final: ticket visible con "CONSUMIDOR FINAL" y total', async () => {
    setupApiMocks(configConsumidorFinal)
    render(<PosNormalPage />)

    await waitFor(() => {
      expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
    })

    await addProducto('Coca-Cola 500ml')
    await registrarVenta()

    // El ticket debe existir en el DOM tras la venta
    const ticket = await waitFor(() => {
      const el = document.getElementById('ticket-print-root')
      expect(el).not.toBeNull()
      return el as HTMLElement
    })

    // Contenido esperado del ticket (Expected Behavior)
    const t = within(ticket)
    expect(t.getByText('Comercio Demo')).toBeInTheDocument()
    expect(t.getByText('CONSUMIDOR FINAL')).toBeInTheDocument()
    expect(t.getByText('Coca-Cola 500ml')).toBeInTheDocument()
    expect(t.getByText('TOTAL:')).toBeInTheDocument()
    expect(t.getByText('$1.50')).toBeInTheDocument()
    expect(t.getByText(/Comprobante:/)).toBeInTheDocument()
    expect(t.getByText(/Método de Pago:/)).toBeInTheDocument()
    expect(t.getByText(/Fecha:/)).toBeInTheDocument()
    expect(t.getByText('¡Gracias por su compra!')).toBeInTheDocument()

    // PROPIEDAD (Property 1) — Expected Behavior verificable en jsdom:
    // el ticket completo está presente en el DOM tras la venta. La visibilidad
    // efectiva bajo @media print se valida en la prueba E2E de Playwright.
    expect(ticket).toBeInTheDocument()
  }, 30000)

  it('Caso 2 — Impresión manual, con cliente: ticket visible con nombre e identificación', async () => {
    setupApiMocks(configConCliente, [mockCliente])
    render(<PosNormalPage />)

    await waitFor(() => {
      expect(screen.getByText('Empanada')).toBeInTheDocument()
    })

    await addProducto('Empanada')

    // Seleccionar cliente
    await userEvent.type(
      screen.getByLabelText('Buscar cliente por identificación'),
      '1712345678',
    )
    await userEvent.click(await screen.findByText(/Juan Pérez/))

    await registrarVenta()

    const ticket = await waitFor(() => {
      const el = document.getElementById('ticket-print-root')
      expect(el).not.toBeNull()
      return el as HTMLElement
    })

    const t = within(ticket)
    expect(t.getByText(/Juan Pérez/)).toBeInTheDocument()
    expect(t.getByText(/1712345678/)).toBeInTheDocument()
    expect(t.getByText('Empanada')).toBeInTheDocument()
    expect(t.getByText('TOTAL:')).toBeInTheDocument()

    // PROPIEDAD (Property 1) — Expected Behavior verificable en jsdom:
    // el ticket con los datos del cliente está presente en el DOM. La visibilidad
    // efectiva bajo @media print se valida en la prueba E2E de Playwright.
    expect(ticket).toBeInTheDocument()
  }, 30000)

  it('Caso 3 — Impresión automática (impresionAutomaticaTicket=true): ticket visible', async () => {
    setupApiMocks(configImpresionAutomatica)
    render(<PosNormalPage />)

    await waitFor(() => {
      expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
    })

    await addProducto('Coca-Cola 500ml')
    await registrarVenta()

    const ticket = await waitFor(() => {
      const el = document.getElementById('ticket-print-root')
      expect(el).not.toBeNull()
      return el as HTMLElement
    })

    // Confirma que la impresión automática se disparó
    await waitFor(() => {
      expect(window.print).toHaveBeenCalled()
    })

    // PROPIEDAD (Property 1) — Expected Behavior verificable en jsdom:
    // tras la impresión automática, el ticket está presente en el DOM. La
    // visibilidad efectiva bajo @media print se valida en la prueba E2E de Playwright.
    expect(ticket).toBeInTheDocument()
  }, 30000)

  it('Caso 4 — Estructura del DOM: #ticket-print-root está anidado dentro de #root (precondición de la causa raíz)', async () => {
    // Montamos la app dentro de un contenedor con id="root" para reproducir el
    // punto de montaje real (index.html: <div id="root"> como único hijo de <body>).
    const root = document.createElement('div')
    root.id = 'root'
    document.body.appendChild(root)

    setupApiMocks(configConsumidorFinal)
    render(<PosNormalPage />, { container: root })

    await waitFor(() => {
      expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
    })

    await addProducto('Coca-Cola 500ml')
    await registrarVenta()

    const ticket = await waitFor(() => {
      const el = document.getElementById('ticket-print-root')
      expect(el).not.toBeNull()
      return el as HTMLElement
    })

    const rootEl = document.getElementById('root') as HTMLElement

    // Precondición estructural del bug: el ticket es descendiente de #root,
    // y #root es hijo directo de <body>. Documenta por qué la regla actual
    // (que oculta los hijos directos de <body>) oculta al ticket.
    expect(rootEl.contains(ticket)).toBe(true)
    expect(rootEl.parentElement).toBe(document.body)
    expect(ticket.id).toBe('ticket-print-root')
    expect(ticket).not.toBe(rootEl) // el ticket NO es hijo directo de <body>

    document.body.removeChild(root)
  }, 30000)
})
