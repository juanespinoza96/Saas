import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { PosNormalPage } from './PosNormalPage'

// Mock the API module (same pattern as PosBarPage.test.tsx)
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

// Mock del AuthContext para proveer el contexto de autenticación de prueba
// (Variante A, mismo patrón que TicketPrintBugCondition.test.tsx). Se usa un
// factory inline por el hoisting de vi.mock; la forma del valor está alineada
// con AuthContextValue para que PosNormalPage renderice sin lanzar
// "useAuth must be used within an AuthProvider".
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
const mockApiPost = vi.mocked(api.post)

// ─── Test fixtures ────────────────────────────────────────────────────────────

const mockProductos = [
  { id: 1, nombre: 'Coca-Cola 500ml', tipoArticulo: 'Venta Directa', precioLista: 1.50, categoriaId: 1, categoriaNombre: 'Bebidas' },
  { id: 2, nombre: 'Empanada', tipoArticulo: 'Venta Directa', precioLista: 2.00, categoriaId: 2, categoriaNombre: 'Snacks' },
]

const mockSucursales = [{ id: 1, nombre: 'Principal' }]

const configNegociadoEnabled = {
  mostrarBotonCliente: false,
  impresionAutomaticaTicket: false,
  usaFacturacionSRI: false,
  permitePrecioNegociado: true,
  esBarEscolar: false,
}

const configNegociadoDisabled = {
  mostrarBotonCliente: false,
  impresionAutomaticaTicket: false,
  usaFacturacionSRI: false,
  permitePrecioNegociado: false,
  esBarEscolar: false,
}

const configBarEscolar = {
  mostrarBotonCliente: false,
  impresionAutomaticaTicket: false,
  usaFacturacionSRI: false,
  permitePrecioNegociado: true,
  esBarEscolar: true,
}

function setupApiMocks(config: typeof configNegociadoEnabled) {
  mockApiGet.mockImplementation((url: string) => {
    if (url.includes('precios-volumen')) return Promise.resolve([]) as never
    if (url.includes('productos')) return Promise.resolve(mockProductos) as never
    // El endpoint de comprobantes espera un ARRAY; debe evaluarse ANTES que la
    // condición genérica 'configuracion' (evita "comprobantesData.filter is not a function").
    if (url.includes('comprobantes/configuracion'))
      return Promise.resolve([{ tipoComprobante: 'Ticket Digital', habilitado: true }]) as never
    if (url.includes('configuracion')) return Promise.resolve(config) as never
    if (url.includes('sucursales')) return Promise.resolve(mockSucursales) as never
    return Promise.resolve([]) as never
  })
}

