import { useState, useEffect, useCallback } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { api } from '../lib/api'
import { Button } from '../components/ui/Button'
import type { ApiError } from '../lib/api'

/** Tipos de comprobante soportados por el sistema */
const TIPOS_COMPROBANTE = ['Ticket Digital', 'Ticket Impreso', 'Factura Electrónica'] as const

type TipoComprobante = typeof TIPOS_COMPROBANTE[number]

interface ConfiguracionComprobanteDto {
  tipoComprobante: string
  habilitado: boolean
}

interface UpdateComprobantesRequest {
  tipos: ConfiguracionComprobanteDto[]
}

/** Descripciones para cada tipo de comprobante */
const TIPO_DESCRIPTIONS: Record<TipoComprobante, string> = {
  'Ticket Digital': 'Comprobante visual en pantalla con opción de compartir vía WhatsApp o correo electrónico.',
  'Ticket Impreso': 'Comprobante impreso en la impresora térmica configurada para la sucursal.',
  'Factura Electrónica': 'Factura electrónica autorizada por el SRI. Requiere Plan Intermedio o superior y firma digital configurada.',
}

/**
 * Página de configuración de tipos de comprobante habilitados.
 * Solo accesible para roles Dueño y Gerente.
 * Implementa:
 * - Req 6.3: Checkboxes para habilitar/deshabilitar tipos
 * - Req 6.4: Invariante de al menos 1 tipo habilitado
 * - Req 6.5: Solo tipos habilitados se muestran al cajero
 * - Req 6.6: Mensaje si plan insuficiente para Factura Electrónica
 * - Req 6.7: Mensaje si falta firma SRI para Factura Electrónica
 */
