import { useState, useEffect, useCallback } from 'react'
import { api } from '../lib/api'
import { Button } from '../components/ui/Button'
import { ResponsiveTable } from '../components/ui/ResponsiveTable'
import { ExportPdfDialog, type PdfExportOptions } from '../components/pos/ExportPdfDialog'

type PlanNivel = 'BÃƒÆ’Ã‚Â¡sico' | 'Intermedio' | 'Empresarial'

interface TopProducto {
  productoNombre: string
  cantidadVendida: number
  montoTotal: number
}

interface TopCategoria {
  categoriaNombre: string
  montoTotal: number
}

interface TopSucursal {
  sucursalNombre: string
  montoTotal: number
}

/** Shape returned by the /api/tenants/reportes/* endpoints */
interface ReporteResponse {
  tipoReporte: string
  titulo: string
  datos: { etiqueta: string; valor: number; detalle?: string }[]
  generadoEn: string
}

interface Configuracion {
  planNivel: PlanNivel
}

interface SucursalOption {
  id: string
  nombre: string
}

interface CategoriaOption {
  id: string
  nombre: string
}

interface ProductoOption {
  id: string
  nombre: string
}

/**
 * Reportes page.
 * - Plan BÃƒÆ’Ã‚Â¡sico: blocked, shows upgrade message (Req 13.1)
 * - Plan Intermedio: 3 predefined reports for current month, no filters (Req 13.2)
 * - Plan Empresarial: predefined + custom reports with filters (Req 13.3)
 */