async function addProductToCart(productName: string) {
  const addButton = screen.getByLabelText(`Agregar ${productName} al carrito`)
  await userEvent.click(addButton)
}

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('PosNormalPage — Precio Negociado UI & Payload', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    // Mock window.print to prevent actual print dialogs
    vi.spyOn(window, 'print').mockImplementation(() => {})
  })

  describe('UI condicionality (Req 5.1, 5.2, 5.3)', () => {
    it('renders editable price input when toggle=true and barEscolar=false', async () => {
      setupApiMocks(configNegociadoEnabled)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Should show an editable input with the aria-label
      const priceInput = screen.getByLabelText('Precio unitario de Coca-Cola 500ml')
      expect(priceInput).toBeInTheDocument()
      expect(priceInput.tagName).toBe('INPUT')
      expect(priceInput).not.toHaveAttribute('readonly')
      expect(priceInput).not.toBeDisabled()
    })

    it('renders read-only text when toggle=false', async () => {
      setupApiMocks(configNegociadoDisabled)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Should NOT have editable input
      expect(screen.queryByLabelText('Precio unitario de Coca-Cola 500ml')).not.toBeInTheDocument()
      // Should show read-only price text
      expect(screen.getByText('$1.50 c/u')).toBeInTheDocument()
    })

    it('renders read-only text when barEscolar=true (regardless of toggle)', async () => {
      setupApiMocks(configBarEscolar)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Should NOT have editable input even though toggle is true
      expect(screen.queryByLabelText('Precio unitario de Coca-Cola 500ml')).not.toBeInTheDocument()
      // Should show read-only price text
      expect(screen.getByText('$1.50 c/u')).toBeInTheDocument()
    })
  })

  describe('Initial value (Req 5.5)', () => {
    it('editable input shows effective price as initial value', async () => {
      setupApiMocks(configNegociadoEnabled)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      const priceInput = screen.getByLabelText('Precio unitario de Coca-Cola 500ml') as HTMLInputElement
      expect(priceInput.value).toBe('1.5')
    })
  })

  describe('Payload structure (Req 7.1, 7.2)', () => {
    it('uses "lineas" field in the sale payload', async () => {
      setupApiMocks(configNegociadoEnabled)
      mockApiPost.mockResolvedValue(undefined as never)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Click Confirmar Venta
      const confirmBtn = screen.getByRole('button', { name: /Confirmar Venta/i })
      await userEvent.click(confirmBtn)

      // Click Registrar Venta in ConfirmDialog
      const registerBtn = await screen.findByRole('button', { name: /Registrar Venta/i })
      await userEvent.click(registerBtn)

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalledWith(
          '/api/tenants/ventas',
          expect.objectContaining({
            lineas: expect.any(Array),
          }),
        )
      })

      // Verify it does NOT use "detalles"
      const payload = mockApiPost.mock.calls[0]![1] as Record<string, unknown>
      expect(payload).not.toHaveProperty('detalles')
    })

    it('uses "metodoPago" field in the sale payload', async () => {
      setupApiMocks(configNegociadoEnabled)
      mockApiPost.mockResolvedValue(undefined as never)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      const confirmBtn = screen.getByRole('button', { name: /Confirmar Venta/i })
      await userEvent.click(confirmBtn)

      const registerBtn = await screen.findByRole('button', { name: /Registrar Venta/i })
      await userEvent.click(registerBtn)

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalledWith(
          '/api/tenants/ventas',
          expect.objectContaining({
            metodoPago: 'Efectivo',
          }),
        )
      })

      // Verify it does NOT use "metodo_pago"
      const payload = mockApiPost.mock.calls[0]![1] as Record<string, unknown>
      expect(payload).not.toHaveProperty('metodo_pago')
    })
  })

  describe('PrecioRealCobrado logic (Req 5.6, 5.7, 5.8, 5.9)', () => {
    it('sends modified price as precioRealCobrado', async () => {
      setupApiMocks(configNegociadoEnabled)
      mockApiPost.mockResolvedValue(undefined as never)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Modify the price - use tripleClick to select all, then type new value
      const priceInput = screen.getByLabelText('Precio unitario de Coca-Cola 500ml') as HTMLInputElement
      await userEvent.tripleClick(priceInput)
      await userEvent.keyboard('1.25')

      // Confirm sale
      const confirmBtn = screen.getByRole('button', { name: /Confirmar Venta/i })
      await userEvent.click(confirmBtn)

      const registerBtn = await screen.findByRole('button', { name: /Registrar Venta/i })
      await userEvent.click(registerBtn)

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalled()
      })

      const payload = mockApiPost.mock.calls[0]![1] as { lineas: Array<{ precioRealCobrado: number | null }> }
      expect(payload.lineas[0]!.precioRealCobrado).toBe(1.25)
    })

    it('sends null for unmodified price', async () => {
      setupApiMocks(configNegociadoEnabled)
      mockApiPost.mockResolvedValue(undefined as never)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Don't modify the price — just submit
      const confirmBtn = screen.getByRole('button', { name: /Confirmar Venta/i })
      await userEvent.click(confirmBtn)

      const registerBtn = await screen.findByRole('button', { name: /Registrar Venta/i })
      await userEvent.click(registerBtn)

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalled()
      })

      const payload = mockApiPost.mock.calls[0]![1] as { lineas: Array<{ precioRealCobrado: number | null }> }
      expect(payload.lineas[0]!.precioRealCobrado).toBeNull()
    })

    it('sends null when price is reverted to original (return-to-original)', async () => {
      setupApiMocks(configNegociadoEnabled)
      mockApiPost.mockResolvedValue(undefined as never)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Modify the price first
      const priceInput = screen.getByLabelText('Precio unitario de Coca-Cola 500ml') as HTMLInputElement
      await userEvent.tripleClick(priceInput)
      await userEvent.keyboard('2.00')

      // Revert to original value and blur to trigger return-to-original
      await userEvent.tripleClick(priceInput)
      await userEvent.keyboard('1.5')
      await userEvent.tab()

      // Confirm sale
      const confirmBtn = screen.getByRole('button', { name: /Confirmar Venta/i })
      await userEvent.click(confirmBtn)

      const registerBtn = await screen.findByRole('button', { name: /Registrar Venta/i })
      await userEvent.click(registerBtn)

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalled()
      })

      const payload = mockApiPost.mock.calls[0]![1] as { lineas: Array<{ precioRealCobrado: number | null }> }
      expect(payload.lineas[0]!.precioRealCobrado).toBeNull()
    })

    it('rounds precioRealCobrado to 2 decimal places', async () => {
      setupApiMocks(configNegociadoEnabled)
      mockApiPost.mockResolvedValue(undefined as never)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Empanada')).toBeInTheDocument()
      })

      await addProductToCart('Empanada')

      // Type a price that would need rounding (e.g. 1.555 → 1.56)
      const priceInput = screen.getByLabelText('Precio unitario de Empanada') as HTMLInputElement
      await userEvent.tripleClick(priceInput)
      await userEvent.keyboard('1.555')

      // Confirm sale
      const confirmBtn = screen.getByRole('button', { name: /Confirmar Venta/i })
      await userEvent.click(confirmBtn)

      const registerBtn = await screen.findByRole('button', { name: /Registrar Venta/i })
      await userEvent.click(registerBtn)

      await waitFor(() => {
        expect(mockApiPost).toHaveBeenCalled()
      })

      const payload = mockApiPost.mock.calls[0]![1] as { lineas: Array<{ precioRealCobrado: number | null }> }
      // Math.round(1.555 * 100) / 100 = 1.56
      expect(payload.lineas[0]!.precioRealCobrado).toBe(1.56)
    })
  })

  describe('Subtotal recalculation (Req 5.4)', () => {
    it('recalculates cart total when price is modified', async () => {
      setupApiMocks(configNegociadoEnabled)
      render(<PosNormalPage />)

      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      await addProductToCart('Coca-Cola 500ml')

      // Find the total display - it's the span next to the "Total" label
      const totalLabel = screen.getByText('Total')
      const totalContainer = totalLabel.parentElement!
      expect(within(totalContainer).getByText('$1.50')).toBeInTheDocument()

      // Modify the price to 3.00
      const priceInput = screen.getByLabelText('Precio unitario de Coca-Cola 500ml') as HTMLInputElement
      await userEvent.tripleClick(priceInput)
      await userEvent.keyboard('3')

      // Total should update to $3.00
      await waitFor(() => {
        expect(within(totalContainer).getByText('$3.00')).toBeInTheDocument()
      })
    })
  })
})
