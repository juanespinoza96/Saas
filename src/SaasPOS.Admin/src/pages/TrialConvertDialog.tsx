import { useState } from 'react'
import { Modal } from '../components/ui/Modal'
import { Button } from '../components/ui/Button'
import { convertirTrial } from '../lib/api'
import { formatDateTime } from '../lib/dateUtils'
import type { ConversionResultDto } from '../types/trial'

interface TrialConvertDialogProps {
  isOpen: boolean
  onClose: () => void
  onSuccess: () => void
  suscripcionId: number
  razonSocial: string
}

/** Planes disponibles para conversión */
const PLANES = [
  { id: 1, nombre: 'Básico', precio: 350 },
  { id: 2, nombre: 'Intermedio', precio: 750 },
  { id: 3, nombre: 'Empresarial', precio: 1200 },
] as const

/** Mensajes de error según código del backend */
const ERROR_MESSAGES: Record<string, string> = {
  CONVERSION_NO_APLICA: 'Esta suscripción no puede ser convertida (no está en estado Trial)',
  PLAN_INVALIDO: 'El plan seleccionado no es válido',
}

/**
 * Extrae el código de error del mensaje de la excepción del API.
 * El backend retorna { error: string, code: string } en el body del 400.
 */
function getErrorCode(error: unknown): string | null {
  if (error instanceof Error) {
    // Intentar extraer código del mensaje
    for (const code of Object.keys(ERROR_MESSAGES)) {
      if (error.message.includes(code)) return code
    }
  }
  return null
}

export function TrialConvertDialog({
  isOpen,
  onClose,
  onSuccess,
  suscripcionId,
  razonSocial,
}: TrialConvertDialogProps) {
  const [selectedPlanId, setSelectedPlanId] = useState<number | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ConversionResultDto | null>(null)

  /** Resetea el estado interno al cerrar */
  function handleClose() {
    setSelectedPlanId(null)
    setLoading(false)
    setError(null)
    setResult(null)
    onClose()
  }

  /** Ejecuta la conversión del trial al plan seleccionado */
  async function handleConfirm() {
    if (!selectedPlanId) return

    setLoading(true)
    setError(null)

    try {
      const response = await convertirTrial(suscripcionId, { planId: selectedPlanId })
      setResult(response)

      // Mostrar resultado brevemente y luego notificar éxito
      setTimeout(() => {
        handleClose()
        onSuccess()
      }, 2500)
    } catch (err: unknown) {
      const code = getErrorCode(err)
      if (code && ERROR_MESSAGES[code]) {
        setError(ERROR_MESSAGES[code])
      } else {
        setError(err instanceof Error ? err.message : 'Error al convertir el trial')
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal isOpen={isOpen} onClose={handleClose} title={`Convertir a Plan Pago — ${razonSocial}`}>
      {/* Resultado exitoso */}
      {result ? (
        <div className="space-y-3">
          <div className="bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg p-4">
            <p className="text-green-800 dark:text-green-300 font-medium mb-2">
              ✓ Conversión exitosa
            </p>
            <ul className="text-sm text-green-700 dark:text-green-400 space-y-1">
              <li><span className="font-medium">Plan:</span> {result.nombrePlan}</li>
              <li><span className="font-medium">Cuota mensual:</span> ${result.montoCuota}</li>
              <li><span className="font-medium">Próximo corte:</span> {formatDateTime(result.fechaProximoCorte)}</li>
            </ul>
          </div>
        </div>
      ) : (
        <div className="space-y-4">
          {/* Mensaje de error */}
          {error && (
            <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-3">
              <p className="text-red-700 dark:text-red-400 text-sm">{error}</p>
            </div>
          )}

          {/* Selección de plan */}
          <p className="text-sm text-gray-600 dark:text-dark-muted">
            Selecciona el plan al que se convertirá esta suscripción:
          </p>

          <div className="space-y-2">
            {PLANES.map((plan) => (
              <label
                key={plan.id}
                className={`flex items-center gap-3 p-3 rounded-lg border cursor-pointer transition-colors
                  ${selectedPlanId === plan.id
                    ? 'border-action-confirm bg-action-confirm/5 dark:bg-action-confirm/10'
                    : 'border-gray-200 dark:border-dark-border hover:border-gray-300 dark:hover:border-dark-border/80'
                  }`}
              >
                <input
                  type="radio"
                  name="plan"
                  value={plan.id}
                  checked={selectedPlanId === plan.id}
                  onChange={() => setSelectedPlanId(plan.id)}
                  className="w-4 h-4 text-action-confirm focus:ring-action-confirm"
                />
                <div className="flex-1">
                  <span className="font-medium text-gray-900 dark:text-dark-text">
                    {plan.nombre}
                  </span>
                </div>
                <span className="text-sm font-semibold text-gray-700 dark:text-dark-muted">
                  ${plan.precio}/mes
                </span>
              </label>
            ))}
          </div>

          {/* Botones de acción */}
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="neutral" onClick={handleClose} disabled={loading}>
              Cancelar
            </Button>
            <Button
              variant="confirm"
              onClick={handleConfirm}
              disabled={!selectedPlanId || loading}
            >
              {loading ? 'Convirtiendo...' : 'Confirmar Conversión'}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  )
}
