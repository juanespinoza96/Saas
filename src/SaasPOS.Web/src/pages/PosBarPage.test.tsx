import { render, screen, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'

// Mock del AuthContext para proveer `comercioNombre` y una sesión autenticada
// sin necesidad del provider real. Debe declararse ANTES del import de
// `PosBarPage` por el hoisting de `vi.mock`. Se replica el factory inline (misma
// forma de valor que el helper `createAuthContextMock`) porque el hoisting no
// permite referenciar un import estático dentro del factory.
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

import { PosBarPage } from './PosBarPage'

// Mock the API module
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

import { api } from '../lib/api'
const mockApiGet = vi.mocked(api.get)
const mockApiPost = vi.mocked(api.post)

const mockProductos = [
  { id: 1, nombre: 'Empanada', tipoArticulo: 'Venta Directa', precioLista: 1.5, categoriaId: 1, categoriaNombre: 'Snacks' },
  { id: 2, nombre: 'Jugo de Naranja', tipoArticulo: 'Venta Directa', precioLista: 2.0, categoriaId: 2, categoriaNombre: 'Bebidas' },
]

const mockSucursales = [{ id: 1, nombre: 'Principal' }]

describe('PosBarPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders product grid when EsBarEscolar=TRUE', async () => {
    mockApiGet.mockImplementation((url: string) => {
      if (url.includes('productos')) return Promise.resolve(mockProductos) as never
      if (url.includes('configuracion')) return Promise.resolve({ esBarEscolar: true }) as never
      if (url.includes('sucursales')) return Promise.resolve(mockSucursales) as never
      return Promise.resolve([]) as never
    })

    render(<PosBarPage />)

    // Wait for products to load and display
    await waitFor(() => {
      expect(screen.getByText('Empanada')).toBeInTheDocument()
      expect(screen.getByText('Jugo de Naranja')).toBeInTheDocument()
    })

    // Title should be visible
    expect(screen.getByText('Bar Escolar')).toBeInTheDocument()
  })

  it('shows "not available" message when EsBarEscolar=FALSE', async () => {
    mockApiGet.mockImplementation((url: string) => {
      if (url.includes('productos')) return Promise.resolve(mockProductos) as never
      if (url.includes('configuracion')) return Promise.resolve({ esBarEscolar: false }) as never
      if (url.includes('sucursales')) return Promise.resolve(mockSucursales) as never
      return Promise.resolve([]) as never
    })

    render(<PosBarPage />)

    await waitFor(() => {
      expect(screen.getByText('Modo Bar Escolar no disponible')).toBeInTheDocument()
    })

    // Products should NOT be rendered
    expect(screen.queryByText('Empanada')).not.toBeInTheDocument()
  })

  it('shows success toast after clicking a product', async () => {
    mockApiGet.mockImplementation((url: string) => {
      if (url.includes('productos')) return Promise.resolve(mockProductos) as never
      if (url.includes('configuracion')) return Promise.resolve({ esBarEscolar: true }) as never
      if (url.includes('sucursales')) return Promise.resolve(mockSucursales) as never
      return Promise.resolve([]) as never
    })
    mockApiPost.mockResolvedValue(undefined as never)

    render(<PosBarPage />)

    // Wait for products to load
    await waitFor(() => {
      expect(screen.getByText('Empanada')).toBeInTheDocument()
    })

    // Click a product
    const productButton = screen.getByLabelText(/Vender Empanada/i)
    productButton.click()

    // Verify toast appears
    await waitFor(() => {
      expect(screen.getByText(/✓ Empanada/)).toBeInTheDocument()
    })
  })
})
