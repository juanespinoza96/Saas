import { render, screen, waitFor, cleanup } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ReportesPage } from './ReportesPage'

// Mock del cliente API: la página solo debe pedir la configuración del plan.
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

import { api } from '../lib/api'
const mockApiGet = vi.mocked(api.get)

/**
 * Regresión de codificación: el literal de comparación del plan debía ser 'Básico'
 * (UTF-8 correcto). Con el texto doblemente codificado, un comercio con plan Básico
 * no se reconocía y la página lanzaba las peticiones a /reportes/*.
 */
describe('ReportesPage - plan Básico', () => {
  beforeEach(() => {
    mockApiGet.mockReset()
  })

  afterEach(() => {
    cleanup()
  })

  it('muestra el aviso de reportes no disponibles y no pide /reportes/*', async () => {
    mockApiGet.mockImplementation((endpoint: string) => {
      if (endpoint === '/api/tenants/configuracion') {
        return Promise.resolve({ planNivel: 'Básico' })
      }
      return Promise.reject(new Error(`Petición inesperada: ${endpoint}`))
    })

    render(<ReportesPage />)

    expect(await screen.findByText(/no están disponibles/i)).toBeInTheDocument()

    // Ninguna petición a los reportes ni a las opciones de filtro.
    await waitFor(() => {
      const endpoints = mockApiGet.mock.calls.map(c => c[0])
      expect(endpoints).toEqual(['/api/tenants/configuracion'])
    })
  })
})
