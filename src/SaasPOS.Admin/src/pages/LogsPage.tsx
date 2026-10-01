import { useState, useEffect } from 'react'
import { apiRequest } from '../lib/api'
import { formatDateTime } from '../lib/dateUtils'

interface LogEntry {
  id: number
  comercioId: number
  usuarioId: number
  accion: string
  fechaHora: string
  tablaAfectada: string
  registroId: string
  valoresAnteriores: Record<string, unknown> | null
  valoresNuevos: Record<string, unknown> | null
}

export function LogsPage() {
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [loading, setLoading] = useState(true)
  const [expandedId, setExpandedId] = useState<number | null>(null)
  const [filterAccion, setFilterAccion] = useState('')
  const [filterTabla, setFilterTabla] = useState('')
  const [filterComercio, setFilterComercio] = useState('')
  const [comercios, setComercios] = useState<{ id: number; razonSocial: string }[]>([])
  const [page, setPage] = useState(1)
  const pageSize = 20

  // Load comercios list for the filter dropdown
  useEffect(() => {
    const fetchComercios = async () => {
      try {
        const data = await apiRequest<{ id: number; ruc: string; razonSocial: string }[]>('/api/admin/comercios')
        setComercios(data.map(c => ({ id: c.id, razonSocial: c.razonSocial })))
      } catch (error) {
        console.error('Error loading comercios:', error)
      }
    }
    fetchComercios()
  }, [])

  useEffect(() => {
    const fetchLogs = async () => {
      setLoading(true)
      try {
        // Build query params for server-side filtering
        const params = new URLSearchParams()
        if (filterComercio) params.append('comercioId', filterComercio)
        if (filterTabla) params.append('tablaAfectada', filterTabla)
        if (filterAccion) params.append('accion', filterAccion)
        params.append('pageSize', '200') // Fetch more for client-side pagination

        const queryString = params.toString()
        const data = await apiRequest<{ items: LogEntry[], totalCount: number }>(`/api/admin/logs?${queryString}`)
        setLogs(data.items ?? [])
      } catch (error) {
        console.error('Error loading logs:', error)
        setLogs([])
      } finally {
        setLoading(false)
      }
    }
    fetchLogs()
  }, [filterComercio, filterTabla, filterAccion])

  const toggleExpand = (id: number) => {
    setExpandedId(expandedId === id ? null : id)
  }

  // Filter logs (additional client-side filtering if needed)
  const filteredLogs = logs

  // Pagination
  const totalPages = Math.ceil(filteredLogs.length / pageSize)
  const paginatedLogs = filteredLogs.slice((page - 1) * pageSize, page * pageSize)

  // Get unique values for filter options
  const uniqueAcciones = [...new Set(logs.map((l) => l.accion))]
  const uniqueTablas = [...new Set(logs.map((l) => l.tablaAfectada))]

  if (loading) {
    return (
      <div className="space-y-6">
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Logs de Auditoría</h2>
        <div className="animate-pulse space-y-3">
          {[1, 2, 3, 4, 5].map((i) => (
            <div key={i} className="h-12 bg-gray-200 dark:bg-dark-surface rounded"></div>
          ))}
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Logs de Auditoría</h2>
        <p className="text-gray-500 dark:text-dark-text/70 mt-1">
          Registro de acciones realizadas en el sistema.
        </p>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-4">
        <div>
          <label className="block text-xs text-gray-500 dark:text-dark-text/60 mb-1">Comercio</label>
          <select
            value={filterComercio}
            onChange={(e) => { setFilterComercio(e.target.value); setPage(1) }}
            className="px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text text-sm focus:ring-2 focus:ring-action-confirm focus:border-transparent"
          >
            <option value="">Todos</option>
            {comercios.map((c) => (
              <option key={c.id} value={c.id}>{c.razonSocial}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs text-gray-500 dark:text-dark-text/60 mb-1">Acción</label>
          <select
            value={filterAccion}
            onChange={(e) => { setFilterAccion(e.target.value); setPage(1) }}
            className="px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text text-sm focus:ring-2 focus:ring-action-confirm focus:border-transparent"
          >
            <option value="">Todas</option>
            {uniqueAcciones.map((a) => (
              <option key={a} value={a}>{a}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs text-gray-500 dark:text-dark-text/60 mb-1">Tabla</label>
          <select
            value={filterTabla}
            onChange={(e) => { setFilterTabla(e.target.value); setPage(1) }}
            className="px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text text-sm focus:ring-2 focus:ring-action-confirm focus:border-transparent"
          >
            <option value="">Todas</option>
            {uniqueTablas.map((t) => (
              <option key={t} value={t}>{t}</option>
            ))}
          </select>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white dark:bg-dark-surface rounded-xl shadow-sm border border-gray-100 dark:border-dark-surface overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-gray-500 dark:text-dark-text/60 bg-gray-50 dark:bg-dark-bg/50">
                <th className="px-4 py-3 font-medium">Fecha/Hora</th>
                <th className="px-4 py-3 font-medium">Comercio ID</th>
                <th className="px-4 py-3 font-medium">Usuario ID</th>
                <th className="px-4 py-3 font-medium">Acción</th>
                <th className="px-4 py-3 font-medium">Tabla</th>
                <th className="px-4 py-3 font-medium">Registro ID</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-50 dark:divide-dark-surface/50">
              {paginatedLogs.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-gray-400 dark:text-dark-text/50">
                    No se encontraron registros.
                  </td>
                </tr>
              ) : (
                paginatedLogs.map((log) => (
                  <LogRow
                    key={log.id}
                    log={log}
                    isExpanded={expandedId === log.id}
                    onToggle={() => toggleExpand(log.id)}
                  />
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-sm text-gray-500 dark:text-dark-text/60">
            Mostrando {(page - 1) * pageSize + 1}-{Math.min(page * pageSize, filteredLogs.length)} de {filteredLogs.length}
          </p>
          <div className="flex gap-2">
            <button
              onClick={() => setPage(page - 1)}
              disabled={page === 1}
              className="px-3 py-1 rounded text-sm border border-gray-300 dark:border-dark-surface disabled:opacity-50 disabled:cursor-not-allowed text-gray-700 dark:text-dark-text hover:bg-gray-100 dark:hover:bg-dark-bg"
            >
              Anterior
            </button>
            <span className="px-3 py-1 text-sm text-gray-600 dark:text-dark-text/70">
              {page} / {totalPages}
            </span>
            <button
              onClick={() => setPage(page + 1)}
              disabled={page === totalPages}
              className="px-3 py-1 rounded text-sm border border-gray-300 dark:border-dark-surface disabled:opacity-50 disabled:cursor-not-allowed text-gray-700 dark:text-dark-text hover:bg-gray-100 dark:hover:bg-dark-bg"
            >
              Siguiente
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

function LogRow({
  log,
  isExpanded,
  onToggle,
}: {
  log: LogEntry
  isExpanded: boolean
  onToggle: () => void
}) {
  return (
    <>
      <tr
        onClick={onToggle}
        className="text-gray-700 dark:text-dark-text cursor-pointer hover:bg-gray-50 dark:hover:bg-dark-bg/30 transition-colors"
      >
        <td className="px-4 py-3">{formatDateTime(log.fechaHora)}</td>
        <td className="px-4 py-3">{log.comercioId}</td>
        <td className="px-4 py-3">{log.usuarioId}</td>
        <td className="px-4 py-3">
          <span className="inline-flex px-2 py-0.5 rounded text-xs font-medium bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400">
            {log.accion}
          </span>
        </td>
        <td className="px-4 py-3">{log.tablaAfectada}</td>
        <td className="px-4 py-3 text-gray-500 dark:text-dark-text/60">{log.registroId}</td>
      </tr>
      {isExpanded && (
        <tr className="bg-gray-50 dark:bg-dark-bg/30">
          <td colSpan={6} className="px-4 py-4">
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
              <div>
                <p className="font-semibold text-gray-600 dark:text-dark-text/70 mb-1">
                  Valores Anteriores
                </p>
                <pre className="bg-white dark:bg-dark-surface p-3 rounded-lg overflow-x-auto text-gray-700 dark:text-dark-text border border-gray-100 dark:border-dark-surface">
                  {log.valoresAnteriores
                    ? JSON.stringify(log.valoresAnteriores, null, 2)
                    : '—'}
                </pre>
              </div>
              <div>
                <p className="font-semibold text-gray-600 dark:text-dark-text/70 mb-1">
                  Valores Nuevos
                </p>
                <pre className="bg-white dark:bg-dark-surface p-3 rounded-lg overflow-x-auto text-gray-700 dark:text-dark-text border border-gray-100 dark:border-dark-surface">
                  {log.valoresNuevos
                    ? JSON.stringify(log.valoresNuevos, null, 2)
                    : '—'}
                </pre>
              </div>
            </div>
          </td>
        </tr>
      )}
    </>
  )
}


