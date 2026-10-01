import { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import { api } from '../lib/api'
import { useAuth } from '../contexts/AuthContext'
import { useConfiguracion } from '../hooks/useConfiguracion'

// ─── Types ────────────────────────────────────────────────────────────────────

interface Producto {
  id: number
  nombre: string
  tipoArticulo: 'Venta Directa' | 'Ensamblado'
  precioLista: number
  categoriaId: number | null
  categoriaNombre: string | null
}

interface ConfiguracionComercio {
  esBarEscolar: boolean
}

interface Sucursal {
  id: number
  nombre: string
}

interface VentaReciente {
  productoNombre: string
  timestamp: number
}

interface Toast {
  id: number
  type: 'success' | 'error'
  message: string
}

// ─── Component ────────────────────────────────────────────────────────────────

/**
 * Verifica si un error es un 403 con código VENTAS_VISIBILITY_RESTRICTED.
 * Este error es esperado cuando un Cajero sin permiso intenta acceder a ventas.
 */
function isVentasVisibilityRestricted(err: unknown): boolean {
  if (err && typeof err === 'object' && 'status' in err) {
    const apiErr = err as { status: number; details?: { error?: string } }
    if (apiErr.status === 403) {
      return apiErr.details?.error === 'VENTAS_VISIBILITY_RESTRICTED'
    }
  }
  return false
}

export function PosBarPage() {
  // ─── Auth & Permissions ─────────────────────────────────────────────────────
  const { hasRole } = useAuth()
  const { config: sucursalConfig, fetchConfig } = useConfiguracion()

  // Determinar si el Cajero puede ver ventas (Req 4.3, 4.4)
  const esCajero = hasRole('Cajero')
  const puedeVerVentas = !esCajero || (sucursalConfig?.mostrarVentasAlCajero ?? false)

  // ─── State ──────────────────────────────────────────────────────────────────
  const [productos, setProductos] = useState<Producto[]>([])
  const [config, setConfig] = useState<ConfiguracionComercio | null>(null)
  const [sucursales, setSucursales] = useState<Sucursal[]>([])
  const [selectedSucursalId, setSelectedSucursalId] = useState<number | null>(null)

  // Loading states
  const [loading, setLoading] = useState(true)
  const [processingProducts, setProcessingProducts] = useState<Set<number>>(new Set())

  // Feedback
  const [toasts, setToasts] = useState<Toast[]>([])
  const [ventasDelDia, setVentasDelDia] = useState<VentaReciente[]>([])

  // Search/filter
  const [searchTerm, setSearchTerm] = useState('')
  const [selectedCategoria, setSelectedCategoria] = useState<string | null>(null)

  // Toast counter ref for unique IDs
  const toastCounterRef = useRef(0)

  // Conjunto de temporizadores activos de los toasts. Se guardan sus ids para
  // poder cancelarlos al desmontar el componente y evitar que un setTimeout
  // pendiente (auto-cierre del toast a los 2 s) invoque setToasts sobre un
  // componente ya desmontado. Ese temporizador huérfano retenía el estado del
  // componente entre montajes y era la causa raíz de la fuga de memoria monótona
  // (OOM) en la suite de tests, además del warning de act(...) en PosBarPage.
  const toastTimeoutsRef = useRef<Set<ReturnType<typeof setTimeout>>>(new Set())

  // ─── Toast Helpers ──────────────────────────────────────────────────────────

  const addToast = useCallback((type: 'success' | 'error', message: string) => {
    toastCounterRef.current += 1
    const id = toastCounterRef.current
    setToasts((t) => [...t, { id, type, message }])
    // Auto-cierre a los 2 segundos. Se guarda el id del temporizador para poder
    // cancelarlo al desmontar; al ejecutarse, se elimina a sí mismo del conjunto.
    const timeoutId = setTimeout(() => {
      setToasts((t) => t.filter((toast) => toast.id !== id))
      toastTimeoutsRef.current.delete(timeoutId)
    }, 2000)
    toastTimeoutsRef.current.add(timeoutId)
  }, [])

  // Cancelar todos los temporizadores de toasts pendientes al desmontar, para no
  // retener el estado del componente ni disparar actualizaciones fuera de act(...).
  useEffect(() => {
    const timeouts = toastTimeoutsRef.current
    return () => {
      timeouts.forEach((timeoutId) => clearTimeout(timeoutId))
      timeouts.clear()
    }
  }, [])

  // ─── Data Fetching ──────────────────────────────────────────────────────────

  useEffect(() => {
    // Cargar configuración de sucursal para verificar permisos de ventas
    fetchConfig()
  }, [fetchConfig])

  useEffect(() => {
    async function loadInitialData() {
      try {
        const [productosData, configData, sucursalesData] = await Promise.all([
          api.get<Producto[]>('/api/tenants/productos'),
          api.get<ConfiguracionComercio>('/api/tenants/configuracion'),
          api.get<Sucursal[]>('/api/tenants/sucursales'),
        ])

        setConfig(configData)

        // Filter to only Venta Directa and Ensamblado (Req 10.1)
        const posProductos = productosData.filter(
          (p) =>
            p.tipoArticulo === 'Venta Directa' ||
            p.tipoArticulo === 'Ensamblado',
        )
        setProductos(posProductos)
        setSucursales(sucursalesData)

        // Auto-select sucursal if only one
        if (sucursalesData.length === 1 && sucursalesData[0]) {
          setSelectedSucursalId(sucursalesData[0].id)
        }

        // Solo obtener ventas del día si tiene permiso (Req 4.3, 4.4)
        if (puedeVerVentas) {
          const hoy = new Date()
          const desde = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate()).toISOString()
          try {
            const ventasHoy = await api.get<{ id: number }[]>(`/api/tenants/ventas?desde=${desde}`)
            if (Array.isArray(ventasHoy)) {
              setVentasDelDia(ventasHoy.map((v) => ({ productoNombre: '', timestamp: v.id })))
            }
          } catch (err: unknown) {
            // Manejo defensivo: si recibimos 403 VENTAS_VISIBILITY_RESTRICTED, ignorar silenciosamente
            if (!isVentasVisibilityRestricted(err)) {
              // Solo registrar error si NO es un 403 de visibilidad de ventas
              // El contador se queda en 0 sin mostrar error al usuario
            }
          }
        }
      } catch {
        addToast('error', 'Error al cargar datos iniciales.')
      } finally {
        setLoading(false)
      }
    }

    loadInitialData()
  }, [addToast, puedeVerVentas])

  // ─── Sale Handler (Req 10.2 — one-click, no confirmation) ──────────────────

  const handleProductClick = useCallback(
    async (producto: Producto) => {
      if (!selectedSucursalId) {
        addToast('error', 'Seleccione una sucursal primero.')
        return
      }

      // Mark product as processing
      setProcessingProducts((prev) => new Set(prev).add(producto.id))

      try {
        await api.post('/api/tenants/ventas/bar-escolar', {
          sucursalId: selectedSucursalId,
          productoId: producto.id,
        })

        addToast('success', `✓ ${producto.nombre} — $${producto.precioLista.toFixed(2)}`)

        // Track sale for daily counter
        setVentasDelDia((prev) => [
          { productoNombre: producto.nombre, timestamp: Date.now() },
          ...prev,
        ])
      } catch (err: unknown) {
        const message =
          err instanceof Error
            ? err.message
            : typeof err === 'object' && err !== null && 'message' in err
              ? String((err as { message: unknown }).message)
              : 'Error al registrar venta'
        addToast('error', message)
      } finally {
        setProcessingProducts((prev) => {
          const next = new Set(prev)
          next.delete(producto.id)
          return next
        })
      }
    },
    [selectedSucursalId, addToast],
  )

  // ─── Computed Values ────────────────────────────────────────────────────────

  const categorias = useMemo(() => {
    const cats = new Set<string>()
    productos.forEach((p) => {
      if (p.categoriaNombre) cats.add(p.categoriaNombre)
    })
    return Array.from(cats).sort()
  }, [productos])

  const filteredProducts = useMemo(() => {
    let filtered = productos

    if (selectedCategoria) {
      filtered = filtered.filter((p) => p.categoriaNombre === selectedCategoria)
    }

    if (searchTerm.trim()) {
      const term = searchTerm.toLowerCase()
      filtered = filtered.filter((p) => p.nombre.toLowerCase().includes(term))
    }

    return filtered
  }, [productos, searchTerm, selectedCategoria])

  // ─── Render: Loading ────────────────────────────────────────────────────────

  if (loading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-gray-500 dark:text-gray-400">
          Cargando Bar Escolar...
        </div>
      </div>
    )
  }

  // ─── Render: Not Bar Escolar ────────────────────────────────────────────────

  if (config && !config.esBarEscolar) {
    return (
      <div className="flex flex-col items-center justify-center h-64 gap-4">
        <div className="text-center">
          <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-2">
            Modo Bar Escolar no disponible
          </h1>
          <p className="text-gray-600 dark:text-gray-400 max-w-md">
            Este modo solo está disponible para comercios configurados como Bar Escolar.
            Contacte al administrador para activar esta funcionalidad.
          </p>
        </div>
      </div>
    )
  }

  // ─── Render: Main Interface ─────────────────────────────────────────────────

  return (
    <div className="flex flex-col h-full relative">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 mb-4">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
            Bar Escolar
          </h1>
          <span className="inline-flex items-center gap-1 px-2 py-1 rounded-full bg-action-confirm/10 text-action-confirm text-xs font-medium">
            <span className="w-2 h-2 rounded-full bg-action-confirm animate-pulse" />
            Modo rápido
          </span>
        </div>

        <div className="flex items-center gap-3">
          {/* Contador de ventas del día — ocultar si Cajero sin permiso (Req 4.3) */}
          {puedeVerVentas && (
            <span className="text-sm text-gray-600 dark:text-gray-400">
              Ventas hoy:{' '}
              <span className="font-bold text-gray-900 dark:text-dark-text">
                {ventasDelDia.length}
              </span>
            </span>
          )}

          {/* Sucursal selector (only if multiple) */}
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
      </div>

      {/* Search and Category filter */}
      <div className="flex flex-col sm:flex-row gap-3 mb-4">
        <input
          type="text"
          placeholder="Buscar producto..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          className="flex-1 px-4 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-surface text-gray-900 dark:text-dark-text text-sm placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-action-confirm"
          aria-label="Buscar producto"
        />

        {categorias.length > 0 && (
          <select
            value={selectedCategoria ?? ''}
            onChange={(e) => setSelectedCategoria(e.target.value || null)}
            className="px-3 py-2 rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-surface text-gray-900 dark:text-dark-text text-sm"
            aria-label="Filtrar por categoría"
          >
            <option value="">Todas las categorías</option>
            {categorias.map((cat) => (
              <option key={cat} value={cat}>
                {cat}
              </option>
            ))}
          </select>
        )}
      </div>

      {/* Product Grid — large touch-friendly cards (Req 10.1, 10.2, 21.11) */}
      <div className="flex-1 overflow-y-auto">
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-3">
          {filteredProducts.map((producto) => {
            const isProcessing = processingProducts.has(producto.id)
            return (
              <button
                key={producto.id}
                onClick={() => handleProductClick(producto)}
                disabled={isProcessing}
                className={`
                  relative flex flex-col items-center justify-center
                  p-4 sm:p-6 rounded-xl border-2 transition-all duration-150
                  min-h-[100px] sm:min-h-[120px]
                  select-none cursor-pointer
                  ${
                    isProcessing
                      ? 'border-action-confirm bg-action-confirm/10 dark:bg-action-confirm/20 opacity-70 cursor-wait'
                      : 'border-gray-200 dark:border-gray-600 bg-white dark:bg-dark-surface hover:border-action-confirm hover:shadow-md active:scale-95 active:bg-action-confirm/5 dark:active:bg-action-confirm/10'
                  }
                `}
                aria-label={`Vender ${producto.nombre} por $${producto.precioLista.toFixed(2)}`}
                aria-busy={isProcessing}
              >
                {/* Loading spinner overlay */}
                {isProcessing && (
                  <div className="absolute inset-0 flex items-center justify-center rounded-xl bg-white/50 dark:bg-dark-surface/50">
                    <svg
                      className="animate-spin h-6 w-6 text-action-confirm"
                      xmlns="http://www.w3.org/2000/svg"
                      fill="none"
                      viewBox="0 0 24 24"
                      aria-hidden="true"
                    >
                      <circle
                        className="opacity-25"
                        cx="12"
                        cy="12"
                        r="10"
                        stroke="currentColor"
                        strokeWidth="4"
                      />
                      <path
                        className="opacity-75"
                        fill="currentColor"
                        d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
                      />
                    </svg>
                  </div>
                )}

                {/* Product name */}
                <span className="text-sm sm:text-base font-semibold text-gray-900 dark:text-dark-text text-center leading-tight line-clamp-2" title={producto.nombre}>
                  {producto.nombre}
                </span>

                {/* Price */}
                <span className="mt-2 text-lg sm:text-xl font-bold text-action-confirm">
                  ${producto.precioLista.toFixed(2)}
                </span>

                {/* Category tag */}
                {producto.categoriaNombre && (
                  <span className="mt-1 text-[10px] text-gray-400 dark:text-gray-500 truncate max-w-full">
                    {producto.categoriaNombre}
                  </span>
                )}
              </button>
            )
          })}
        </div>

        {filteredProducts.length === 0 && (
          <p className="text-center text-gray-500 dark:text-gray-400 mt-12">
            No se encontraron productos.
          </p>
        )}
      </div>

      {/* Toast notifications */}
      <div
        className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 pointer-events-none"
        aria-live="polite"
        aria-atomic="false"
      >
        {toasts.map((toast) => (
          <div
            key={toast.id}
            className={`
              pointer-events-auto px-4 py-3 rounded-lg shadow-lg text-sm font-medium
              animate-[slideIn_0.2s_ease-out]
              ${
                toast.type === 'success'
                  ? 'bg-action-confirm text-white'
                  : 'bg-action-danger text-white'
              }
            `}
            role="alert"
          >
            {toast.message}
          </div>
        ))}
      </div>
    </div>
  )
}
