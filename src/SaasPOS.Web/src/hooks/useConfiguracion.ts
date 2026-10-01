import { useState, useCallback } from 'react'
import { api } from '../lib/api'
import type { ConfiguracionSucursalResponse, UpdateConfiguracionRequest } from '../types/configuracion'

/** Tiempo máximo para cargar configuración (Req 4.2) */
const CONFIG_FETCH_TIMEOUT_MS = 3000

interface UseConfiguracionReturn {
  config: ConfiguracionSucursalResponse | null
  loading: boolean
  saving: boolean
  error: string | null
  success: boolean
  fetchConfig: (sucursalId?: number) => Promise<void>
  updateConfig: (sucursalId: number, data: UpdateConfiguracionRequest) => Promise<boolean>
  clearSuccess: () => void
}

export function useConfiguracion(): UseConfiguracionReturn {
  const [config, setConfig] = useState<ConfiguracionSucursalResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [success, setSuccess] = useState(false)

  const fetchConfig = useCallback(async (sucursalId?: number) => {
    setLoading(true)
    setError(null)

    // Timeout de 3 segundos con AbortController (Req 4.2)
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), CONFIG_FETCH_TIMEOUT_MS)

    try {
      // Construir URL con query param opcional
      const endpoint = sucursalId
        ? `/api/tenants/configuracion?sucursalId=${sucursalId}`
        : '/api/tenants/configuracion'

      const data = await api.get<ConfiguracionSucursalResponse>(endpoint, {
        signal: controller.signal,
        skipCache: true,
      })
      setConfig(data)
    } catch (err: unknown) {
      // Verificar si fue un abort por timeout
      if (err instanceof DOMException && err.name === 'AbortError') {
        setError('La carga de configuración excedió el tiempo máximo de 3 segundos')
      } else if (err && typeof err === 'object' && 'status' in err && (err as { status: number }).status === 400) {
        const details = (err as { details?: { error?: string } }).details
        if (details?.error === 'SUCURSAL_NOT_ASSIGNED') {
          setError('SUCURSAL_NOT_ASSIGNED')
        } else {
          setError('SUCURSAL_NOT_ASSIGNED')
        }
      } else {
        setError('Error al cargar la configuración')
      }
    } finally {
      clearTimeout(timeoutId)
      setLoading(false)
    }
  }, [])

  const updateConfig = useCallback(async (sucursalId: number, data: UpdateConfiguracionRequest): Promise<boolean> => {
    setSaving(true)
    setError(null)
    setSuccess(false)
    try {
      const result = await api.put<ConfiguracionSucursalResponse>(
        `/api/tenants/configuracion/${sucursalId}`,
        data
      )
      setConfig(result)
      setSuccess(true)
      return true
    } catch (err: unknown) {
      if (err && typeof err === 'object' && 'details' in err) {
        const details = (err as { details?: { error?: string } }).details
        setError(details?.error ?? 'Error al guardar la configuración')
      } else {
        setError('Error al guardar la configuración')
      }
      return false
    } finally {
      setSaving(false)
    }
  }, [])

  const clearSuccess = useCallback(() => setSuccess(false), [])

  return { config, loading, saving, error, success, fetchConfig, updateConfig, clearSuccess }
}
