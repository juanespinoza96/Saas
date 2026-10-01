import { useState } from 'react'
import { Modal } from '../components/ui/Modal'
import { Button } from '../components/ui/Button'
import { crearTrial } from '../lib/api'

interface TrialCreateFormProps {
  isOpen: boolean
  onClose: () => void
  onSuccess: () => void
}

/** Regex para validar RUC ecuatoriano: exactamente 13 dígitos numéricos */
const RUC_REGEX = /^\d{13}$/

/**
 * Formulario modal para crear un nuevo comercio con período de prueba gratuita.
 * Valida RUC en tiempo real y maneja errores específicos del API.
 */
export function TrialCreateForm({ isOpen, onClose, onSuccess }: TrialCreateFormProps) {
  const [ruc, setRuc] = useState('')
  const [razonSocial, setRazonSocial] = useState('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Validaciones en tiempo real
  const rucValido = RUC_REGEX.test(ruc)
  const rucTocado = ruc.length > 0
  const razonSocialTrimmed = razonSocial.trim()
  const razonSocialValida = razonSocialTrimmed.length >= 1 && razonSocialTrimmed.length <= 200
  const razonSocialTocada = razonSocial.length > 0

  // El formulario es válido cuando ambos campos pasan validación
  const formularioValido = rucValido && razonSocialValida

  function handleRucChange(value: string) {
    // Solo permitir dígitos y máximo 13 caracteres
    const soloDigitos = value.replace(/\D/g, '').slice(0, 13)
    setRuc(soloDigitos)
    setError(null)
  }

  function handleRazonSocialChange(value: string) {
    // Limitar a 200 caracteres
    if (value.length <= 200) {
      setRazonSocial(value)
      setError(null)
    }
  }

  function resetForm() {
    setRuc('')
    setRazonSocial('')
    setError(null)
    setLoading(false)
  }

  function handleClose() {
    resetForm()
    onClose()
  }

  /**
   * Mapea errores del API a mensajes descriptivos en español.
   */
  function mapearError(err: unknown): string {
    const mensaje = err instanceof Error ? err.message : String(err)

    if (mensaje.includes('RUC_DUPLICATE')) {
      return 'Este RUC ya está registrado en el sistema'
    }
    if (mensaje.includes('TRIAL_YA_UTILIZADO')) {
      return 'Este RUC ya utilizó su período de prueba gratuita'
    }

    return mensaje || 'Error inesperado al crear el trial'
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault()

    if (!formularioValido) return

    try {
      setLoading(true)
      setError(null)

      await crearTrial({
        ruc,
        razonSocial: razonSocialTrimmed,
      })

      // Éxito: cerrar modal y notificar al padre
      resetForm()
      onSuccess()
    } catch (err) {
      setError(mapearError(err))
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title="Crear Prueba Gratuita"
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        {/* Mensaje de error general */}
        {error && (
          <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-3">
            <p className="text-sm text-red-700 dark:text-red-300">{error}</p>
          </div>
        )}

        {/* Campo RUC */}
        <div>
          <label htmlFor="trial-ruc" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
            RUC
          </label>
          <div className="relative">
            <input
              id="trial-ruc"
              type="text"
              inputMode="numeric"
              value={ruc}
              onChange={(e) => handleRucChange(e.target.value)}
              className={`w-full px-3 py-2 pr-10 rounded-lg border bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm ${
                rucTocado
                  ? rucValido
                    ? 'border-green-400 dark:border-green-600'
                    : 'border-red-400 dark:border-red-600'
                  : 'border-gray-300 dark:border-dark-surface'
              }`}
              placeholder="Ej: 1234567890001"
              maxLength={13}
              required
              disabled={loading}
            />
            {/* Indicador de validación */}
            {rucTocado && (
              <span className="absolute right-3 top-1/2 -translate-y-1/2 text-lg">
                {rucValido ? (
                  <span className="text-green-500" aria-label="RUC válido">✓</span>
                ) : (
                  <span className="text-red-500" aria-label="RUC inválido">✗</span>
                )}
              </span>
            )}
          </div>
          <p className="mt-1 text-xs text-gray-500 dark:text-dark-text/50">
            13 dígitos numéricos ({ruc.length}/13)
          </p>
        </div>

        {/* Campo Razón Social */}
        <div>
          <label htmlFor="trial-razon-social" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
            Razón Social
          </label>
          <input
            id="trial-razon-social"
            type="text"
            value={razonSocial}
            onChange={(e) => handleRazonSocialChange(e.target.value)}
            className={`w-full px-3 py-2 rounded-lg border bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm ${
              razonSocialTocada
                ? razonSocialValida
                  ? 'border-green-400 dark:border-green-600'
                  : 'border-red-400 dark:border-red-600'
                : 'border-gray-300 dark:border-dark-surface'
            }`}
            placeholder="Nombre del comercio"
            required
            disabled={loading}
          />
          {razonSocialTocada && !razonSocialValida && (
            <p className="mt-1 text-xs text-red-500 dark:text-red-400">
              {razonSocialTrimmed.length === 0
                ? 'La razón social no puede estar vacía o contener solo espacios'
                : 'La razón social debe tener entre 1 y 200 caracteres'}
            </p>
          )}
          <p className="mt-1 text-xs text-gray-500 dark:text-dark-text/50">
            {razonSocialTrimmed.length}/200 caracteres
          </p>
        </div>

        {/* Botones de acción */}
        <div className="flex justify-end gap-3 pt-2">
          <Button
            type="button"
            variant="neutral"
            onClick={handleClose}
            disabled={loading}
          >
            Cancelar
          </Button>
          <Button
            type="submit"
            variant="confirm"
            disabled={!formularioValido || loading}
          >
            {loading ? 'Creando...' : 'Crear Trial'}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