export function ReportesPage() {
  const [planNivel, setPlanNivel] = useState<PlanNivel | null>(null)
  const [loadingPlan, setLoadingPlan] = useState(true)

  // Report data
  const [topProductos, setTopProductos] = useState<TopProducto[]>([])
  const [topCategorias, setTopCategorias] = useState<TopCategoria[]>([])
  const [topSucursales, setTopSucursales] = useState<TopSucursal[]>([])
  const [loadingReports, setLoadingReports] = useState(false)

  // Custom report filters (Empresarial only)
  const [fechaDesde, setFechaDesde] = useState('')
  const [fechaHasta, setFechaHasta] = useState('')
  const [sucursalId, setSucursalId] = useState('')
  const [categoriaId, setCategoriaId] = useState('')
  const [productoId, setProductoId] = useState('')
  const [customReportData, setCustomReportData] = useState<unknown[] | null>(null)
  const [loadingCustom, setLoadingCustom] = useState(false)

  // Filter options
  const [sucursales, setSucursales] = useState<SucursalOption[]>([])
  const [categorias, setCategorias] = useState<CategoriaOption[]>([])
  const [productos, setProductos] = useState<ProductoOption[]>([])

  // Export loading
  const [exportingId, setExportingId] = useState<string | null>(null)

  // Diálogo de personalización PDF (Plan Empresarial)
  const [showExportDialog, setShowExportDialog] = useState(false)
  const [pendingExportId, setPendingExportId] = useState<string | null>(null)

  useEffect(() => {
    async function fetchConfig() {
      try {
        const config = await api.get<Configuracion>('/api/tenants/configuracion')
        setPlanNivel(config.planNivel)
      } catch {
        setPlanNivel('BÃƒÆ’Ã‚Â¡sico')
      } finally {
        setLoadingPlan(false)
      }
    }
    fetchConfig()
  }, [])

  const fetchPredefinedReports = useCallback(async () => {
    setLoadingReports(true)
    try {
      const [prodRes, catRes, sucRes] = await Promise.all([
        api.get<ReporteResponse>('/api/tenants/reportes/top-producto'),
        api.get<ReporteResponse>('/api/tenants/reportes/top-categoria'),
        api.get<ReporteResponse>('/api/tenants/reportes/top-sucursal'),
      ])
      setTopProductos(
        (prodRes.datos ?? []).map(d => ({
          productoNombre: d.etiqueta,
          cantidadVendida: Number(d.detalle ?? 0),
          montoTotal: d.valor,
        }))
      )
      setTopCategorias(
        (catRes.datos ?? []).map(d => ({
          categoriaNombre: d.etiqueta,
          montoTotal: d.valor,
        }))
      )
      setTopSucursales(
        (sucRes.datos ?? []).map(d => ({
          sucursalNombre: d.etiqueta,
          montoTotal: d.valor,
        }))
      )
    } catch {
      // Silently handle - empty state shown
    } finally {
      setLoadingReports(false)
    }
  }, [])

  useEffect(() => {
    if (planNivel && planNivel !== 'BÃƒÆ’Ã‚Â¡sico') {
      fetchPredefinedReports()
    }
  }, [planNivel, fetchPredefinedReports])

  useEffect(() => {
    if (planNivel === 'Empresarial') {
      async function fetchFilterOptions() {
        try {
          const [suc, cat, prod] = await Promise.all([
            api.get<SucursalOption[]>('/api/tenants/sucursales'),
            api.get<CategoriaOption[]>('/api/tenants/categorias'),
            api.get<ProductoOption[]>('/api/tenants/productos'),
          ])
          setSucursales(suc)
          setCategorias(cat)
          setProductos(prod)
        } catch {
          // Filter options unavailable
        }
      }
      fetchFilterOptions()
    }
  }, [planNivel])

  const handleGenerarReportePersonalizado = async () => {
    setLoadingCustom(true)
    try {
      const params = new URLSearchParams()
      if (fechaDesde) params.set('fechaDesde', fechaDesde)
      if (fechaHasta) params.set('fechaHasta', fechaHasta)
      if (sucursalId) params.set('sucursalId', sucursalId)
      if (categoriaId) params.set('categoriaId', categoriaId)
      if (productoId) params.set('productoId', productoId)

      const res = await api.get<ReporteResponse>(
        `/api/tenants/reportes/personalizado?${params.toString()}`,
      )
      setCustomReportData(res.datos ?? [])
    } catch {
      setCustomReportData([])
    } finally {
      setLoadingCustom(false)
    }
  }

  const handleExport = async (reporteId: string, formato: 'pdf' | 'csv') => {
    // Plan Empresarial + PDF: mostrar diálogo de personalización (Req 7.4)
    if (formato === 'pdf' && planNivel === 'Empresarial') {
      setPendingExportId(reporteId)
      setShowExportDialog(true)
      return
    }

    // Plan Intermedio + PDF o cualquier plan + CSV: descarga directa (Req 7.4, 7.6)
    await executeExport(reporteId, formato)
  }

  /** Ejecuta la descarga directa del reporte sin opciones de personalización */
  const executeExport = async (reporteId: string, formato: 'pdf' | 'csv', options?: PdfExportOptions) => {
    setExportingId(`${reporteId}-${formato}`)
    try {
      // Mapear el ID del frontend al valor del enum del backend
      const tipoMap: Record<string, string> = {
        'top-producto': 'TopProducto',
        'top-categoria': 'TopCategoria',
        'top-sucursal': 'TopSucursal',
        'personalizado': 'Personalizado',
      }
      const tipo = tipoMap[reporteId] ?? reporteId
      const formatoApi = formato === 'pdf' ? 'Pdf' : 'Csv'

      // Construir URL con parámetros base
      let url = `${import.meta.env.VITE_API_BASE_URL ?? 'https://api.saas.com'}/api/tenants/reportes/exportar?tipo=${tipo}&formato=${formatoApi}`

      // Agregar query params de personalización para Plan Empresarial + PDF (Req 7.5)
      if (options && formato === 'pdf') {
        url += `&incluirGraficoBarras=${options.incluirGraficoBarras}`
        url += `&incluirGraficoPastel=${options.incluirGraficoPastel}`
        url += `&incluirTabla=${options.incluirTabla}`
      }

      const response = await fetch(url, {
        headers: {
          Authorization: `Bearer ${localStorage.getItem('saas-pos-token') ?? ''}`,
        },
      })
      if (response.ok) {
        const blob = await response.blob()
        const blobUrl = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = blobUrl
        a.download = `reporte-${reporteId}.${formato}`
        a.click()
        URL.revokeObjectURL(blobUrl)
      }
    } catch {
      // Export failed silently
    } finally {
      setExportingId(null)
    }
  }

  /** Callback del diálogo de personalización: confirmar exportación con opciones (Req 7.5) */
  const handleExportConfirm = async (options: PdfExportOptions) => {
    setShowExportDialog(false)
    if (pendingExportId) {
      await executeExport(pendingExportId, 'pdf', options)
      setPendingExportId(null)
    }
  }

  if (loadingPlan) {
    return (
      <div className="flex items-center justify-center py-12">
        <div className="animate-spin h-8 w-8 border-4 border-action-confirm border-t-transparent rounded-full" />
      </div>
    )
  }

  // Plan BÃƒÆ’Ã‚Â¡sico: block all reports (Req 13.1)
  if (planNivel === 'BÃƒÆ’Ã‚Â¡sico') {
    return (
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-4">
          Reportes
        </h1>
        <div className="bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-700 rounded-lg p-6 text-center">
          <svg
            className="mx-auto h-12 w-12 text-yellow-500 mb-4"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-2.5L13.732 4c-.77-.833-1.964-.833-2.732 0L4.082 16.5c-.77.833.192 2.5 1.732 2.5z"
            />
          </svg>
          <p className="text-gray-700 dark:text-gray-300 text-lg">
            Los reportes no estÃƒÆ’Ã‚Â¡n disponibles para su plan actual. Actualice a Plan
            Intermedio o Empresarial.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-6">
        Reportes
      </h1>

      {loadingReports ? (
        <div className="flex items-center justify-center py-12">
          <div className="animate-spin h-8 w-8 border-4 border-action-confirm border-t-transparent rounded-full" />
        </div>
      ) : (
        <div className="space-y-6">
          {/* Predefined Reports */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
            {/* Top Producto */}
            <div className="bg-white dark:bg-dark-surface rounded-lg shadow p-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text mb-4">
                Top Productos
              </h2>
              {topProductos.length === 0 ? (
                <p className="text-gray-500 dark:text-gray-400 text-sm">
                  Sin datos disponibles
                </p>
              ) : (
                <ul className="space-y-2">
                  {topProductos.map((item, idx) => (
                    <li
                      key={idx}
                      className="flex justify-between text-sm text-gray-700 dark:text-gray-300"
                    >
                      <span>{item.productoNombre}</span>
                      <span className="font-medium">${item.montoTotal.toFixed(2)}</span>
                    </li>
                  ))}
                </ul>
              )}
              <div className="mt-4 flex gap-2">
                <Button
                  variant="neutral"
                  className="text-xs px-2 py-1"
                  loading={exportingId === 'top-producto-pdf'}
                  onClick={() => handleExport('top-producto', 'pdf')}
                >
                  PDF
                </Button>
                <Button
                  variant="neutral"
                  className="text-xs px-2 py-1"
                  loading={exportingId === 'top-producto-csv'}
                  onClick={() => handleExport('top-producto', 'csv')}
                >
                  CSV
                </Button>
              </div>
            </div>

            {/* Top CategorÃƒÆ’Ã‚Â­a */}
            <div className="bg-white dark:bg-dark-surface rounded-lg shadow p-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text mb-4">
                Top CategorÃƒÆ’Ã‚Â­as
              </h2>
              {topCategorias.length === 0 ? (
                <p className="text-gray-500 dark:text-gray-400 text-sm">
                  Sin datos disponibles
                </p>
              ) : (
                <ul className="space-y-2">
                  {topCategorias.map((item, idx) => (
                    <li
                      key={idx}
                      className="flex justify-between text-sm text-gray-700 dark:text-gray-300"
                    >
                      <span>{item.categoriaNombre}</span>
                      <span className="font-medium">${item.montoTotal.toFixed(2)}</span>
                    </li>
                  ))}
                </ul>
              )}
              <div className="mt-4 flex gap-2">
                <Button
                  variant="neutral"
                  className="text-xs px-2 py-1"
                  loading={exportingId === 'top-categoria-pdf'}
                  onClick={() => handleExport('top-categoria', 'pdf')}
                >
                  PDF
                </Button>
                <Button
                  variant="neutral"
                  className="text-xs px-2 py-1"
                  loading={exportingId === 'top-categoria-csv'}
                  onClick={() => handleExport('top-categoria', 'csv')}
                >
                  CSV
                </Button>
              </div>
            </div>

            {/* Top Sucursal */}
            <div className="bg-white dark:bg-dark-surface rounded-lg shadow p-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text mb-4">
                Top Sucursales
              </h2>
              {topSucursales.length === 0 ? (
                <p className="text-gray-500 dark:text-gray-400 text-sm">
                  Sin datos disponibles
                </p>
              ) : (
                <ul className="space-y-2">
                  {topSucursales.map((item, idx) => (
                    <li
                      key={idx}
                      className="flex justify-between text-sm text-gray-700 dark:text-gray-300"
                    >
                      <span>{item.sucursalNombre}</span>
                      <span className="font-medium">${item.montoTotal.toFixed(2)}</span>
                    </li>
                  ))}
                </ul>
              )}
              <div className="mt-4 flex gap-2">
                <Button
                  variant="neutral"
                  className="text-xs px-2 py-1"
                  loading={exportingId === 'top-sucursal-pdf'}
                  onClick={() => handleExport('top-sucursal', 'pdf')}
                >
                  PDF
                </Button>
                <Button
                  variant="neutral"
                  className="text-xs px-2 py-1"
                  loading={exportingId === 'top-sucursal-csv'}
                  onClick={() => handleExport('top-sucursal', 'csv')}
                >
                  CSV
                </Button>
              </div>
            </div>
          </div>

          {/* Custom Reports - Empresarial only (Req 13.3) */}
          {planNivel === 'Empresarial' && (
            <div className="bg-white dark:bg-dark-surface rounded-lg shadow p-6">
              <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text mb-4">
                Reporte Personalizado
              </h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4 mb-4">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Fecha Desde
                  </label>
                  <input
                    type="date"
                    value={fechaDesde}
                    onChange={e => setFechaDesde(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Fecha Hasta
                  </label>
                  <input
                    type="date"
                    value={fechaHasta}
                    onChange={e => setFechaHasta(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Sucursal
                  </label>
                  <select
                    value={sucursalId}
                    onChange={e => setSucursalId(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
                  >
                    <option value="">Todas</option>
                    {sucursales.map(s => (
                      <option key={s.id} value={s.id}>
                        {s.nombre}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    CategorÃƒÆ’Ã‚Â­a
                  </label>
                  <select
                    value={categoriaId}
                    onChange={e => setCategoriaId(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
                  >
                    <option value="">Todas</option>
                    {categorias.map(c => (
                      <option key={c.id} value={c.id}>
                        {c.nombre}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                    Producto
                  </label>
                  <select
                    value={productoId}
                    onChange={e => setProductoId(e.target.value)}
                    className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
                  >
                    <option value="">Todos</option>
                    {productos.map(p => (
                      <option key={p.id} value={p.id}>
                        {p.nombre}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
              <Button
                variant="confirm"
                loading={loadingCustom}
                onClick={handleGenerarReportePersonalizado}
              >
                Generar Reporte
              </Button>

              {customReportData !== null && (
                <div className="mt-4">
                  {Array.isArray(customReportData) && customReportData.length === 0 ? (
                    <p className="text-gray-500 dark:text-gray-400 text-sm">
                      No se encontraron resultados para los filtros seleccionados.
                    </p>
                  ) : (
                    <ResponsiveTable>
                      <table className="w-full text-sm text-left text-gray-700 dark:text-gray-300">
                        <thead className="text-xs uppercase bg-gray-100 dark:bg-dark-bg">
                          <tr>
                            {customReportData &&
                              Array.isArray(customReportData) &&
                              customReportData.length > 0 &&
                              Object.keys(
                                customReportData[0] as Record<string, unknown>,
                              ).map((key, idx) => (
                                <th key={key} className={`px-4 py-2${idx === 0 ? ' sticky-col' : ''}`}>
                                  {key}
                                </th>
                              ))}
                          </tr>
                        </thead>
                        <tbody>
                          {Array.isArray(customReportData) &&
                            customReportData.map((row, idx) => (
                              <tr
                                key={idx}
                                className="border-b border-gray-200 dark:border-gray-700"
                              >
                                {Object.values(row as Record<string, unknown>).map(
                                  (val, vIdx) => (
                                    <td key={vIdx} className={`px-4 py-2${vIdx === 0 ? ' sticky-col' : ''}`}>
                                      {String(val)}
                                    </td>
                                  ),
                                )}
                              </tr>
                            ))}
                        </tbody>
                      </table>
                    </ResponsiveTable>
                  )}
                  <div className="mt-3 flex gap-2">
                    <Button
                      variant="neutral"
                      className="text-xs px-2 py-1"
                      loading={exportingId === 'personalizado-pdf'}
                      onClick={() => handleExport('personalizado', 'pdf')}
                    >
                      Exportar PDF
                    </Button>
                    <Button
                      variant="neutral"
                      className="text-xs px-2 py-1"
                      loading={exportingId === 'personalizado-csv'}
                      onClick={() => handleExport('personalizado', 'csv')}
                    >
                      Exportar CSV
                    </Button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      )}

      {/* Diálogo de personalización PDF - Solo Plan Empresarial (Req 7.1) */}
      <ExportPdfDialog
        isOpen={showExportDialog}
        onConfirm={handleExportConfirm}
        onCancel={() => {
          setShowExportDialog(false)
          setPendingExportId(null)
        }}
      />
    </div>
  )
}
