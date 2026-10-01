import { useState, useEffect, useCallback } from 'react'
import { useAuth } from '../contexts/AuthContext'
import { useConfiguracion } from '../hooks/useConfiguracion'
import { api } from '../lib/api'
import { Button } from '../components/ui/Button'
import { SucursalSelector } from '../components/ui/SucursalSelector'
import { CambioVoluntarioPasswordCard } from '../components/configuracion/CambioVoluntarioPasswordCard'
import type { UpdateConfiguracionRequest, Sucursal } from '../types/configuracion'

interface ToggleOption {
  key: keyof UpdateConfiguracionRequest
  label: string
  description: string
  /** Si es true, solo se muestra para roles con permiso de edición (Gerente/Dueño) */
  soloEditores?: boolean
}

const TOGGLE_OPTIONS: ToggleOption[] = [
  {
    key: 'esBarEscolar',
    label: 'Es Bar Escolar',
    description: 'Activa el modo bar escolar, que adapta la interfaz para ventas rápidas en entornos educativos.',
  },
  {
    key: 'mostrarBotonCliente',
    label: 'Mostrar Botón Cliente',
    description: 'Muestra el botón para seleccionar o crear un cliente en la pantalla de ventas.',
  },
  {
    key: 'permiteVentaEnNegativo',
    label: 'Permite Venta en Negativo',
    description: 'Permite realizar ventas incluso cuando el stock del producto es insuficiente o negativo.',
  },
  {
    key: 'impresionAutomaticaTicket',
    label: 'Impresión Automática de Ticket',
    description: 'Imprime automáticamente el ticket al completar una venta, sin necesidad de confirmación.',
  },
  {
    key: 'mostrarVentasAlCajero',
    label: 'Mostrar Ventas al Cajero',
    description: 'Permite que los cajeros vean los totales de ventas diarias y el historial de ventas en el dashboard.',
    soloEditores: true,
  },
]

/**
 * Configuración de Sucursal page.
 * - Dueño: selector de sucursal + toggles editables + botón guardar
 * - Gerente: SucursalSelector (visible si plan > 1 sucursal) + toggles editables + botón guardar
 * - Cajero/Bodeguero/Supervisor: toggles en modo lectura, sin botón guardar
 * Implements Requirements 4.1, 4.2, 4.3, 4.5, 4.6, 6.1, 8.1, 8.2, 8.3, 8.4, 8.5
 */
