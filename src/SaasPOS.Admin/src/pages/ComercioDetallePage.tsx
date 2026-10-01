import { useState, useEffect, useCallback } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { apiRequest } from '../lib/api'
import { formatDateTime } from '../lib/dateUtils'
import { Button } from '../components/ui/Button'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { RecuperacionDuenosModal } from '../components/RecuperacionDuenosModal'

interface Comercio {
  id: number
  ruc: string
  razonSocial: string
  planNombre: string
  planId: number
  estado: string
  fechaRegistro: string
  fechaProximoCorte: string
  usaFacturacionSRI: boolean
  activo: boolean
}

interface Usuario {
  id: number
  nombre: string
  email: string
  rol: string
  activo: boolean
}

interface Pago {
  id: number
  montoPagado: number
  fechaPago: string
  metodoPago: string
  referencia: string
}

interface Plan {
  id: number
  nombre: string
}

type TabKey = 'info' | 'usuarios' | 'pagos'

export function ComercioDetallePage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [comercio, setComercio] = useState<Comercio | null>(null)
  const [usuarios, setUsuarios] = useState<Usuario[]>([])
  const [pagos, setPagos] = useState<Pago[]>([])
  const [planes, setPlanes] = useState<Plan[]>([])
  const [loading, setLoading] = useState(true)
  const [activeTab, setActiveTab] = useState<TabKey>('info')

  // Action states
  const [confirmSuspend, setConfirmSuspend] = useState(false)
  const [confirmReactivate, setConfirmReactivate] = useState(false)
  const [showChangePlan, setShowChangePlan] = useState(false)
  const [showRegisterPayment, setShowRegisterPayment] = useState(false)
  const [showRecuperacion, setShowRecuperacion] = useState(false)
  const [actionLoading, setActionLoading] = useState(false)

  // Change plan form
  const [selectedPlanId, setSelectedPlanId] = useState<number>(0)

  // Register payment form
  const [paymentMonto, setPaymentMonto] = useState('')
  const [paymentMetodo, setPaymentMetodo] = useState('')
  const [paymentReferencia, setPaymentReferencia] = useState('')

  const fetchData = useCallback(async () => {
    if (!id) return
    setLoading(true)
    try {
      const [comercioData, usuariosData, pagosData, planesData] = await Promise.all([
        apiRequest<Comercio>(`/api/admin/comercios/${id}`),
        apiRequest<Usuario[]>(`/api/admin/comercios/${id}/usuarios`),
        apiRequest<Pago[]>(`/api/admin/comercios/${id}/pagos`),
        apiRequest<Plan[]>('/api/admin/planes'),
      ])
      setComercio(comercioData)
      setUsuarios(usuariosData)
      setPagos(pagosData)
      setPlanes(planesData)
      setSelectedPlanId(comercioData.planId)
    } catch (error) {
      console.error('Error loading commerce detail:', error)
    } finally {
      setLoading(false)
    }
  }, [id])

  useEffect(() => {
    fetchData()
  }, [fetchData])

  const handleSuspend = async () => {
    setActionLoading(true)
    try {
      await apiRequest(`/api/admin/comercios/${id}/suspender`, { method: 'PATCH' })
      await fetchData()
    } catch (error) {
      console.error('Error suspending commerce:', error)
    } finally {
      setActionLoading(false)
      setConfirmSuspend(false)
    }
  }

  const handleReactivate = async () => {
    setActionLoading(true)
    try {
      await apiRequest(`/api/admin/comercios/${id}/reactivar`, { method: 'PATCH' })
      await fetchData()
    } catch (error) {
      console.error('Error reactivating commerce:', error)
    } finally {
      setActionLoading(false)
      setConfirmReactivate(false)
    }
  }

  const handleChangePlan = async () => {
    setActionLoading(true)
    try {
      await apiRequest(`/api/admin/comercios/${id}/plan`, {
        method: 'PATCH',
        body: { planId: selectedPlanId },
      })
      await fetchData()
    } catch (error) {
      console.error('Error changing plan:', error)
    } finally {
      setActionLoading(false)
      setShowChangePlan(false)
    }
  }

  const handleRegisterPayment = async (e: React.FormEvent) => {
    e.preventDefault()
    setActionLoading(true)
    try {
      await apiRequest(`/api/admin/comercios/${id}/pagos`, {
        method: 'POST',
        body: {
          montoPagado: parseFloat(paymentMonto),
          metodoPago: paymentMetodo,
          referencia: paymentReferencia,
        },
      })
      setPaymentMonto('')
      setPaymentMetodo('')
      setPaymentReferencia('')
      setShowRegisterPayment(false)
      await fetchData()
    } catch (error) {
      console.error('Error registering payment:', error)
    } finally {
      setActionLoading(false)
    }
  }

  if (loading) {
    return (
      <div className="space-y-6">
        <div className="animate-pulse space-y-4">
          <div className="h-8 bg-gray-200 dark:bg-dark-surface rounded w-1/3"></div>
          <div className="h-4 bg-gray-200 dark:bg-dark-surface rounded w-1/2"></div>
          <div className="h-64 bg-gray-200 dark:bg-dark-surface rounded"></div>
        </div>
      </div>
    )
  }

  if (!comercio) {
    return (
      <div className="space-y-6">
        <p className="text-red-500">No se pudo cargar la información del comercio.</p>
        <Button variant="neutral" onClick={() => navigate('/comercios')}>
          ← Volver a Comercios
        </Button>
      </div>
    )
  }

  const tabs: { key: TabKey; label: string }[] = [
    { key: 'info', label: 'Info General' },
    { key: 'usuarios', label: `Usuarios (${usuarios.length})` },
    { key: 'pagos', label: `Pagos (${pagos.length})` },
  ]

  return (
    <div className="space-y-6">
      {/* Back button */}
      <button
        onClick={() => navigate('/comercios')}
        className="text-sm text-gray-500 dark:text-dark-text/60 hover:text-gray-700 dark:hover:text-dark-text flex items-center gap-1"
      >
        ← Volver a Comercios
      </button>

      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
            {comercio.razonSocial}
          </h2>
          <p className="text-gray-500 dark:text-dark-text/70 text-sm mt-1">
            RUC: {comercio.ruc} · Plan: {comercio.planNombre}
          </p>
        </div>
        <span
          className={`inline-flex px-3 py-1 rounded-full text-xs font-semibold w-fit ${
            comercio.activo
              ? 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400'
              : 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400'
          }`}
        >
          {comercio.estado}
        </span>
      </div>

      {/* Quick Actions */}
      <div className="flex flex-wrap gap-3">
        {comercio.activo ? (
          <Button variant="danger" onClick={() => setConfirmSuspend(true)}>
            Suspender
          </Button>
        ) : (
          <Button variant="confirm" onClick={() => setConfirmReactivate(true)}>
            Reactivar
          </Button>
        )}
        <Button variant="edit" onClick={() => setShowChangePlan(true)}>
          Cambiar Plan
        </Button>
        <Button variant="confirm" onClick={() => setShowRegisterPayment(true)}>
          Registrar Pago
        </Button>
        {/* Abre el modal cross-tenant de recuperación de contraseña de dueños (Req 13.1) */}
        <Button variant="edit" onClick={() => setShowRecuperacion(true)}>
          Recuperación de contraseña (Dueños)
        </Button>
      </div>

      {/* Tabs */}
      <div className="border-b border-gray-200 dark:border-dark-surface">
        <nav className="flex gap-4">
          {tabs.map((tab) => (
            <button
              key={tab.key}
              onClick={() => setActiveTab(tab.key)}
              className={`pb-3 px-1 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.key
                  ? 'border-action-confirm text-action-confirm'
                  : 'border-transparent text-gray-500 dark:text-dark-text/60 hover:text-gray-700 dark:hover:text-dark-text'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {/* Tab Content */}
      <div className="bg-white dark:bg-dark-surface rounded-xl p-6 shadow-sm border border-gray-100 dark:border-dark-surface">
        {activeTab === 'info' && (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <InfoRow label="Fecha de Registro" value={formatDateTime(comercio.fechaRegistro)} />
            <InfoRow label="Próximo Corte" value={formatDateTime(comercio.fechaProximoCorte)} />
            <InfoRow label="Facturación SRI" value={comercio.usaFacturacionSRI ? 'Sí' : 'No'} />
            <InfoRow label="Plan Actual" value={comercio.planNombre} />
            <InfoRow label="Estado" value={comercio.estado} />
            <InfoRow label="RUC" value={comercio.ruc} />
          </div>
        )}

        {activeTab === 'usuarios' && (
          <div className="overflow-x-auto">
            {usuarios.length === 0 ? (
              <p className="text-gray-400 dark:text-dark-text/50 text-center py-4">
                No hay usuarios registrados.
              </p>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-gray-500 dark:text-dark-text/60 border-b border-gray-100 dark:border-dark-surface/50">
                    <th className="pb-3 font-medium">Nombre</th>
                    <th className="pb-3 font-medium">Email</th>
                    <th className="pb-3 font-medium">Rol</th>
                    <th className="pb-3 font-medium">Estado</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-50 dark:divide-dark-surface/50">
                  {usuarios.map((u) => (
                    <tr key={u.id} className="text-gray-700 dark:text-dark-text">
                      <td className="py-3">{u.nombre}</td>
                      <td className="py-3">{u.email}</td>
                      <td className="py-3">{u.rol}</td>
                      <td className="py-3">
                        <span
                          className={`inline-flex px-2 py-0.5 rounded text-xs font-medium ${
                            u.activo
                              ? 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400'
                              : 'bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-400'
                          }`}
                        >
                          {u.activo ? 'Activo' : 'Inactivo'}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}

        {activeTab === 'pagos' && (
          <div className="overflow-x-auto">
            {pagos.length === 0 ? (
              <p className="text-gray-400 dark:text-dark-text/50 text-center py-4">
                No hay pagos registrados.
              </p>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-gray-500 dark:text-dark-text/60 border-b border-gray-100 dark:border-dark-surface/50">
                    <th className="pb-3 font-medium">Fecha</th>
                    <th className="pb-3 font-medium">Monto</th>
                    <th className="pb-3 font-medium">Método</th>
                    <th className="pb-3 font-medium">Referencia</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-50 dark:divide-dark-surface/50">
                  {pagos.map((p) => (
                    <tr key={p.id} className="text-gray-700 dark:text-dark-text">
                      <td className="py-3">{formatDateTime(p.fechaPago)}</td>
                      <td className="py-3 font-medium">${p.montoPagado.toFixed(2)}</td>
                      <td className="py-3">{p.metodoPago}</td>
                      <td className="py-3 text-gray-500 dark:text-dark-text/60">{p.referencia}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        )}
      </div>

      {/* Confirm Suspend Dialog */}
      <ConfirmDialog
        isOpen={confirmSuspend}
        onClose={() => setConfirmSuspend(false)}
        onConfirm={handleSuspend}
        title="Suspender Comercio"
        message={`¿Está seguro de suspender "${comercio.razonSocial}"? El comercio perderá acceso al sistema hasta que sea reactivado.`}
        confirmLabel="Suspender"
        variant="danger"
        loading={actionLoading}
      />

      {/* Confirm Reactivate Dialog */}
      <ConfirmDialog
        isOpen={confirmReactivate}
        onClose={() => setConfirmReactivate(false)}
        onConfirm={handleReactivate}
        title="Reactivar Comercio"
        message={`¿Está seguro de reactivar "${comercio.razonSocial}"?`}
        confirmLabel="Reactivar"
        variant="confirm"
        loading={actionLoading}
      />

      {/* Change Plan Modal */}
      <Modal isOpen={showChangePlan} onClose={() => setShowChangePlan(false)} title="Cambiar Plan">
        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Nuevo Plan
            </label>
            <select
              value={selectedPlanId}
              onChange={(e) => setSelectedPlanId(Number(e.target.value))}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            >
              {planes.map((plan) => (
                <option key={plan.id} value={plan.id}>
                  {plan.nombre}
                </option>
              ))}
            </select>
          </div>
          <div className="flex justify-end gap-3">
            <Button variant="neutral" onClick={() => setShowChangePlan(false)}>
              Cancelar
            </Button>
            <Button
              variant="edit"
              onClick={handleChangePlan}
              disabled={actionLoading || selectedPlanId === comercio.planId}
            >
              {actionLoading ? 'Guardando...' : 'Cambiar Plan'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Register Payment Modal */}
      <Modal
        isOpen={showRegisterPayment}
        onClose={() => setShowRegisterPayment(false)}
        title="Registrar Pago"
      >
        <form onSubmit={handleRegisterPayment} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Monto ($)
            </label>
            <input
              type="number"
              step="0.01"
              min="0.01"
              value={paymentMonto}
              onChange={(e) => setPaymentMonto(e.target.value)}
              required
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
              placeholder="0.00"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Método de Pago
            </label>
            <select
              value={paymentMetodo}
              onChange={(e) => setPaymentMetodo(e.target.value)}
              required
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            >
              <option value="">Seleccionar...</option>
              <option value="Transferencia">Transferencia</option>
              <option value="Efectivo">Efectivo</option>
              <option value="Tarjeta">Tarjeta</option>
            </select>
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
              Referencia
            </label>
            <input
              type="text"
              value={paymentReferencia}
              onChange={(e) => setPaymentReferencia(e.target.value)}
              required
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface dark:bg-dark-bg dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
              placeholder="Nro. de comprobante"
            />
          </div>
          <div className="flex justify-end gap-3">
            <Button variant="neutral" type="button" onClick={() => setShowRegisterPayment(false)}>
              Cancelar
            </Button>
            <Button variant="confirm" type="submit" disabled={actionLoading}>
              {actionLoading ? 'Registrando...' : 'Registrar Pago'}
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

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col">
      <span className="text-xs text-gray-500 dark:text-dark-text/60 uppercase tracking-wide">
        {label}
      </span>
      <span className="text-gray-900 dark:text-dark-text font-medium mt-1">{value}</span>
    </div>
  )
}


