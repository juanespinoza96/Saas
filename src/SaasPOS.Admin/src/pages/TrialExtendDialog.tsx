import { useState } from 'react'
import { Modal } from '../components/ui/Modal'
import { Button } from '../components/ui/Button'
import { extenderTrial } from '../lib/api'
import { formatDateTime } from '../lib/dateUtils'

interface TrialExtendDialogProps {
  isOpen: boolean
  onClose: () => void
  onSuccess: () => void
  suscripcionId: number
  razonSocial: string
}

/** Mensajes de error según código del backend */
const ERROR_MESSAGES: Record<string, string> = {
  EXTENSION_NO_APLICA: 'Solo se pueden extender trials activos',
  DIAS_FUERA_DE_RANGO: 'Los días deben estar entre 1 y 15',
  LIMITE_EXTENSION_SUPERADO: 'Se ha superado el límite máximo de 30 días de extensión acumulados',
}

/**
 * Extrae el código de error del mensaje de la excepción del API.
 * El backend retorna { error: string, code: string } en el body del 400.
 */
function getErrorCode(error: unknown): string | null {
  if (error instanceof Error) {
    for (const code of Object.keys(ERROR_MESSAGES)) {
      if (error.message.includes(code)) return code
    }
  }
  return null
}

export function TrialExtendDialog({
  isOpen,
  onClose,
  onSuccess,
  suscripcionId,
  razonSocial,
}: TrialExtendDialogProps) {
  const [diasAdicionales, setDiasAdicionales] = useState<number>(1)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [nuevaFecha, setNuevaFecha] = useState<string | null>(null)

  /** Resetea el estado interno al cerrar */
  function handleClose() {
    setDiasAdicionales(1)
    setLoading(false)
    setError(null)
    setNuevaFecha(null)
    onClose()
  }

  /** Ejecuta la extensión del trial */
  async function handleConfirm() {
    if (diasAdicionales < 1 || diasAdicionales > 15) {
      setError('Los días deben estar entre 1 y 15')
      return
    }

    setLoading(true)
    setError(null)

    try {
      const response = await extenderTrial(suscripcionId, { diasAdicionales })
      setNuevaFecha(response.nuevaFechaProximoCorte)

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
        setError(err instanceof Error ? err.message : 'Error al extender el trial')
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal isOpen={isOpen} onClose={handleClose} title={`Extender Trial — ${razonSocial}`}>
      {/* Resultado exitoso */}
      {nuevaFecha ? (
        <div className="space-y-3">
          <div className="bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg p-4">
            <p className="text-green-800 dark:text-green-300 font-medium">
              ✓ Trial extendido exitosamente
            </p>
            <p className="text-sm text-green-700 dark:text-green-400 mt-1">
              Nueva fecha de corte: <span className="font-semibold">{formatDateTime(nuevaFecha)}</span>
            </p>
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

          {/* Input de días */}
          <div>
            <label
              htmlFor="dias-adicionales"
              className="block text-sm font-medium text-gray-700 dark:text-dark-muted mb-1"
            >
              Días adicionales (1-15)
            </label>
            <input
              id="dias-adicionales"
              type="number"
              min={1}
              max={15}
              value={diasAdicionales}
              onChange={(e) => setDiasAdicionales(Number(e.target.value))}
              className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-border
                bg-white dark:bg-dark-surface text-gray-900 dark:text-dark-text
                focus:outline-none focus:ring-2 focus:ring-action-confirm focus:border-transparent
                [appearance:textfield] [&::-webkit-outer-spin-button]:appearance-none [&::-webkit-inner-spin-button]:appearance-none"
            />
            <p className="text-xs text-gray-500 dark:text-dark-muted mt-1">
              Máximo acumulado de extensiones: 30 días
            </p>
          </div>

          {/* Botones de acción */}
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="neutral" onClick={handleClose} disabled={loading}>
              Cancelar
            </Button>
            <Button
              variant="confirm"
              onClick={handleConfirm}
              disabled={loading || diasAdicionales < 1 || diasAdicionales > 15}
            >
              {loading ? 'Extendiendo...' : 'Confirmar Extensión'}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  )
}