export function ConfiguracionPage() {
  const { claims } = useAuth()
  const { config, loading, saving, error, success, fetchConfig, updateConfig, clearSuccess } = useConfiguracion()

  const [sucursales, setSucursales] = useState<Sucursal[]>([])
  const [loadingSucursales, setLoadingSucursales] = useState(false)
  const [selectedSucursalId, setSelectedSucursalId] = useState<number | null>(null)
  const [localToggles, setLocalToggles] = useState<UpdateConfiguracionRequest>({
    esBarEscolar: false,
    mostrarBotonCliente: false,
    permiteVentaEnNegativo: false,
    impresionAutomaticaTicket: false,
    mostrarVentasAlCajero: false,
  })

  const role = claims?.role
  const isDueno = role === 'Dueño'
  const isGerente = role === 'Gerente'
  const canEdit = isDueno || isGerente
  const isReadOnly = !canEdit

  // Fetch config on mount
  useEffect(() => {
    fetchConfig()
  }, [fetchConfig])

  // Fetch sucursales list para Dueño y Gerente (Req 4.1, 4.4)
  useEffect(() => {
    if (!isDueno && !isGerente) return
    async function loadSucursales() {
      setLoadingSucursales(true)
      try {
        const data = await api.get<Sucursal[]>('/api/tenants/sucursales')
        setSucursales(data)
      } catch {
        // Error al cargar sucursales — el selector mostrará estado vacío
      } finally {
        setLoadingSucursales(false)
      }
    }
    loadSucursales()
  }, [isDueno, isGerente])

  // Sync local toggles when config is loaded/changed
  useEffect(() => {
    if (config) {
      setLocalToggles({
        esBarEscolar: config.esBarEscolar,
        mostrarBotonCliente: config.mostrarBotonCliente,
        permiteVentaEnNegativo: config.permiteVentaEnNegativo,
        impresionAutomaticaTicket: config.impresionAutomaticaTicket,
        mostrarVentasAlCajero: config.mostrarVentasAlCajero,
      })
      // Set selected sucursal from config if not already set
      if (!selectedSucursalId) {
        setSelectedSucursalId(config.sucursalId)
      }
    }
  }, [config, selectedSucursalId])

  // Set selectedSucursalId from JWT claim para non-Dueño roles (Req 4.5)
  useEffect(() => {
    if (!isDueno && claims?.sucursal_id) {
      setSelectedSucursalId(Number(claims.sucursal_id))
    }
  }, [isDueno, claims?.sucursal_id])

  const handleToggle = useCallback((key: keyof UpdateConfiguracionRequest) => {
    clearSuccess()
    setLocalToggles(prev => ({ ...prev, [key]: !prev[key] }))
  }, [clearSuccess])

  const handleSave = useCallback(async () => {
    if (!selectedSucursalId) return
    await updateConfig(selectedSucursalId, localToggles)
  }, [selectedSucursalId, localToggles, updateConfig])

  // Handler para el selector del Dueño (raw <select>)
  const handleDuenoSucursalChange = useCallback((e: React.ChangeEvent<HTMLSelectElement>) => {
    const newId = Number(e.target.value)
    setSelectedSucursalId(newId)
    clearSuccess()
    // Recargar configuración de la sucursal seleccionada
    fetchConfig(newId)
  }, [clearSuccess, fetchConfig])

  // Handler para el SucursalSelector del Gerente (Req 4.2)
  const handleGerenteSucursalChange = useCallback((newId: number) => {
    setSelectedSucursalId(newId)
    clearSuccess()
    // Recargar configuración de la sucursal seleccionada (máx 3s timeout)
    fetchConfig(newId)
  }, [clearSuccess, fetchConfig])

  // Determinar el nombre de sucursal para el heading
  const sucursalNombre = isDueno
    ? sucursales.find(s => s.id === selectedSucursalId)?.nombre
    : (sucursales.find(s => s.id === selectedSucursalId)?.nombre ?? `Sucursal ${selectedSucursalId ?? ''}`)

  // Estado de carga
  if (loading && !config) {
    return (
      <div className="flex items-center justify-center py-12">
        <div className="animate-spin h-8 w-8 border-4 border-action-confirm border-t-transparent rounded-full" />
      </div>
    )
  }

  // Error: Sucursal no asignada (no-Dueño sin sucursal)
  if (error === 'SUCURSAL_NOT_ASSIGNED' && !isDueno) {
    return (
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-4">
          Configuración de Sucursal
        </h1>
        <div className="bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-700 rounded-lg p-6 text-center">
          <p className="text-gray-700 dark:text-gray-300 text-lg">
            No tiene una sucursal asignada. Contacte al administrador.
          </p>
        </div>
      </div>
    )
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-2">
        Configuración de Sucursal{sucursalNombre ? `: ${sucursalNombre}` : ''}
      </h1>

      {/* Selector de sucursal para Gerente (Req 4.1, 4.3) — visible solo si >1 sucursal */}
      {isGerente && sucursales.length > 1 && (
        <div className="mb-6 max-w-xs">
          <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
            Seleccionar Sucursal
          </label>
          <SucursalSelector
            sucursales={sucursales}
            selectedId={selectedSucursalId}
            onChange={handleGerenteSucursalChange}
            loading={loadingSucursales}
            aria-label="Seleccionar sucursal"
          />
        </div>
      )}

      {/* Branch selector for Dueño (Req 8.3) */}
      {isDueno && sucursales.length > 0 && (
        <div className="mb-6">
          <label
            htmlFor="sucursal-selector"
            className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
          >
            Seleccionar Sucursal
          </label>
          <select
            id="sucursal-selector"
            value={selectedSucursalId ?? ''}
            onChange={handleDuenoSucursalChange}
            className="block w-full max-w-xs rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-surface px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:border-action-confirm focus:ring-1 focus:ring-action-confirm"
          >
            <option value="" disabled>
              -- Seleccione una sucursal --
            </option>
            {sucursales.map(s => (
              <option key={s.id} value={s.id}>
                {s.nombre}
              </option>
            ))}
          </select>
        </div>
      )}

      {/* Mensaje de error (Req 4.6) — selector permanece habilitado */}
      {error && error !== 'SUCURSAL_NOT_ASSIGNED' && (
        <div className="mb-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700 rounded-lg px-4 py-3">
          <p className="text-sm text-red-700 dark:text-red-300">{error}</p>
        </div>
      )}

      {/* Indicador de carga al cambiar de sucursal */}
      {loading && config && (
        <div className="mb-4 flex items-center gap-2">
          <div className="animate-spin h-4 w-4 border-2 border-action-confirm border-t-transparent rounded-full" />
          <span className="text-sm text-gray-600 dark:text-gray-400">Cargando configuración...</span>
        </div>
      )}

      {/* Toggles (Req 8.1) */}
      <div className="bg-white dark:bg-dark-surface rounded-lg shadow divide-y divide-gray-200 dark:divide-gray-700">
        {TOGGLE_OPTIONS
          .filter(option => !option.soloEditores || canEdit)
          .map(option => (
          <div
            key={option.key}
            className="flex items-center justify-between px-6 py-4"
          >
            <div className="flex-1 min-w-0 pr-4">
              <p className="text-sm font-medium text-gray-900 dark:text-dark-text">
                {option.label}
              </p>
              <p className="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
                {option.description}
              </p>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={localToggles[option.key]}
              aria-label={option.label}
              disabled={isReadOnly}
              onClick={() => handleToggle(option.key)}
              className={`
                relative inline-flex h-6 w-11 shrink-0 rounded-full
                border-2 border-transparent transition-colors duration-200 ease-in-out
                focus:outline-none focus:ring-2 focus:ring-action-confirm focus:ring-offset-2
                ${isReadOnly ? 'cursor-not-allowed opacity-60' : 'cursor-pointer'}
                ${localToggles[option.key] ? 'bg-action-confirm' : 'bg-gray-300 dark:bg-gray-600'}
              `}
            >
              <span
                className={`
                  pointer-events-none inline-block h-5 w-5 rounded-full
                  bg-white shadow transform ring-0 transition duration-200 ease-in-out
                  ${localToggles[option.key] ? 'translate-x-5' : 'translate-x-0'}
                `}
              />
            </button>
          </div>
        ))}
      </div>

      {/* Save button — solo para Gerente/Dueño (Req 4.2, 8.5) */}
      {canEdit && (
        <div className="mt-6 flex items-center gap-4">
          <Button
            variant="confirm"
            loading={saving}
            disabled={!selectedSucursalId}
            onClick={handleSave}
          >
            Guardar Configuración
          </Button>
          {success && (
            <span className="text-sm text-action-confirm font-medium">
              ✓ Configuración guardada exitosamente
            </span>
          )}
        </div>
      )}

      {/* Aviso de solo lectura para Cajero/Bodeguero/Supervisor */}
      {isReadOnly && (
        <div className="mt-6 bg-gray-50 dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg px-4 py-3">
          <p className="text-sm text-gray-600 dark:text-gray-400">
            Solo lectura — no tiene permisos para modificar la configuración.
          </p>
        </div>
      )}

      {/* Cambio voluntario de contraseña (Req 17.1) — disponible para todo
          usuario autenticado, sin importar su rol ni sus permisos de edición
          de la configuración de la sucursal. */}
      <CambioVoluntarioPasswordCard />
    </div>
  )
}
