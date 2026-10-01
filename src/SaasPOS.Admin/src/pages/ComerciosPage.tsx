import { useEffect, useState, useMemo } from 'react'
import { Link } from 'react-router-dom'
import { apiRequest } from '../lib/api'
import { formatDateTime } from '../lib/dateUtils'
import { Button } from '../components/ui/Button'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { RecuperacionDuenosModal } from '../components/RecuperacionDuenosModal'

type EstadoComercio = 'Activo' | 'Por vencer' | 'En mora' | 'Suspendido'

interface Comercio {
  id: number
  ruc: string
  razonSocial: string
  planNombre: string
  estado: EstadoComercio
  fechaProximoCorte: string
  activo: boolean
}

interface PagoForm {
  montoPagado: string
  metodoPago: string
  referencia: string
}

const ESTADO_OPTIONS: Array<{ value: string; label: string }> = [
  { value: 'Todos', label: 'Todos' },
  { value: 'Activo', label: 'Activo' },
  { value: 'Por vencer', label: 'Por vencer' },
  { value: 'En mora', label: 'En mora' },
  { value: 'Suspendido', label: 'Suspendido' },
]

const METODO_PAGO_OPTIONS = [
  { value: 'transferencia', label: 'Transferencia bancaria' },
  { value: 'efectivo', label: 'Efectivo' },
  { value: 'tarjeta', label: 'Tarjeta de crédito/débito' },
  { value: 'cheque', label: 'Cheque' },
]

function getEstadoBadgeClasses(estado: EstadoComercio): string {
  switch (estado) {
    case 'Activo':
      return 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300'
    case 'Por vencer':
      return 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300'
    case 'En mora':
      return 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300'
    case 'Suspendido':
      return 'bg-gray-100 text-gray-600 dark:bg-gray-700/30 dark:text-gray-400'
  }
}

