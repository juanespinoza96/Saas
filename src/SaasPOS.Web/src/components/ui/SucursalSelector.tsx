import type { Sucursal } from '../../types/configuracion'

export interface SucursalSelectorProps {
  /** Lista de sucursales disponibles del comercio */
  sucursales: Sucursal[]
  /** ID de la sucursal seleccionada actualmente */
  selectedId: number | null
  /** Callback cuando el usuario cambia la selección */
  onChange: (id: number) => void
  /** Indica si las sucursales están cargando */
  loading?: boolean
  /** Deshabilita el selector */
  disabled?: boolean
  /** Etiqueta accesible personalizada */
  'aria-label'?: string
}

/**
 * Selector de sucursales reutilizable.
 * Muestra un dropdown con las sucursales del comercio.
 * La lógica de visibilidad (mostrar solo si el usuario es Gerente
 * y el plan permite >1 sucursal) la maneja el componente padre.
 *
 * Requisitos: 4.1, 4.3, 4.4, 4.5
 */
export function SucursalSelector({
  sucursales,
  selectedId,
  onChange,
  loading = false,
  disabled = false,
  'aria-label': ariaLabel = 'Seleccionar sucursal',
}: SucursalSelectorProps) {
  // Estilos base siguiendo el patrón de RolSelect
  const selectBaseStyles =
    'w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-surface px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent'

  // Estado de carga: spinner y select deshabilitado
  if (loading) {
    return (
      <div className="relative">
        <select
          disabled
          className={`${selectBaseStyles} bg-gray-100 dark:bg-dark-surface text-gray-500 dark:text-gray-400 cursor-not-allowed`}
          aria-busy="true"
          aria-label="Cargando sucursales"
        >
          <option>Cargando sucursales...</option>
        </select>
        <div className="absolute right-3 top-1/2 -translate-y-1/2">
          <div
            className="animate-spin h-4 w-4 border-2 border-action-confirm border-t-transparent rounded-full"
            role="status"
            aria-label="Cargando"
          />
        </div>
      </div>
    )
  }

  // Estado vacío: no hay sucursales disponibles
  if (sucursales.length === 0) {
    return (
      <div>
        <select
          disabled
          className={`${selectBaseStyles} border-action-danger bg-red-50 dark:bg-red-900/10 text-gray-500 dark:text-gray-400 cursor-not-allowed`}
          aria-invalid="true"
          aria-describedby="sucursal-selector-error"
        >
          <option>— Sin sucursales disponibles —</option>
        </select>
        <p id="sucursal-selector-error" className="text-xs text-action-danger mt-1">
          No se encontraron sucursales para este comercio
        </p>
      </div>
    )
  }

  // Selector normal con sucursales cargadas
  return (
    <div>
      <select
        value={selectedId ?? ''}
        onChange={(e) => onChange(Number(e.target.value))}
        disabled={disabled}
        className={`${selectBaseStyles} ${
          disabled ? 'cursor-not-allowed opacity-60' : ''
        }`}
        aria-label={ariaLabel}
      >
        <option value="" disabled>
          -- Seleccione una sucursal --
        </option>
        {sucursales.map((s) => (
          <option key={s.id} value={s.id}>
            {s.nombre}
          </option>
        ))}
      </select>
    </div>
  )
}
