/**
 * Prueba de exploración de la Bug Condition (Property 1: Fix Checking) para el
 * bugfix `pos-comprobantes-403-consola`.
 *
 * Validates: Requirements 1.1, 1.2 (bug analysis) — codifica el comportamiento
 * esperado descrito en las Correctness Properties del design (Property 1).
 *
 * CRÍTICO (metodología bug condition):
 *   - Esta prueba DEBE FALLAR sobre el código SIN arreglar. La falla CONFIRMA
 *     que el bug existe: `PosNormalPage` dispara el GET a
 *     `/api/tenants/comprobantes/configuracion` de forma incondicional, incluso
 *     para roles que el backend rechaza con 403.
 *   - Cuando el arreglo esté implementado (guarda por rol con `hasRole`), esta
 *     MISMA prueba debe PASAR (tarea 3.2), confirmando que no hay request 403
 *     evitable para roles no autorizados.
 *
 * Bug Condition (del design):
 *   isBugCondition(carga) := carga.rol <> 'Dueño' AND carga.rol <> 'Gerente'
 *
 * Property 1 — Fix Checking: para todo rol donde isBugCondition es verdadero
 *   (incluido el caso de claims null / rol indeterminado):
 *     - `api.get('/api/tenants/comprobantes/configuracion')` NO es invocado, y
 *     - `tiposComprobanteHabilitados === ['Ticket Digital']` y
 *       `tipoComprobante === 'Ticket Digital'`, y
 *     - la página termina de cargar sin errorMessage por comprobantes.
 */
import { render, screen, waitFor, cleanup } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import type { UserRole } from '../types/auth'

// ─── Mocks ────────────────────────────────────────────────────────────────────

// Mock del cliente HTTP para observar/controlar las invocaciones a `api.get`.
vi.mock('../lib/api', () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

// Variable de módulo mutable que representa el rol del usuario autenticado para
// el caso de prueba en curso. `null` modela el rol indeterminado (claims null).
// Debe declararse con prefijo `mock` para que `vi.mock` (hoisted) pueda
// referenciarla sin violar las reglas de hoisting de Vitest.
let mockCurrentRole: UserRole | null = null

// Mock del AuthContext replicando la semántica REAL de `hasRole`:
//   - devuelve `false` cuando no hay claims (rol indeterminado), y
//   - en otro caso, verifica si el rol actual está incluido en los solicitados.
// Así la prueba controla el rol por caso vía `mockCurrentRole`.
vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => ({
    token: null,
    claims: mockCurrentRole ? { role: mockCurrentRole } : null,
    isAuthenticated: true,
    comercioNombre: 'Comercio Demo',
    login: vi.fn(),
    logout: vi.fn(),
    hasRole: (role: UserRole | UserRole[]) => {
      if (!mockCurrentRole) return false
      const roles = Array.isArray(role) ? role : [role]
      return roles.includes(mockCurrentRole)
    },
  }),
}))

// Evitar diálogos de impresión reales durante el montaje de PosNormalPage.
vi.stubGlobal('print', vi.fn())

import { api } from '../lib/api'
import { PosNormalPage } from './PosNormalPage'

const mockApiGet = vi.mocked(api.get)

// ─── Fixtures ───────────────────────────────────────────────────────────────

const ENDPOINT_COMPROBANTES = '/api/tenants/comprobantes/configuracion'

const mockProductos = [
  {
    id: 1,
    nombre: 'Coca-Cola 500ml',
    tipoArticulo: 'Venta Directa',
    precioLista: 1.5,
    categoriaId: 1,
    categoriaNombre: 'Bebidas',
  },
]

const mockConfig = {
  mostrarBotonCliente: false,
  impresionAutomaticaTicket: false,
  usaFacturacionSRI: false,
  permitePrecioNegociado: false,
  esBarEscolar: false,
}

const mockSucursales = [{ id: 1, nombre: 'Principal' }]

// Respuesta backend de comprobantes que se devolvería SI se dispara el GET.
// Contiene tipos distintos del default para poder distinguir el estado
// derivado de la respuesta del estado por defecto (`Ticket Digital`).
const mockComprobantesResponse = [
  { tipoComprobante: 'Factura', habilitado: true },
  { tipoComprobante: 'NotaVenta', habilitado: true },
]

/**
 * Configura `api.get` de modo que, si `PosNormalPage` invoca el endpoint de
 * comprobantes, devuelva una configuración con tipos distintos al default.
 * Esto permite que la aserción de estado (`['Ticket Digital']`) falle cuando la
 * petición SÍ se dispara (bug presente), y pase cuando NO se dispara (arreglado).
 */
