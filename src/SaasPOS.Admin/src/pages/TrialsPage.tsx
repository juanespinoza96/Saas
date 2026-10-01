import { useEffect, useState } from 'react'
import { obtenerTrials, obtenerResumenTrials, convertirTrial, extenderTrial, apiRequest } from '../lib/api'
import { formatDateTime } from '../lib/dateUtils'
import { Button } from '../components/ui/Button'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { TrialCreateForm } from './TrialCreateForm'
import type { TrialDto, TrialResumenDto, ConversionResultDto, ExtensionResultDto } from '../types/trial'

/** Planes disponibles para conversión */
const PLANES_DISPONIBLES = [
  { id: 1, nombre: 'Básico', precio: 350 },
  { id: 2, nombre: 'Intermedio', precio: 750 },
  { id: 3, nombre: 'Empresarial', precio: 1200 },
]

export function TrialsPage() {
  // --- Estado principal ---
  const [trials, setTrials] = useState<TrialDto[]>([])
  const [resumen, setResumen] = useState<TrialResumenDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // --- Estado para crear trial ---
  const [showCreateForm, setShowCreateForm] = useState(false)

  // --- Estado modal conversión ---
  const [convertModal, setConvertModal] = useState<{
    isOpen: boolean
    suscripcionId: number | null
    razonSocial: string
  }>({ isOpen: false, suscripcionId: null, razonSocial: '' })
  const [planSeleccionado, setPlanSeleccionado] = useState<number>(PLANES_DISPONIBLES[0]!.id)
  const [convertLoading, setConvertLoading] = useState(false)
  const [convertError, setConvertError] = useState<string | null>(null)
  const [convertResult, setConvertResult] = useState<ConversionResultDto | null>(null)

  // --- Estado modal extensión ---
  const [extendModal, setExtendModal] = useState<{
    isOpen: boolean
    suscripcionId: number | null
    razonSocial: string
  }>({ isOpen: false, suscripcionId: null, razonSocial: '' })
  const [diasExtension, setDiasExtension] = useState<number>(7)
  const [extendLoading, setExtendLoading] = useState(false)
  const [extendError, setExtendError] = useState<string | null>(null)
  const [extendResult, setExtendResult] = useState<ExtensionResultDto | null>(null)

  // --- Estado diálogo suspender ---
  const [suspendDialog, setSuspendDialog] = useState<{
    isOpen: boolean
    suscripcionId: number | null
    comercioId: number | null
    razonSocial: string
  }>({ isOpen: false, suscripcionId: null, comercioId: null, razonSocial: '' })
  const [suspendLoading, setSuspendLoading] = useState(false)

  // --- Carga de datos ---
  useEffect(() => {
    fetchData()
  }, [])

  async function fetchData() {
    try {
      setLoading(true)
      setError(null)
      const [trialsData, resumenData] = await Promise.all([
        obtenerTrials(),
        obtenerResumenTrials(),
      ])
      // Ordenar por días restantes ascendente (expirados primero)
      trialsData.sort((a, b) => a.diasRestantes - b.diasRestantes)
      setTrials(trialsData)
      setResumen(resumenData)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al cargar los datos de trials')
    } finally {
      setLoading(false)
    }
  }

  // --- Acciones ---

  function handleConvertir(trial: TrialDto) {
    setConvertModal({ isOpen: true, suscripcionId: trial.suscripcionId, razonSocial: trial.razonSocial })
    setPlanSeleccionado(PLANES_DISPONIBLES[0]!.id)
    setConvertError(null)
    setConvertResult(null)
  }

  async function handleSubmitConversion(e: React.FormEvent) {
    e.preventDefault()
    if (!convertModal.suscripcionId) return

    try {
      setConvertLoading(true)
      setConvertError(null)
      const result = await convertirTrial(convertModal.suscripcionId, { planId: planSeleccionado })
      setConvertResult(result)
      await fetchData()
    } catch (err) {
      setConvertError(err instanceof Error ? err.message : 'Error al convertir el trial')
    } finally {
      setConvertLoading(false)
    }
  }

  function handleExtender(trial: TrialDto) {
    setExtendModal({ isOpen: true, suscripcionId: trial.suscripcionId, razonSocial: trial.razonSocial })
    setDiasExtension(7)
    setExtendError(null)
    setExtendResult(null)
  }

  async function handleSubmitExtension(e: React.FormEvent) {
    e.preventDefault()
    if (!extendModal.suscripcionId) return

    if (diasExtension < 1 || diasExtension > 15) {
      setExtendError('Los días deben estar entre 1 y 15')
      return
    }

    try {
      setExtendLoading(true)
      setExtendError(null)
      const result = await extenderTrial(extendModal.suscripcionId, { diasAdicionales: diasExtension })
      setExtendResult(result)
      await fetchData()
    } catch (err) {
      setExtendError(err instanceof Error ? err.message : 'Error al extender el trial')
    } finally {
      setExtendLoading(false)
    }
  }

  function handleSuspender(trial: TrialDto) {
    setSuspendDialog({
      isOpen: true,
      suscripcionId: trial.suscripcionId,
      comercioId: trial.comercioId,
      razonSocial: trial.razonSocial,
    })
  }

  async function handleConfirmSuspend() {
    if (!suspendDialog.comercioId) return

    try {
      setSuspendLoading(true)
      await apiRequest(`/api/admin/comercios/${suspendDialog.comercioId}/suspender`, { method: 'PATCH' })
      setSuspendDialog({ isOpen: false, suscripcionId: null, comercioId: null, razonSocial: '' })
      await fetchData()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al suspender el comercio')
    } finally {
      setSuspendLoading(false)
    }
  }

  // --- Renderizado ---
  return (
    <div className="space-y-6">
      {/* Encabezado */}
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
          Gestión de Pruebas Gratuitas
        </h2>
        <Button variant="confirm" onClick={() => setShowCreateForm(true)}>
          Nuevo Trial
        </Button>
      </div>

      {/* Tarjetas resumen */}
      {resumen && (
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-xl p-4">
            <p className="text-sm font-medium text-blue-600 dark:text-blue-300">Total Activos</p>
            <p className="text-3xl font-bold text-blue-800 dark:text-blue-200 mt-1">{resumen.totalActivos}</p>
          </div>
          <div className="bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-800 rounded-xl p-4">
            <p className="text-sm font-medium text-yellow-600 dark:text-yellow-300">Por Expirar (≤5 días)</p>
            <p className="text-3xl font-bold text-yellow-800 dark:text-yellow-200 mt-1">{resumen.porExpirar}</p>
          </div>
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4">
            <p className="text-sm font-medium text-red-600 dark:text-red-300">Expirados</p>
            <p className="text-3xl font-bold text-red-800 dark:text-red-200 mt-1">{resumen.expirados}</p>
          </div>
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4 flex items-center justify-between">
          <p className="text-red-700 dark:text-red-300">{error}</p>
          <Button variant="neutral" className="text-xs" onClick={fetchData}>
            Reintentar
          </Button>
        </div>
      )}

      {/* Tabla de trials */}
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
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Razón Social</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">RUC</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Fecha Inicio</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Próximo Corte</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Días Restantes</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Estado</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500 dark:text-dark-text/70">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {trials.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="px-4 py-8 text-center text-gray-400 dark:text-dark-text/50">
                      No hay trials registrados
                    </td>
                  </tr>
                ) : (
                  trials.map((trial) => {
                    const esUrgente = trial.diasRestantes <= 3 && trial.estado === 'Trial'
                    return (
                      <tr
                        key={trial.suscripcionId}
                        className={`border-b border-gray-100 dark:border-gray-700/50 hover:bg-gray-50 dark:hover:bg-dark-surface/80 transition-colors ${
                          esUrgente ? 'border-l-4 border-l-red-500 bg-red-50/50 dark:bg-red-900/10' : ''
                        }`}
                      >
                        <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-medium">
                          {trial.razonSocial}
                        </td>
                        <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-mono text-xs">
                          {trial.ruc}
                        </td>
                        <td className="px-4 py-3 text-gray-700 dark:text-dark-text/80">
                          {formatDateTime(trial.fechaInicio)}
                        </td>
                        <td className="px-4 py-3 text-gray-700 dark:text-dark-text/80">
                          {formatDateTime(trial.fechaProximoCorte)}
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                              trial.diasRestantes === 0
                                ? 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-300'
                                : trial.diasRestantes <= 3
                                  ? 'bg-orange-100 text-orange-800 dark:bg-orange-900/30 dark:text-orange-300'
                                  : trial.diasRestantes <= 5
                                    ? 'bg-yellow-100 text-yellow-800 dark:bg-yellow-900/30 dark:text-yellow-300'
                                    : 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-300'
                            }`}
                          >
                            {trial.diasRestantes} días
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <span
                            className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                              trial.estado === 'Trial'
                                ? 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300'
                                : 'bg-gray-100 text-gray-600 dark:bg-gray-700/30 dark:text-gray-400'
                            }`}
                          >
                            {trial.estado === 'Trial' ? 'Trial' : 'Expirado'}
                          </span>
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-2 flex-wrap">
                            {/* Convertir a Pago: disponible en Trial y Trial_Expirado */}
                            {(trial.estado === 'Trial' || trial.estado === 'Trial_Expirado') && (
                              <Button
                                variant="confirm"
                                className="text-xs px-2 py-1"
                                onClick={() => handleConvertir(trial)}
                              >
                                Convertir
                              </Button>
                            )}
                            {/* Extender Trial: solo en Trial */}
                            {trial.estado === 'Trial' && (
                              <Button
                                variant="edit"
                                className="text-xs px-2 py-1"
                                onClick={() => handleExtender(trial)}
                              >
                                Extender
                              </Button>
                            )}
                            {/* Suspender: solo en Trial */}
                            {trial.estado === 'Trial' && (
                              <Button
                                variant="danger"
                                className="text-xs px-2 py-1"
                                onClick={() => handleSuspender(trial)}
                              >
                                Suspender
                              </Button>
                            )}
                          </div>
                        </td>
                      </tr>
                    )
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Formulario de creación de trial */}
      <TrialCreateForm
        isOpen={showCreateForm}
        onClose={() => setShowCreateForm(false)}
        onSuccess={() => {
          setShowCreateForm(false)
          fetchData()
        }}
      />

      {/* Modal Convertir a Pago */}
      <Modal
        isOpen={convertModal.isOpen}
        onClose={() => setConvertModal({ isOpen: false, suscripcionId: null, razonSocial: '' })}
        title={`Convertir a Pago — ${convertModal.razonSocial}`}
      >
        {convertResult ? (
          <div className="space-y-4">
            <div className="bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg p-4">
              <p className="text-green-700 dark:text-green-300 font-medium">¡Conversión exitosa!</p>
              <ul className="mt-2 text-sm text-green-600 dark:text-green-400 space-y-1">
                <li>Plan: {convertResult.nombrePlan}</li>
                <li>Cuota mensual: ${convertResult.montoCuota}</li>
                <li>Próximo corte: {formatDateTime(convertResult.fechaProximoCorte)}</li>
              </ul>
            </div>
            <div className="flex justify-end">
              <Button
                variant="neutral"
                onClick={() => setConvertModal({ isOpen: false, suscripcionId: null, razonSocial: '' })}
              >
                Cerrar
              </Button>
            </div>
          </div>
        ) : (
          <form onSubmit={handleSubmitConversion} className="space-y-4">
            {convertError && (
              <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-3">
                <p className="text-sm text-red-700 dark:text-red-300">{convertError}</p>
              </div>
            )}

            <div>
              <label htmlFor="plan-select" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
                Seleccione el plan
              </label>
              <select
                id="plan-select"
                value={planSeleccionado}
                onChange={(e) => setPlanSeleccionado(Number(e.target.value))}
                className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm"
              >
                {PLANES_DISPONIBLES.map((plan) => (
                  <option key={plan.id} value={plan.id}>
                    {plan.nombre} — ${plan.precio}/mes
                  </option>
                ))}
              </select>
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <Button
                type="button"
                variant="neutral"
                onClick={() => setConvertModal({ isOpen: false, suscripcionId: null, razonSocial: '' })}
                disabled={convertLoading}
              >
                Cancelar
              </Button>
              <Button type="submit" variant="confirm" disabled={convertLoading}>
                {convertLoading ? 'Procesando...' : 'Confirmar Conversión'}
              </Button>
            </div>
          </form>
        )}
      </Modal>

      {/* Modal Extender Trial */}
      <Modal
        isOpen={extendModal.isOpen}
        onClose={() => setExtendModal({ isOpen: false, suscripcionId: null, razonSocial: '' })}
        title={`Extender Trial — ${extendModal.razonSocial}`}
      >
        {extendResult ? (
          <div className="space-y-4">
            <div className="bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg p-4">
              <p className="text-green-700 dark:text-green-300 font-medium">¡Extensión aplicada!</p>
              <p className="mt-2 text-sm text-green-600 dark:text-green-400">
                Nueva fecha de corte: {formatDateTime(extendResult.nuevaFechaProximoCorte)}
              </p>
            </div>
            <div className="flex justify-end">
              <Button
                variant="neutral"
                onClick={() => setExtendModal({ isOpen: false, suscripcionId: null, razonSocial: '' })}
              >
                Cerrar
              </Button>
            </div>
          </div>
        ) : (
          <form onSubmit={handleSubmitExtension} className="space-y-4">
            {extendError && (
              <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-3">
                <p className="text-sm text-red-700 dark:text-red-300">{extendError}</p>
              </div>
            )}

            <div>
              <label htmlFor="dias-extension" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
                Días adicionales (1–15)
              </label>
              <input
                id="dias-extension"
                type="number"
                min={1}
                max={15}
                value={diasExtension}
                onChange={(e) => setDiasExtension(Number(e.target.value))}
                className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm"
                required
              />
            </div>

            <div className="flex justify-end gap-3 pt-2">
              <Button
                type="button"
                variant="neutral"
                onClick={() => setExtendModal({ isOpen: false, suscripcionId: null, razonSocial: '' })}
                disabled={extendLoading}
              >
                Cancelar
              </Button>
              <Button type="submit" variant="edit" disabled={extendLoading}>
                {extendLoading ? 'Procesando...' : 'Confirmar Extensión'}
              </Button>
            </div>
          </form>
        )}
      </Modal>

      {/* Diálogo Suspender */}
      <ConfirmDialog
        isOpen={suspendDialog.isOpen}
        title="Suspender Comercio en Trial"
        message={`¿Está seguro que desea suspender el comercio "${suspendDialog.razonSocial}"? El comercio perderá acceso inmediatamente.`}
        variant="danger"
        confirmLabel="Suspender"
        isLoading={suspendLoading}
        onConfirm={handleConfirmSuspend}
        onCancel={() => setSuspendDialog({ isOpen: false, suscripcionId: null, comercioId: null, razonSocial: '' })}
      />
    </div>
  )
}
