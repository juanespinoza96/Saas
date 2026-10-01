import { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import { api } from '../lib/api'
import { Button } from '../components/ui/Button'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { Modal } from '../components/ui/Modal'
import { PaymentMethodSelector, type MetodoPago, type CuotasValidas } from '../components/pos/PaymentMethodSelector'
import { InstallmentSummary } from '../components/pos/InstallmentSummary'
import { TicketPrint } from '../components/pos/TicketPrint'
import { useAuth } from '../contexts/AuthContext'

// ─── Types ────────────────────────────────────────────────────────────────────

interface Producto {
  id: number
  nombre: string
  tipoArticulo: 'Venta Directa' | 'Ensamblado'
  precioLista: number
  categoriaId: number | null
  categoriaNombre: string | null
}

interface PrecioVolumen {
  id: number
  productoId: number
  cantidadMinima: number
  precioEspecial: number
}

interface ConfiguracionComercio {
  mostrarBotonCliente: boolean
  impresionAutomaticaTicket: boolean
  usaFacturacionSRI: boolean
  permitePrecioNegociado: boolean
  esBarEscolar: boolean
}

interface Cliente {
  id: number
  identificacion: string
  nombre: string
  correo: string | null
}

interface CartItem {
  productoId: number
  nombre: string
  cantidad: number
  precioUnitario: number           // precio calculado (lista/volumen)
  precioModificado: number | null  // null = usar precioUnitario
  preciosVolumen: PrecioVolumen[]
}

interface Sucursal {
  id: number
  nombre: string
}

// Configuración de tipos de comprobante habilitados (Req 6.5)
interface ConfiguracionComprobanteDto {
  tipoComprobante: string
  habilitado: boolean
}

type TipoComprobante = string

// Datos almacenados de la última venta para imprimir ticket
interface LastSaleData {
  items: { nombre: string; cantidad: number; precioUnitario: number; subtotal: number }[]
  total: number
  tipoComprobante: string
  metodoPago: string
  cliente: { nombre: string; identificacion: string } | null
  fecha: Date
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

/**
 * Calculates the applicable price for a product given a quantity and volume pricing rules.
 * Selects the rule with the HIGHEST CantidadMinima that the quantity meets (Req 8.2, 8.3).
 * Falls back to PrecioLista if no rule applies.
 */
function calcularPrecio(
  precioLista: number,
  cantidad: number,
  preciosVolumen: PrecioVolumen[],
): number {
  if (preciosVolumen.length === 0 || cantidad <= 0) return precioLista

  const applicableRules = preciosVolumen.filter(
    (r) => cantidad >= r.cantidadMinima,
  )

  if (applicableRules.length === 0) return precioLista

  // Select the rule with the highest CantidadMinima
  const bestRule = applicableRules.reduce((best, current) =>
    current.cantidadMinima > best.cantidadMinima ? current : best,
  )

  return bestRule.precioEspecial
}

// ─── Component ────────────────────────────────────────────────────────────────

export function PosNormalPage() {
  const { comercioNombre, hasRole } = useAuth()

  // ─── State ────────────────────────────────────────────────────────────────
  const [productos, setProductos] = useState<Producto[]>([])
  const [lastSaleData, setLastSaleData] = useState<LastSaleData | null>(null)
  const [preciosVolumenMap, setPreciosVolumenMap] = useState<
    Record<number, PrecioVolumen[]>
  >({})
  const [config, setConfig] = useState<ConfiguracionComercio | null>(null)
  const [sucursales, setSucursales] = useState<Sucursal[]>([])
  const [selectedSucursalId, setSelectedSucursalId] = useState<number | null>(
    null,
  )
  const [cart, setCart] = useState<CartItem[]>([])
  const [searchTerm, setSearchTerm] = useState('')
  const [tiposComprobanteHabilitados, setTiposComprobanteHabilitados] = useState<string[]>([])
  const [tipoComprobante, setTipoComprobante] =
    useState<TipoComprobante>('')

  // Estado de método de pago (Req 1.2: Efectivo por defecto)
  const [metodoPago, setMetodoPago] = useState<MetodoPago>('Efectivo')
  const [cuotas, setCuotas] = useState<CuotasValidas>(0)
  const [referenciaTransaccion, setReferenciaTransaccion] = useState('')

  // Client state
  const [clienteSearch, setClienteSearch] = useState('')
  const [clienteResults, setClienteResults] = useState<Cliente[]>([])
  const [selectedCliente, setSelectedCliente] = useState<Cliente | null>(null)
  const [searchingCliente, setSearchingCliente] = useState(false)
  const [showNewClientModal, setShowNewClientModal] = useState(false)
  const [newClientForm, setNewClientForm] = useState({
    identificacion: '',
    nombre: '',
    correo: '',
    direccion: '',
    telefono: '',
  })

  // UI state
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [showConfirm, setShowConfirm] = useState(false)
  const [successMessage, setSuccessMessage] = useState('')
  const [errorMessage, setErrorMessage] = useState('')

  // Referencias a los temporizadores diferidos de la confirmación de venta
  // (auto-impresión y limpieza del mensaje de éxito). Se guardan para poder
  // cancelarlos al desmontar el componente y evitar que un setTimeout pendiente
  // retenga el estado del componente (fuga de memoria en tests y en navegación).
  const printTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const successTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  // Cancelar cualquier temporizador pendiente al desmontar.
  useEffect(() => {
    return () => {
      if (printTimeoutRef.current !== null) clearTimeout(printTimeoutRef.current)
      if (successTimeoutRef.current !== null) clearTimeout(successTimeoutRef.current)
    }
  }, [])

  // ─── Data Fetching ──────────────────────────────────────────────────────────

  useEffect(() => {
    async function loadInitialData() {
      try {
        const [productosData, configData, sucursalesData, preciosData] = await Promise.all([
          api.get<Producto[]>('/api/tenants/productos'),
          api.get<ConfiguracionComercio>('/api/tenants/configuracion'),
          api.get<Sucursal[]>('/api/tenants/sucursales'),
          api.get<Record<number, PrecioVolumen[]>>('/api/tenants/productos/precios-volumen'),
        ])

        // Filter to only Venta Directa and Ensamblado (Req 9.1)
        const posProductos = productosData.filter(
          (p) =>
            p.tipoArticulo === 'Venta Directa' ||
            p.tipoArticulo === 'Ensamblado',
        )
        setProductos(posProductos)
        setConfig(configData)
        setSucursales(sucursalesData)

        // Auto-select sucursal if only one
        if (sucursalesData.length === 1 && sucursalesData[0]) {
          setSelectedSucursalId(sucursalesData[0].id)
        }

        // Cargar configuracion de comprobantes con guarda por rol.
        // Solo Dueño/Gerente tienen permiso backend; para el resto evitamos el 403 innecesario.
        let comprobantesData: ConfiguracionComprobanteDto[]
        if (hasRole(['Dueño', 'Gerente'])) {
          try {
            comprobantesData = await api.get<ConfiguracionComprobanteDto[]>(
              '/api/tenants/comprobantes/configuracion',
            )
          } catch {
            // Salvaguarda ante errores no esperados (500, red): aplicar tipo por defecto.
            comprobantesData = [{ tipoComprobante: 'Ticket Digital', habilitado: true }]
          }
        } else {
          // Rol sin permiso (Cajero, Supervisor, Bodeguero) o rol indeterminado:
          // aplicar tipo por defecto SIN disparar la petición (evita el 403 y el log rojo).
          comprobantesData = [{ tipoComprobante: 'Ticket Digital', habilitado: true }]
        }

        // Configurar tipos de comprobante habilitados (Req 6.5).
        // Blindaje: el endpoint de comprobantes DEBE devolver un array, pero si por
        // cualquier motivo (respuesta 200 con formato inesperado, cambio de contrato
        // de la API, error de serialización backend) llega un objeto/null, un
        // comprobantesData.filter(...) directo lanzaría "filter is not a function" y
        // haría caer toda la carga inicial del POS, dejando la página inservible.
        // Se normaliza a array y, si no lo es, se aplica el tipo por defecto seguro.
        const comprobantesArray = Array.isArray(comprobantesData)
          ? comprobantesData
          : [{ tipoComprobante: 'Ticket Digital', habilitado: true }]
        const habilitados = comprobantesArray
          .filter((c) => c.habilitado)
          .map((c) => c.tipoComprobante)
        setTiposComprobanteHabilitados(habilitados)
        // Establecer primer tipo habilitado como valor por defecto
        if (habilitados.length > 0) {
          setTipoComprobante(habilitados[0]!)
        }

        // Usar datos batch de precios-volumen (1 request en vez de N)
        const preciosMap: Record<number, PrecioVolumen[]> = {}
        for (const prod of posProductos) {
          preciosMap[prod.id] = preciosData[prod.id] ?? []
        }
        setPreciosVolumenMap(preciosMap)
      } catch (err) {
        setErrorMessage('Error al cargar datos iniciales.')
        console.error(err)
      } finally {
        setLoading(false)
      }
    }

    loadInitialData()
    // `hasRole` proviene de useAuth (memoizado por claims); se incluye como
    // dependencia para satisfacer la regla de hooks. Solo cambia si cambian los
    // claims (cambio de sesión), por lo que no provoca recargas espurias.
  }, [hasRole])

  // ─── Cart Actions ───────────────────────────────────────────────────────────

  const addToCart = useCallback(
    (producto: Producto) => {
      setCart((prev) => {
        const existing = prev.find((item) => item.productoId === producto.id)
        if (existing) {
          const newQty = existing.cantidad + 1
          const newPrice = calcularPrecio(
            producto.precioLista,
            newQty,
            preciosVolumenMap[producto.id] ?? [],
          )
          return prev.map((item) =>
            item.productoId === producto.id
              ? { ...item, cantidad: newQty, precioUnitario: newPrice }
              : item,
          )
        }
        const precio = calcularPrecio(
          producto.precioLista,
          1,
          preciosVolumenMap[producto.id] ?? [],
        )
        return [
          ...prev,
          {
            productoId: producto.id,
            nombre: producto.nombre,
            cantidad: 1,
            precioUnitario: precio,
            precioModificado: null,
            preciosVolumen: preciosVolumenMap[producto.id] ?? [],
          },
        ]
      })
    },
    [preciosVolumenMap],
  )

  const updateQuantity = useCallback(
    (productoId: number, newQty: number) => {
      if (newQty <= 0) {
        setCart((prev) => prev.filter((item) => item.productoId !== productoId))
        return
      }
      const producto = productos.find((p) => p.id === productoId)
      if (!producto) return

      const newPrice = calcularPrecio(
        producto.precioLista,
        newQty,
        preciosVolumenMap[productoId] ?? [],
      )
      setCart((prev) =>
        prev.map((item) =>
          item.productoId === productoId
            ? { ...item, cantidad: newQty, precioUnitario: newPrice }
            : item,
        ),
      )
    },
    [productos, preciosVolumenMap],
  )

  const removeFromCart = useCallback((productoId: number) => {
    setCart((prev) => prev.filter((item) => item.productoId !== productoId))
  }, [])

  const updatePrecio = useCallback((productoId: number, newPrice: number | null) => {
    setCart((prev) =>
      prev.map((item) =>
        item.productoId === productoId
          ? { ...item, precioModificado: newPrice }
          : item,
      ),
    )
  }, [])

  // ─── Client Search ──────────────────────────────────────────────────────────

  const searchClientes = useCallback(async (query: string) => {
    if (query.length < 2) {
      setClienteResults([])
      return
    }
    setSearchingCliente(true)
    try {
      const response = await api.get<{ items: Cliente[]; total: number }>(
        `/api/tenants/clientes?busqueda=${encodeURIComponent(query)}`,
      )
      setClienteResults(response.items ?? [])
    } catch {
      setClienteResults([])
    } finally {
      setSearchingCliente(false)
    }
  }, [])

  const createCliente = useCallback(async () => {
    try {
      const newCliente = await api.post<Cliente>(
        '/api/tenants/clientes',
        newClientForm,
      )
      setSelectedCliente(newCliente)
      setShowNewClientModal(false)
      setNewClientForm({
        identificacion: '',
        nombre: '',
        correo: '',
        direccion: '',
        telefono: '',
      })
    } catch {
      setErrorMessage('Error al crear el cliente.')
    }
  }, [newClientForm])

  // ─── Sale Submission ────────────────────────────────────────────────────────

  const handleConfirmSale = useCallback(async () => {
    if (!selectedSucursalId) {
      setErrorMessage('Seleccione una sucursal.')
      return
    }
    setSubmitting(true)
    setErrorMessage('')
    try {
      await api.post('/api/tenants/ventas', {
        sucursalId: selectedSucursalId,
        clienteId: selectedCliente?.id ?? null,
        tipoComprobante,
        metodoPago,
        cuotas,
        referenciaTransaccion: referenciaTransaccion || null,
        lineas: cart.map((item) => {
          let precioRealCobrado: number | null = null
          if (item.precioModificado !== null && item.precioModificado !== item.precioUnitario) {
            precioRealCobrado = Math.round(item.precioModificado * 100) / 100
          }
          return {
            productoId: item.productoId,
            cantidad: item.cantidad,
            precioRealCobrado,
          }
        }),
      })

      setSuccessMessage('¡Venta registrada exitosamente!')

      // Guardar datos de la venta para el ticket de impresión
      const saleTotal = cart.reduce(
        (sum, item) => sum + item.cantidad * (item.precioModificado ?? item.precioUnitario),
        0,
      )
      setLastSaleData({
        items: cart.map((item) => ({
          nombre: item.nombre,
          cantidad: item.cantidad,
          precioUnitario: item.precioModificado ?? item.precioUnitario,
          subtotal: (item.precioModificado ?? item.precioUnitario) * item.cantidad,
        })),
        total: saleTotal,
        tipoComprobante,
        metodoPago,
        cliente: selectedCliente
          ? { nombre: selectedCliente.nombre, identificacion: selectedCliente.identificacion }
          : null,
        fecha: new Date(),
      })

      setCart([])
      setSelectedCliente(null)
      setClienteSearch('')
      setShowConfirm(false)

      // Resetear estado de método de pago
      setMetodoPago('Efectivo')
      setCuotas(0)
      setReferenciaTransaccion('')

      // Auto-print if configured (Req 9.5)
      if (config?.impresionAutomaticaTicket) {
        // Pequeño delay para que React renderice el ticket antes de imprimir.
        // Se guarda el id para poder cancelarlo si el componente se desmonta antes.
        if (printTimeoutRef.current !== null) clearTimeout(printTimeoutRef.current)
        printTimeoutRef.current = setTimeout(() => window.print(), 100)
      }

      // Clear success message after 4 seconds.
      // Se guarda el id del temporizador para cancelarlo al desmontar y así no
      // retener el estado del componente entre montajes (evita la fuga de memoria).
      if (successTimeoutRef.current !== null) clearTimeout(successTimeoutRef.current)
      successTimeoutRef.current = setTimeout(() => setSuccessMessage(''), 4000)
    } catch {
      setErrorMessage('Error al registrar la venta.')
    } finally {
      setSubmitting(false)
    }
  }, [selectedSucursalId, selectedCliente, tipoComprobante, metodoPago, cuotas, referenciaTransaccion, cart, config])

  // ─── Computed Values ────────────────────────────────────────────────────────

  const total = useMemo(
    () =>
      cart.reduce(
        (sum, item) => sum + item.cantidad * (item.precioModificado ?? item.precioUnitario),
        0,
      ),
    [cart],
  )

  const filteredProducts = useMemo(() => {
    if (!searchTerm.trim()) return productos
    const term = searchTerm.toLowerCase()
    return productos.filter((p) => p.nombre.toLowerCase().includes(term))
  }, [productos, searchTerm])

  // ─── Render ─────────────────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-gray-500 dark:text-gray-400">
          Cargando punto de venta...
        </div>
      </div>
    )
  }

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
          Punto de Venta
        </h1>
        {sucursales.length > 1 && (
          <select
            value={selectedSucursalId ?? ''}
            onChange={(e) => setSelectedSucursalId(Number(e.target.value) || null)}
            className="px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-surface text-gray-900 dark:text-dark-text text-sm"
            aria-label="Seleccionar sucursal"
          >
            <option value="">Seleccionar sucursal</option>
            {sucursales.map((s) => (
              <option key={s.id} value={s.id}>
                {s.nombre}
              </option>
            ))}
          </select>
        )}
      </div>

      {/* Messages */}
      {successMessage && (
        <div
          className="mb-4 p-3 rounded-lg bg-green-100 dark:bg-green-900/30 text-green-800 dark:text-green-300 text-sm"
          role="alert"
        >
          {successMessage}
        </div>
      )}
      {errorMessage && (
        <div
          className="mb-4 p-3 rounded-lg bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-300 text-sm"
          role="alert"
        >
          {errorMessage}
        </div>
      )}

      {/* Main Layout: Products (left) | Cart (right) */}
      <div className="flex-1 grid grid-cols-1 lg:grid-cols-3 gap-4 min-h-0">
        {/* ─── Products Panel ─────────────────────────────────────────────── */}
        <div className="lg:col-span-2 flex flex-col bg-white dark:bg-dark-surface rounded-xl shadow-sm border border-gray-200 dark:border-gray-700 overflow-hidden">
          {/* Search */}
          <div className="p-4 border-b border-gray-200 dark:border-gray-700">
            <input
              type="text"
              placeholder="Buscar producto..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-action-confirm"
              aria-label="Buscar producto"
            />
          </div>

          {/* Product Grid */}
          <div className="flex-1 overflow-y-auto p-4">
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3">
              {filteredProducts.map((producto) => (
                <button
                  key={producto.id}
                  onClick={() => addToCart(producto)}
                  className="flex flex-col items-center justify-center p-3 rounded-lg border border-gray-200 dark:border-gray-600 hover:border-action-confirm hover:bg-action-confirm/5 dark:hover:bg-action-confirm/10 transition-colors text-center cursor-pointer"
                  aria-label={`Agregar ${producto.nombre} al carrito`}
                >
                  <span className="text-sm font-medium text-gray-900 dark:text-dark-text truncate w-full" title={producto.nombre}>
                    {producto.nombre}
                  </span>
                  <span className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                    ${producto.precioLista.toFixed(2)}
                  </span>
                </button>
              ))}
            </div>
            {filteredProducts.length === 0 && (
              <p className="text-center text-gray-500 dark:text-gray-400 mt-8">
                No se encontraron productos.
              </p>
            )}
          </div>
        </div>

        {/* ─── Cart / Summary Panel ───────────────────────────────────────── */}
        <div className="flex flex-col bg-white dark:bg-dark-surface rounded-xl shadow-sm border border-gray-200 dark:border-gray-700 overflow-hidden">
          <div className="p-4 border-b border-gray-200 dark:border-gray-700">
            <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text">
              Detalle de Venta
            </h2>
          </div>

          {/* Cart Items */}
          <div className="flex-1 overflow-y-auto p-4 space-y-3">
            {cart.length === 0 ? (
              <p className="text-center text-gray-400 dark:text-gray-500 text-sm py-8">
                Agregue productos para comenzar.
              </p>
            ) : (
              cart.map((item) => (
                <div
                  key={item.productoId}
                  className="flex flex-col gap-1 p-2 rounded-lg bg-gray-50 dark:bg-dark-bg"
                >
                  <p className="text-sm font-medium text-gray-900 dark:text-dark-text">
                    {item.nombre}
                  </p>
                  <div className="flex items-center justify-between gap-2">
                    {config?.permitePrecioNegociado && !config?.esBarEscolar ? (
                      <div className="flex items-center gap-0.5">
                        <span className="text-xs text-gray-500 dark:text-gray-400">$</span>
                        <input
                          type="number"
                          step="0.01"
                          min="0.01"
                          value={item.precioModificado ?? item.precioUnitario}
                          onChange={(e) => {
                            const val = e.target.value
                            if (val === '') {
                              updatePrecio(item.productoId, null)
                            } else {
                              const parsed = parseFloat(val)
                              if (!isNaN(parsed) && parsed > 0) {
                                updatePrecio(item.productoId, parsed)
                              }
                            }
                          }}
                          onBlur={(e) => {
                            const val = parseFloat(e.target.value)
                            if (!isNaN(val) && val === item.precioUnitario) {
                              updatePrecio(item.productoId, null)
                            }
                          }}
                          className="w-16 text-xs text-center border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text px-1 py-0.5"
                          aria-label={`Precio unitario de ${item.nombre}`}
                        />
                        <span className="text-xs text-gray-500 dark:text-gray-400">c/u</span>
                      </div>
                    ) : (
                      <p className="text-xs text-gray-500 dark:text-gray-400">
                        ${item.precioUnitario.toFixed(2)} c/u
                      </p>
                    )}
                    <div className="flex items-center gap-1">
                      <button
                        onClick={() =>
                          updateQuantity(item.productoId, item.cantidad - 1)
                        }
                        className="w-7 h-7 flex items-center justify-center rounded bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-300 dark:hover:bg-gray-600 text-sm font-bold"
                        aria-label={`Reducir cantidad de ${item.nombre}`}
                      >
                        −
                      </button>
                      <input
                        type="number"
                        min="1"
                        value={item.cantidad}
                        onChange={(e) =>
                          updateQuantity(
                            item.productoId,
                            parseInt(e.target.value) || 0,
                          )
                        }
                        className="w-12 text-center text-sm border border-gray-300 dark:border-gray-600 rounded bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text"
                        aria-label={`Cantidad de ${item.nombre}`}
                      />
                      <button
                        onClick={() =>
                          updateQuantity(item.productoId, item.cantidad + 1)
                        }
                        className="w-7 h-7 flex items-center justify-center rounded bg-gray-200 dark:bg-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-300 dark:hover:bg-gray-600 text-sm font-bold"
                        aria-label={`Aumentar cantidad de ${item.nombre}`}
                      >
                        +
                      </button>
                      <button
                        onClick={() => removeFromCart(item.productoId)}
                        className="w-7 h-7 flex items-center justify-center rounded bg-action-danger/10 text-action-danger hover:bg-action-danger/20 ml-1"
                        aria-label={`Eliminar ${item.nombre} del carrito`}
                      >
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      </button>
                    </div>
                    <div className="text-sm font-semibold text-gray-900 dark:text-dark-text w-16 text-right">
                      ${(item.cantidad * (item.precioModificado ?? item.precioUnitario)).toFixed(2)}
                    </div>
                  </div>
                </div>
              ))
            )}
          </div>

          {/* Footer: Options + Total + Confirm */}
          <div className="border-t border-gray-200 dark:border-gray-700 p-4 space-y-3">
            {/* Tipo Comprobante (Req 6.5 - solo tipos habilitados) */}
            <div>
              <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
                Tipo de Comprobante
              </label>
              <select
                value={tipoComprobante}
                onChange={(e) =>
                  setTipoComprobante(e.target.value as TipoComprobante)
                }
                className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm"
                aria-label="Tipo de comprobante"
              >
                {tiposComprobanteHabilitados.map((tipo) => (
                  <option key={tipo} value={tipo}>
                    {tipo}
                  </option>
                ))}
              </select>
            </div>

            {/* Método de Pago (Req 1.1, 1.2, 1.7) */}
            <PaymentMethodSelector
              metodoPago={metodoPago}
              cuotas={cuotas}
              referencia={referenciaTransaccion}
              onMetodoPagoChange={setMetodoPago}
              onCuotasChange={setCuotas}
              onReferenciaChange={setReferenciaTransaccion}
            />

            {/* Resumen de cuotas - solo visible si cuotas > 0 (Req 1.5) */}
            <InstallmentSummary total={total} cuotas={cuotas} />

            {/* Client field — only if MostrarBotonCliente (Req 9.4, 12.3) */}
            {config?.mostrarBotonCliente && (
              <div>
                <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
                  Cliente (opcional)
                </label>
                {selectedCliente ? (
                  <div className="flex items-center justify-between gap-2 p-2 rounded-lg bg-gray-50 dark:bg-dark-bg">
                    <div>
                      <p className="text-sm font-medium text-gray-900 dark:text-dark-text">
                        {selectedCliente.nombre}
                      </p>
                      <p className="text-xs text-gray-500 dark:text-gray-400">
                        {selectedCliente.identificacion}
                      </p>
                    </div>
                    <button
                      onClick={() => {
                        setSelectedCliente(null)
                        setClienteSearch('')
                      }}
                      className="text-xs text-action-danger hover:underline"
                      aria-label="Quitar cliente seleccionado"
                    >
                      Quitar
                    </button>
                  </div>
                ) : (
                  <div className="relative">
                    <input
                      type="text"
                      placeholder="Buscar por identificación..."
                      value={clienteSearch}
                      onChange={(e) => {
                        setClienteSearch(e.target.value)
                        searchClientes(e.target.value)
                      }}
                      className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-action-confirm"
                      aria-label="Buscar cliente por identificación"
                    />
                    {searchingCliente && (
                      <span className="absolute right-3 top-2.5 text-xs text-gray-400">
                        Buscando...
                      </span>
                    )}
                    {/* Search Results Dropdown */}
                    {clienteSearch.length >= 2 && clienteResults.length > 0 && (
                      <div className="absolute z-10 w-full mt-1 bg-white dark:bg-dark-surface border border-gray-200 dark:border-gray-600 rounded-lg shadow-lg max-h-40 overflow-y-auto">
                        {clienteResults.map((cliente) => (
                          <button
                            key={cliente.id}
                            onClick={() => {
                              setSelectedCliente(cliente)
                              setClienteSearch('')
                              setClienteResults([])
                            }}
                            className="w-full text-left px-3 py-2 hover:bg-gray-100 dark:hover:bg-dark-bg text-sm text-gray-900 dark:text-dark-text"
                          >
                            <span className="font-medium">{cliente.nombre}</span>
                            <span className="text-gray-500 dark:text-gray-400 ml-2">
                              ({cliente.identificacion})
                            </span>
                          </button>
                        ))}
                      </div>
                    )}
                    {clienteSearch.length >= 2 &&
                      clienteResults.length === 0 &&
                      !searchingCliente && (
                        <div className="absolute z-10 w-full mt-1 bg-white dark:bg-dark-surface border border-gray-200 dark:border-gray-600 rounded-lg shadow-lg p-3">
                          <p className="text-xs text-gray-500 dark:text-gray-400 mb-2">
                            No se encontraron clientes.
                          </p>
                          <Button
                            variant="confirm"
                            className="text-xs w-full"
                            onClick={() => {
                              setShowNewClientModal(true)
                              setNewClientForm((f) => ({
                                ...f,
                                identificacion: clienteSearch,
                              }))
                            }}
                          >
                            + Crear nuevo cliente
                          </Button>
                        </div>
                      )}
                  </div>
                )}
              </div>
            )}

            {/* Total */}
            <div className="flex items-center justify-between pt-2 border-t border-gray-200 dark:border-gray-700">
              <span className="text-lg font-bold text-gray-900 dark:text-dark-text">
                Total
              </span>
              <span className="text-xl font-bold text-gray-900 dark:text-dark-text">
                ${total.toFixed(2)}
              </span>
            </div>

            {/* Confirm Button */}
            <Button
              variant="confirm"
              className="w-full"
              disabled={cart.length === 0 || !selectedSucursalId || submitting}
              loading={submitting}
              onClick={() => setShowConfirm(true)}
            >
              Confirmar Venta
            </Button>
          </div>
        </div>
      </div>

      {/* ─── Confirm Sale Dialog (Req 21.15) ────────────────────────────────── */}
      <ConfirmDialog
        open={showConfirm}
        onClose={() => setShowConfirm(false)}
        onConfirm={handleConfirmSale}
        title="Confirmar Venta"
        confirmLabel="Registrar Venta"
        confirmVariant="confirm"
        loading={submitting}
      >
        <div className="space-y-2">
          <p className="font-medium text-gray-800 dark:text-gray-200">
            Resumen de la venta:
          </p>
          <ul className="space-y-1">
            {cart.map((item) => (
              <li key={item.productoId} className="flex justify-between text-xs">
                <span>
                  {item.nombre} × {item.cantidad}
                </span>
                <span>${(item.cantidad * (item.precioModificado ?? item.precioUnitario)).toFixed(2)}</span>
              </li>
            ))}
          </ul>
          <div className="flex justify-between font-bold pt-2 border-t border-gray-200 dark:border-gray-700">
            <span>Total</span>
            <span>${total.toFixed(2)}</span>
          </div>
          {selectedCliente && (
            <p className="text-xs text-gray-500 dark:text-gray-400">
              Cliente: {selectedCliente.nombre} ({selectedCliente.identificacion})
            </p>
          )}
          <p className="text-xs text-gray-500 dark:text-gray-400">
            Comprobante: {tipoComprobante}
          </p>
          <p className="text-xs text-gray-500 dark:text-gray-400">
            Método de Pago: {metodoPago === 'TarjetaCredito' ? 'Tarjeta de Crédito' : metodoPago === 'TarjetaDebito' ? 'Tarjeta de Débito' : metodoPago}
            {cuotas > 0 && ` (${cuotas} cuotas)`}
          </p>
          {referenciaTransaccion && (
            <p className="text-xs text-gray-500 dark:text-gray-400">
              Referencia: {referenciaTransaccion}
            </p>
          )}
          <p className="text-xs text-action-danger mt-2">
            Esta acción es irreversible.
          </p>
        </div>
      </ConfirmDialog>

      {/* ─── New Client Modal (Req 21.9, 21.10) ─────────────────────────────── */}
      <Modal
        open={showNewClientModal}
        onClose={() => setShowNewClientModal(false)}
        title="Nuevo Cliente"
        warnOnClose={!!newClientForm.nombre || !!newClientForm.identificacion}
        size="md"
      >
        <div className="space-y-3">
          <div>
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
              Identificación *
            </label>
            <input
              type="text"
              value={newClientForm.identificacion}
              onChange={(e) =>
                setNewClientForm((f) => ({
                  ...f,
                  identificacion: e.target.value,
                }))
              }
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
              Nombre *
            </label>
            <input
              type="text"
              value={newClientForm.nombre}
              onChange={(e) =>
                setNewClientForm((f) => ({ ...f, nombre: e.target.value }))
              }
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
              Correo
            </label>
            <input
              type="email"
              value={newClientForm.correo}
              onChange={(e) =>
                setNewClientForm((f) => ({ ...f, correo: e.target.value }))
              }
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
              Teléfono
            </label>
            <input
              type="text"
              value={newClientForm.telefono}
              onChange={(e) =>
                setNewClientForm((f) => ({ ...f, telefono: e.target.value }))
              }
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
              Dirección
            </label>
            <input
              type="text"
              value={newClientForm.direccion}
              onChange={(e) =>
                setNewClientForm((f) => ({ ...f, direccion: e.target.value }))
              }
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text text-sm focus:outline-none focus:ring-2 focus:ring-action-confirm"
            />
          </div>
          <div className="flex justify-end gap-3 pt-2">
            <Button
              variant="neutral"
              onClick={() => setShowNewClientModal(false)}
            >
              Cancelar
            </Button>
            <Button
              variant="confirm"
              onClick={createCliente}
              disabled={
                !newClientForm.identificacion.trim() ||
                !newClientForm.nombre.trim()
              }
            >
              Crear Cliente
            </Button>
          </div>
        </div>
      </Modal>

      {/* ─── Ticket de Impresión (solo visible al imprimir) ─────────────────── */}
      {lastSaleData && (
        <TicketPrint
          comercioNombre={comercioNombre}
          cliente={lastSaleData.cliente}
          items={lastSaleData.items}
          total={lastSaleData.total}
          tipoComprobante={lastSaleData.tipoComprobante}
          metodoPago={lastSaleData.metodoPago}
          fecha={lastSaleData.fecha}
        />
      )}
    </div>
  )
}