export function ComerciosPage() {
  const [comercios, setComercios] = useState<Comercio[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Filters
  const [filtroEstado, setFiltroEstado] = useState('Todos')
  const [busqueda, setBusqueda] = useState('')

  // Confirm dialog state
  const [confirmDialog, setConfirmDialog] = useState<{
    isOpen: boolean
    title: string
    message: string
    variant: 'danger' | 'confirm'
    action: (() => Promise<void>) | null
  }>({ isOpen: false, title: '', message: '', variant: 'danger', action: null })
  const [confirmLoading, setConfirmLoading] = useState(false)

  // Payment modal state
  const [pagoModal, setPagoModal] = useState<{ isOpen: boolean; comercioId: number | null; comercioNombre: string }>({
    isOpen: false,
    comercioId: null,
    comercioNombre: '',
  })
  const [pagoForm, setPagoForm] = useState<PagoForm>({ montoPagado: '', metodoPago: 'transferencia', referencia: '' })
  const [pagoLoading, setPagoLoading] = useState(false)
  const [pagoError, setPagoError] = useState<string | null>(null)

  // Modal de recuperación de contraseña de dueños (cross-tenant)
  const [showRecuperacion, setShowRecuperacion] = useState(false)

  useEffect(() => {
    fetchComercios()
  }, [])

  async function fetchComercios() {
    try {
      setLoading(true)
      setError(null)
      const result = await apiRequest<Comercio[]>('/api/admin/comercios')
      setComercios(result)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al cargar los comercios')
    } finally {
      setLoading(false)
    }
  }

  const comerciosFiltrados = useMemo(() => {
    return comercios.filter((c) => {
      const matchEstado = filtroEstado === 'Todos' || c.estado === filtroEstado
      const searchLower = busqueda.toLowerCase()
      const matchBusqueda =
        !busqueda || c.ruc.toLowerCase().includes(searchLower) || c.razonSocial.toLowerCase().includes(searchLower)
      return matchEstado && matchBusqueda
    })
  }, [comercios, filtroEstado, busqueda])

  // --- Actions ---

  function handleSuspender(comercio: Comercio) {
    setConfirmDialog({
      isOpen: true,
      title: 'Suspender Comercio',
      message: `¿Está seguro que desea suspender el comercio "${comercio.razonSocial}" (RUC: ${comercio.ruc})? El comercio no podrá operar hasta ser reactivado.`,
      variant: 'danger',
      action: async () => {
        await apiRequest(`/api/admin/comercios/${comercio.id}/suspender`, { method: 'PATCH' })
      },
    })
  }

  function handleReactivar(comercio: Comercio) {
    setConfirmDialog({
      isOpen: true,
      title: 'Reactivar Comercio',
      message: `¿Está seguro que desea reactivar el comercio "${comercio.razonSocial}" (RUC: ${comercio.ruc})?`,
      variant: 'confirm',
      action: async () => {
        await apiRequest(`/api/admin/comercios/${comercio.id}/reactivar`, { method: 'PATCH' })
      },
    })
  }

  async function handleConfirmAction() {
    if (!confirmDialog.action) return
    try {
      setConfirmLoading(true)
      await confirmDialog.action()
      setConfirmDialog({ isOpen: false, title: '', message: '', variant: 'danger', action: null })
      await fetchComercios()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al ejecutar la acción')
    } finally {
      setConfirmLoading(false)
    }
  }

  function handleRegistrarPago(comercio: Comercio) {
    setPagoModal({ isOpen: true, comercioId: comercio.id, comercioNombre: comercio.razonSocial })
    setPagoForm({ montoPagado: '', metodoPago: 'transferencia', referencia: '' })
    setPagoError(null)
  }

  async function handleSubmitPago(e: React.FormEvent) {
    e.preventDefault()
    if (!pagoModal.comercioId) return

    const monto = parseFloat(pagoForm.montoPagado)
    if (isNaN(monto) || monto <= 0) {
      setPagoError('Ingrese un monto válido mayor a 0')
      return
    }
    if (!pagoForm.referencia.trim()) {
      setPagoError('Ingrese una referencia de pago')
      return
    }

    try {
      setPagoLoading(true)
      setPagoError(null)
      await apiRequest(`/api/admin/comercios/${pagoModal.comercioId}/pagos`, {
        method: 'POST',
        body: {
          montoPagado: monto,
          metodoPago: pagoForm.metodoPago,
          referencia: pagoForm.referencia.trim(),
        },
      })
      setPagoModal({ isOpen: false, comercioId: null, comercioNombre: '' })
      await fetchComercios()
    } catch (err) {
      setPagoError(err instanceof Error ? err.message : 'Error al registrar el pago')
    } finally {
      setPagoLoading(false)
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Gestión de Comercios</h2>
        {/* Abre el modal cross-tenant de recuperación de contraseña de dueños (Req 13.1) */}
        <Button variant="edit" onClick={() => setShowRecuperacion(true)}>
          Recuperación de contraseña (Dueños)
        </Button>
      </div>

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-4">
        <div className="flex-1">
          <label htmlFor="busqueda" className="sr-only">
            Buscar por RUC o razón social
          </label>
          <input
            id="busqueda"
            type="text"
            placeholder="Buscar por RUC o razón social..."
            value={busqueda}
            onChange={(e) => setBusqueda(e.target.value)}
            className="w-full px-4 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-surface text-gray-900 dark:text-dark-text placeholder-gray-400 dark:placeholder-dark-text/50 focus:outline-none focus:ring-2 focus:ring-action-confirm"
          />
        </div>
        <div>
          <label htmlFor="filtro-estado" className="sr-only">
            Filtrar por estado
          </label>
          <select
            id="filtro-estado"
            value={filtroEstado}
            onChange={(e) => setFiltroEstado(e.target.value)}
            className="px-4 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-surface text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm"
          >
            {ESTADO_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Error */}
      {error && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4">
          <p className="text-red-700 dark:text-red-300">{error}</p>
        </div>
      )}

      {/* Table */}
      {loading ? (
        <div className="flex items-center justify-center py-16">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-action-confirm" />
        </div>
      ) : (
        <div className="bg-white dark:bg-dark-surface rounded-xl shadow-sm border border-gray-100 dark:border-dark-surface overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-gray-200 dark:border-gray-700">
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">RUC</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Razón Social</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Plan</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Estado</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Próximo Corte</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {comerciosFiltrados.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="px-4 py-8 text-center text-gray-400 dark:text-dark-text/50">
                      No se encontraron comercios
                    </td>
                  </tr>
                ) : (
                  comerciosFiltrados.map((comercio) => (
                    <tr
                      key={comercio.id}
                      className="border-b border-gray-100 dark:border-gray-700/50 hover:bg-gray-50 dark:hover:bg-dark-surface/80 transition-colors"
                    >
                      <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-mono text-xs">
                        {comercio.ruc}
                      </td>
                      <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-medium">
                        {comercio.razonSocial}
                      </td>
                      <td className="px-4 py-3 text-gray-700 dark:text-dark-text/80">{comercio.planNombre}</td>
                      <td className="px-4 py-3">
                        <span
                          className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getEstadoBadgeClasses(comercio.estado)}`}
                        >
                          {comercio.estado}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-gray-700 dark:text-dark-text/80">
                        {formatDateTime(comercio.fechaProximoCorte)}
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-2 flex-wrap">
                          {comercio.estado !== 'Suspendido' && (
                            <Button
                              variant="danger"
                              className="text-xs px-2 py-1"
                              onClick={() => handleSuspender(comercio)}
                            >
                              Suspender
                            </Button>
                          )}
                          {comercio.estado === 'Suspendido' && (
                            <Button
                              variant="confirm"
                              className="text-xs px-2 py-1"
                              onClick={() => handleReactivar(comercio)}
                            >
                              Reactivar
                            </Button>
                          )}
                          <Button
                            variant="edit"
                            className="text-xs px-2 py-1"
                            onClick={() => handleRegistrarPago(comercio)}
                          >
                            Registrar Pago
                          </Button>
                          <Link
                            to={`/comercios/${comercio.id}`}
                            className="text-xs font-medium text-action-confirm hover:text-action-confirm/80 transition-colors px-2 py-1"
                          >
                            Ver Detalle
                          </Link>
                        </div>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Confirm Dialog */}
      <ConfirmDialog
        isOpen={confirmDialog.isOpen}
        title={confirmDialog.title}
        message={confirmDialog.message}
        variant={confirmDialog.variant}
        confirmLabel={confirmDialog.variant === 'danger' ? 'Suspender' : 'Reactivar'}
        isLoading={confirmLoading}
        onConfirm={handleConfirmAction}
        onCancel={() => setConfirmDialog({ isOpen: false, title: '', message: '', variant: 'danger', action: null })}
      />

      {/* Payment Modal */}
      <Modal
        isOpen={pagoModal.isOpen}
        onClose={() => setPagoModal({ isOpen: false, comercioId: null, comercioNombre: '' })}
        title={`Registrar Pago — ${pagoModal.comercioNombre}`}
      >
        <form onSubmit={handleSubmitPago} className="space-y-4">
          {pagoError && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-3">
              <p className="text-sm text-red-700 dark:text-red-300">{pagoError}</p>
            </div>
          )}

          <div>
            <label htmlFor="monto" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Monto pagado ($)
            </label>
            <input
              id="monto"
              type="number"
              step="0.01"
              min="0.01"
              value={pagoForm.montoPagado}
              onChange={(e) => setPagoForm({ ...pagoForm, montoPagado: e.target.value })}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm"
              placeholder="0.00"
              required
            />
          </div>

          <div>
            <label htmlFor="metodo" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Método de pago
            </label>
            <select
              id="metodo"
              value={pagoForm.metodoPago}
              onChange={(e) => setPagoForm({ ...pagoForm, metodoPago: e.target.value })}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm"
            >
              {METODO_PAGO_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label htmlFor="referencia" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Referencia
            </label>
            <input
              id="referencia"
              type="text"
              value={pagoForm.referencia}
              onChange={(e) => setPagoForm({ ...pagoForm, referencia: e.target.value })}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm"
              placeholder="Ej: TRF-12345, Recibo #001"
              required
            />
          </div>

          <div className="flex justify-end gap-3 pt-2">
            <Button
              type="button"
              variant="neutral"
              onClick={() => setPagoModal({ isOpen: false, comercioId: null, comercioNombre: '' })}
              disabled={pagoLoading}
            >
              Cancelar
            </Button>
            <Button type="submit" variant="confirm" disabled={pagoLoading}>
              {pagoLoading ? 'Registrando...' : 'Registrar Pago'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Modal de recuperación de contraseña de dueños (cross-tenant) */}
      <RecuperacionDuenosModal
        isOpen={showRecuperacion}
        onClose={() => setShowRecuperacion(false)}
      />
    </div>
  )
}