function setupApiMocks() {
  mockApiGet.mockImplementation(((endpoint: string) => {
    if (endpoint.includes('precios-volumen')) return Promise.resolve({})
    if (endpoint === ENDPOINT_COMPROBANTES)
      return Promise.resolve(mockComprobantesResponse)
    if (endpoint.includes('productos')) return Promise.resolve(mockProductos)
    if (endpoint.includes('sucursales')) return Promise.resolve(mockSucursales)
    if (endpoint.includes('configuracion')) return Promise.resolve(mockConfig)
    return Promise.resolve([])
  }) as typeof api.get)
}

/** Indica si `api.get` fue invocado con el endpoint de comprobantes. */
function seLlamoEndpointComprobantes(): boolean {
  return mockApiGet.mock.calls.some((call) => call[0] === ENDPOINT_COMPROBANTES)
}

/**
 * Devuelve los tipos de comprobante que muestra el `<select>` de la UI. Ese
 * selector se puebla desde `tiposComprobanteHabilitados`, por lo que refleja el
 * estado derivado sin acoplarse a la implementación interna.
 */
function tiposComprobanteEnUI(): string[] {
  const select = screen.getByLabelText('Tipo de comprobante') as HTMLSelectElement
  return Array.from(select.options).map((opt) => opt.value)
}

/** Valor por defecto seleccionado en el `<select>` de tipo de comprobante. */
function tipoComprobanteSeleccionado(): string {
  const select = screen.getByLabelText('Tipo de comprobante') as HTMLSelectElement
  return select.value
}

// ─── Roles bajo prueba (Bug Condition) ────────────────────────────────────────

// Conjunto donde isBugCondition es verdadero: roles no autorizados por backend
// más el caso de rol indeterminado (claims null). Se parametriza la propiedad
// sobre este conjunto (Scoped PBT).
const casosBugCondition: { descripcion: string; rol: UserRole | null }[] = [
  { descripcion: 'Cajero', rol: 'Cajero' },
  { descripcion: 'Supervisor', rol: 'Supervisor' },
  { descripcion: 'Bodeguero', rol: 'Bodeguero' },
  { descripcion: 'rol indeterminado (claims null)', rol: null },
]

// ─── Tests ────────────────────────────────────────────────────────────────────

describe('Property 1: Fix Checking — sin request 403 evitable para roles no autorizados', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setupApiMocks()
  })

  afterEach(() => {
    cleanup()
  })

  // FOR ALL carga WHERE isBugCondition(carga) DO ... (parametrizado)
  it.each(casosBugCondition)(
    'con rol $descripcion no dispara el GET de comprobantes y aplica el default',
    async ({ rol }) => {
      // Configurar el rol del caso (controla `hasRole` en el mock de AuthContext).
      mockCurrentRole = rol

      render(<PosNormalPage />)

      // Esperar a que termine la carga inicial (la página deja de mostrar el
      // estado "Cargando punto de venta...").
      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      // (1) El endpoint de comprobantes NO debe haberse invocado.
      expect(seLlamoEndpointComprobantes()).toBe(false)

      // (2) El estado derivado debe ser exactamente el default.
      expect(tiposComprobanteEnUI()).toEqual(['Ticket Digital'])
      expect(tipoComprobanteSeleccionado()).toBe('Ticket Digital')

      // (3) La página carga sin errorMessage provocado por comprobantes.
      expect(
        screen.queryByText('Error al cargar datos iniciales.'),
      ).not.toBeInTheDocument()
    },
  )
})

// ─── Roles autorizados (Preservación) ─────────────────────────────────────────

// Conjunto donde isBugCondition es FALSO: roles que el backend SÍ autoriza a la
// configuración de comprobantes. Sobre este conjunto se parametrizan las
// pruebas de preservación (Property 2 y Property 3 del design).
const rolesAutorizados: UserRole[] = ['Dueño', 'Gerente']

/**
 * Configura `api.get` como en `setupApiMocks` PERO haciendo que el endpoint de
 * comprobantes RECHACE (simulando un error 500 / de red). Se usa para la
 * salvaguarda de error (Property 3): el `try/catch` de la rama autorizada debe
 * aplicar el default `Ticket Digital` sin romper la carga.
 */
function setupApiMocksConComprobantesFallando() {
  mockApiGet.mockImplementation(((endpoint: string) => {
    if (endpoint.includes('precios-volumen')) return Promise.resolve({})
    if (endpoint === ENDPOINT_COMPROBANTES)
      return Promise.reject({ status: 500, message: 'Internal Server Error' })
    if (endpoint.includes('productos')) return Promise.resolve(mockProductos)
    if (endpoint.includes('sucursales')) return Promise.resolve(mockSucursales)
    if (endpoint.includes('configuracion')) return Promise.resolve(mockConfig)
    return Promise.resolve([])
  }) as typeof api.get)
}

/** Indica si `api.get` fue invocado con el endpoint de productos. */
function seLlamoEndpointProductos(): boolean {
  return mockApiGet.mock.calls.some((call) => call[0] === '/api/tenants/productos')
}

/** Indica si `api.get` fue invocado con el endpoint de configuración del comercio. */
function seLlamoEndpointConfigComercio(): boolean {
  return mockApiGet.mock.calls.some((call) => call[0] === '/api/tenants/configuracion')
}