export function ComprobantesConfigPage() {
  const { claims } = useAuth()

  const [configuracion, setConfiguracion] = useState<ConfiguracionComprobanteDto[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)

  const role = claims?.role
  const canEdit = role === 'Dueño' || role === 'Gerente'

  // Cargar configuración actual de comprobantes
  const fetchConfiguracion = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await api.get<ConfiguracionComprobanteDto[]>('/api/tenants/comprobantes/configuracion')
      setConfiguracion(data)
    } catch (err) {
      const apiError = err as ApiError
      setError(apiError?.details && typeof apiError.details === 'object' && 'error' in (apiError.details as Record<string, unknown>)
        ? String((apiError.details as Record<string, string>).error)
        : 'Error al cargar la configuración de comprobantes.')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    fetchConfiguracion()
  }, [fetchConfiguracion])

  // Contar cuántos tipos están habilitados
  const enabledCount = configuracion.filter(c => c.habilitado).length

  /**
   * Maneja el cambio de estado de un checkbox.
   * Valida localmente el invariante de mínimo 1 habilitado
   * y muestra errores del backend para Factura Electrónica.
   */
  const handleToggle = useCallback(async (tipo: string) => {
    const current = configuracion.find(c => c.tipoComprobante === tipo)
    if (!current) return

    const newHabilitado = !current.habilitado

    // Validación local: no permitir deshabilitar el último tipo
    if (!newHabilitado && enabledCount <= 1) {
      setError('Debe mantener al menos un tipo de comprobante habilitado.')
      return
    }

    // Aplicar cambio localmente de forma optimista
    const updatedConfig = configuracion.map(c =>
      c.tipoComprobante === tipo ? { ...c, habilitado: newHabilitado } : c
    )
    setConfiguracion(updatedConfig)
    setError(null)
    setSuccess(false)

    // Guardar en backend
    setSaving(true)
    try {
      const request: UpdateComprobantesRequest = { tipos: updatedConfig }
      await api.put('/api/tenants/comprobantes/configuracion', request)
      setSuccess(true)
    } catch (err) {
      // Revertir cambio local en caso de error
      setConfiguracion(configuracion)
      const apiError = err as ApiError
      const details = apiError?.details as Record<string, unknown> | undefined

      // Manejar errores específicos de Factura Electrónica
      if (details && typeof details === 'object') {
        const code = details.code as string | undefined
        if (code === 'FACTURA_REQUIRES_PLAN') {
          setError('La facturación electrónica requiere Plan Intermedio o superior.')
        } else if (code === 'FACTURA_REQUIRES_SRI_CONFIG') {
          setError('Debe configurar los datos de facturación SRI (firma digital) previamente.')
        } else if (code === 'COMPROBANTE_MIN_ONE_REQUIRED') {
          setError('Debe mantener al menos un tipo de comprobante habilitado.')
        } else {
          setError(details.error as string ?? 'Error al actualizar la configuración.')
        }
      } else {
        setError('Error al actualizar la configuración de comprobantes.')
      }
    } finally {
      setSaving(false)
    }
  }, [configuracion, enabledCount])

  // Determinar si un checkbox debe estar deshabilitado
  // (el último habilitado no se puede deshabilitar)
  const isCheckboxDisabled = useCallback((tipo: string): boolean => {
    if (!canEdit) return true
    const current = configuracion.find(c => c.tipoComprobante === tipo)
    if (!current) return true
    // Si está habilitado y es el único habilitado, deshabilitar el checkbox
    if (current.habilitado && enabledCount <= 1) return true
    return false
  }, [canEdit, configuracion, enabledCount])

  // Estado de carga inicial
  if (loading) {
    return (
      <div className="flex items-center justify-center py-12">
        <div className="animate-spin h-8 w-8 border-4 border-action-confirm border-t-transparent rounded-full" />
      </div>
    )
  }

  // Acceso restringido para roles sin permiso
  if (!canEdit) {
    return (
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-4">
          Configuración de Comprobantes
        </h1>
        <div className="bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-700 rounded-lg p-6 text-center">
          <p className="text-gray-700 dark:text-gray-300 text-lg">
            No tiene permisos para acceder a esta configuración.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-2">
        Configuración de Comprobantes
      </h1>
      <p className="text-sm text-gray-500 dark:text-gray-400 mb-6">
        Seleccione los tipos de comprobante disponibles para su comercio. Al menos un tipo debe permanecer habilitado.
      </p>

      {/* Mensaje de error */}
      {error && (
        <div className="mb-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700 rounded-lg px-4 py-3" role="alert">
          <p className="text-sm text-red-700 dark:text-red-300">{error}</p>
        </div>
      )}

      {/* Mensaje de éxito */}
      {success && (
        <div className="mb-4 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-700 rounded-lg px-4 py-3">
          <p className="text-sm text-green-700 dark:text-green-300">
            ✓ Configuración actualizada exitosamente.
          </p>
        </div>
      )}

      {/* Indicador de guardado */}
      {saving && (
        <div className="mb-4 flex items-center gap-2">
          <div className="animate-spin h-4 w-4 border-2 border-action-confirm border-t-transparent rounded-full" />
          <span className="text-sm text-gray-600 dark:text-gray-400">Guardando...</span>
        </div>
      )}

      {/* Lista de tipos de comprobante con checkboxes */}
      <div className="bg-white dark:bg-dark-surface rounded-lg shadow divide-y divide-gray-200 dark:divide-gray-700">
        {TIPOS_COMPROBANTE.map(tipo => {
          const config = configuracion.find(c => c.tipoComprobante === tipo)
          const habilitado = config?.habilitado ?? false
          const disabled = isCheckboxDisabled(tipo) || saving

          return (
            <div
              key={tipo}
              className="flex items-center px-6 py-4 gap-4"
            >
              <input
                type="checkbox"
                id={`comprobante-${tipo.replace(/\s+/g, '-').toLowerCase()}`}
                checked={habilitado}
                disabled={disabled}
                onChange={() => handleToggle(tipo)}
                className={`
                  h-5 w-5 rounded border-gray-300 dark:border-gray-600
                  text-action-confirm focus:ring-action-confirm focus:ring-2
                  ${disabled ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'}
                `}
                aria-label={`Habilitar ${tipo}`}
              />
              <label
                htmlFor={`comprobante-${tipo.replace(/\s+/g, '-').toLowerCase()}`}
                className={`flex-1 min-w-0 ${disabled ? 'cursor-not-allowed' : 'cursor-pointer'}`}
              >
                <p className="text-sm font-medium text-gray-900 dark:text-dark-text">
                  {tipo}
                </p>
                <p className="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
                  {TIPO_DESCRIPTIONS[tipo as TipoComprobante]}
                </p>
              </label>
            </div>
          )
        })}
      </div>

      {/* Nota informativa sobre el invariante */}
      <div className="mt-4 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-700 rounded-lg px-4 py-3">
        <p className="text-sm text-blue-700 dark:text-blue-300">
          <span className="font-medium">Nota:</span> Debe mantener al menos un tipo de comprobante habilitado en todo momento.
          {enabledCount === 1 && ' El último tipo habilitado no puede ser desactivado.'}
        </p>
      </div>

      {/* Botón para recargar configuración */}
      <div className="mt-6">
        <Button
          variant="neutral"
          onClick={fetchConfiguracion}
          disabled={saving}
        >
          Recargar Configuración
        </Button>
      </div>
    </div>
  )
}