/** Indica si `api.get` fue invocado con el endpoint de sucursales. */
function seLlamoEndpointSucursales(): boolean {
  return mockApiGet.mock.calls.some((call) => call[0] === '/api/tenants/sucursales')
}

/** Indica si `api.get` fue invocado con el endpoint de precios por volumen. */
function seLlamoEndpointPreciosVolumen(): boolean {
  return mockApiGet.mock.calls.some(
    (call) => call[0] === '/api/tenants/productos/precios-volumen',
  )
}

describe('Property 2: Preservación — roles autorizados (Dueño/Gerente) sin cambios', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setupApiMocks()
  })

  afterEach(() => {
    cleanup()
  })

  // FOR ALL rol ∈ {Dueño, Gerente} DO ... (parametrizado)
  it.each(rolesAutorizados)(
    'con rol %s SÍ dispara el GET de comprobantes y deriva los tipos de la respuesta',
    async (rol) => {
      // Configurar el rol autorizado del caso.
      mockCurrentRole = rol

      render(<PosNormalPage />)

      // Esperar a que termine la carga inicial.
      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      // (1) El endpoint de comprobantes SÍ debe haberse invocado.
      expect(seLlamoEndpointComprobantes()).toBe(true)

      // (2) Los tipos habilitados derivan de la respuesta del backend
      // (Factura, NotaVenta) — NO del default `Ticket Digital`.
      expect(tiposComprobanteEnUI()).toEqual(['Factura', 'NotaVenta'])

      // (3) El primer tipo habilitado queda como valor por defecto.
      expect(tipoComprobanteSeleccionado()).toBe('Factura')

      // (4) La página carga sin errorMessage.
      expect(
        screen.queryByText('Error al cargar datos iniciales.'),
      ).not.toBeInTheDocument()
    },
  )
})

describe('Property 3: Salvaguarda de error — rol autorizado con GET de comprobantes fallando', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setupApiMocksConComprobantesFallando()
  })

  afterEach(() => {
    cleanup()
  })

  // FOR ALL rol ∈ {Dueño, Gerente} donde api.get falla (500/red) DO ...
  it.each(rolesAutorizados)(
    'con rol %s y comprobantes fallando aplica el default y la página no se rompe',
    async (rol) => {
      mockCurrentRole = rol

      render(<PosNormalPage />)

      // Esperar a que termine la carga inicial (la página no se rompe).
      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      // (1) Se intentó el GET de comprobantes (rol autorizado).
      expect(seLlamoEndpointComprobantes()).toBe(true)

      // (2) Ante el fallo, el catch aplica el default `Ticket Digital`.
      expect(tiposComprobanteEnUI()).toEqual(['Ticket Digital'])
      expect(tipoComprobanteSeleccionado()).toBe('Ticket Digital')

      // (3) La página carga sin errorMessage (la salvaguarda evita romperla).
      expect(
        screen.queryByText('Error al cargar datos iniciales.'),
      ).not.toBeInTheDocument()
    },
  )
})

describe('Preservación (regresión) — el resto de datos iniciales carga igual para todo rol', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setupApiMocks()
  })

  afterEach(() => {
    cleanup()
  })

  // Independientemente del rol (autorizado o no), los demás datos iniciales
  // (productos, config del comercio, sucursales, precios por volumen) deben
  // cargarse igual. Se parametriza sobre roles autorizados y no autorizados.
  const todosLosRoles: { descripcion: string; rol: UserRole | null }[] = [
    { descripcion: 'Dueño', rol: 'Dueño' },
    { descripcion: 'Gerente', rol: 'Gerente' },
    { descripcion: 'Cajero', rol: 'Cajero' },
    { descripcion: 'Supervisor', rol: 'Supervisor' },
    { descripcion: 'Bodeguero', rol: 'Bodeguero' },
    { descripcion: 'rol indeterminado (claims null)', rol: null },
  ]

  it.each(todosLosRoles)(
    'con rol $descripcion carga productos, config, sucursales y precios por volumen',
    async ({ rol }) => {
      mockCurrentRole = rol

      render(<PosNormalPage />)

      // Esperar a que termine la carga inicial (productos renderizados).
      await waitFor(() => {
        expect(screen.getByText('Coca-Cola 500ml')).toBeInTheDocument()
      })

      // Los cuatro endpoints de datos iniciales se invocan siempre, sin importar el rol.
      expect(seLlamoEndpointProductos()).toBe(true)
      expect(seLlamoEndpointConfigComercio()).toBe(true)
      expect(seLlamoEndpointSucursales()).toBe(true)
      expect(seLlamoEndpointPreciosVolumen()).toBe(true)

      // La página carga sin errorMessage.
      expect(
        screen.queryByText('Error al cargar datos iniciales.'),
      ).not.toBeInTheDocument()
    },
  )
})
