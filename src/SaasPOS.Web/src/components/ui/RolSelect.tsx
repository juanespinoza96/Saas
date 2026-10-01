import { useEffect } from 'react'
import { useRolesDisponibles } from '../../hooks/useRolesDisponibles'

export interface RolSelectProps {
  /** ID del rol actualmente seleccionado */
  value: string
  /** Callback cuando cambia la selección */
  onChange: (rolId: string) => void
  /** Deshabilitar el selector externamente */
  disabled?: boolean
  /**
   * Rol actual del usuario en modo edición. Si no está disponible en el plan,
   * se muestra como opción deshabilitada con advertencia (Req 7.4).
   * Acepta el nombre del prop como `currentRol` o `currentRolNoDisponible`.
   */
  currentRol?: string
  currentRolNoDisponible?: string | null
  /** Clases CSS adicionales para el contenedor */
  className?: string
  /** Callback para informar al padre si los roles cargaron correctamente */
  onLoadStateChange?: (loaded: boolean) => void
}

/**
 * Selector de roles que consulta los roles disponibles del plan actual.
 * Usa internamente el hook `useRolesDisponibles` para obtener opciones.
 * Muestra indicador de carga mientras se obtienen roles (Req 7.1).
 * Deshabilita selector si hay error o lista vacía (Req 7.2).
 * Soporta modo edición con roles no disponibles (Req 7.4).
 */
export function RolSelect({
  value,
  onChange,
  disabled = false,
  currentRol,
  currentRolNoDisponible,
  className = '',
  onLoadStateChange,
}: RolSelectProps) {
  const { roles, isLoading, error } = useRolesDisponibles()

  // Determinar el rol no disponible: soportar ambas props por compatibilidad
  const rolNoDisponible = currentRolNoDisponible ?? currentRol ?? null

  // Determinar si los roles cargaron correctamente
  const rolesLoaded = !isLoading && !error && roles.length > 0

  // Notificar al padre si los roles están disponibles
  useEffect(() => {
    onLoadStateChange?.(rolesLoaded)
  }, [rolesLoaded, onLoadStateChange])

  // Verificar si el rol actual (modo edición) no está en la lista disponible
  const rolActualNoDisponible =
    rolNoDisponible && !roles.some(r => r.id === rolNoDisponible) && !isLoading

  // Estilos base del select
  const selectBaseStyles =
    'w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent'

  // Estado de carga: mostrar spinner e indicador (Req 7.1)
  if (isLoading) {
    return (
      <div className={`relative ${className}`}>
        <select
          disabled
          className={`${selectBaseStyles} bg-gray-100 dark:bg-dark-bg text-gray-500 dark:text-gray-400 cursor-not-allowed`}
          aria-busy="true"
          aria-label="Cargando roles disponibles"
        >
          <option>Cargando roles...</option>
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

  // Estado de error o lista vacía: deshabilitar selector y mostrar mensaje (Req 7.2)
  if (error || roles.length === 0) {
    return (
      <div className={className}>
        <select
          disabled
          className={`${selectBaseStyles} border-action-danger bg-red-50 dark:bg-red-900/10 text-gray-500 dark:text-gray-400 cursor-not-allowed`}
          aria-invalid="true"
          aria-describedby="rol-select-error"
        >
          <option>— Sin roles disponibles —</option>
        </select>
        <p id="rol-select-error" className="text-xs text-action-danger mt-1">
          {error || 'No se pudieron cargar los roles disponibles'}
        </p>
      </div>
    )
  }

  // Renderizar selector con roles disponibles
  return (
    <div className={className}>
      <select
        value={value}
        onChange={e => onChange(e.target.value)}
        disabled={disabled}
        className={`${selectBaseStyles} ${
          disabled ? 'cursor-not-allowed opacity-60' : ''
        }`}
        aria-label="Seleccionar rol"
      >
        {/* Rol actual no disponible como opción deshabilitada (Req 7.4) */}
        {rolActualNoDisponible && (
          <option value={rolNoDisponible!} disabled>
            {rolNoDisponible} (No disponible en plan actual)
          </option>
        )}
        {roles.map(r => (
          <option key={r.id} value={r.id}>
            {r.nombre}
          </option>
        ))}
      </select>

      {/* Mensaje de advertencia si el rol actual no está disponible (Req 7.4) */}
      {rolActualNoDisponible && (
        <p className="text-xs text-amber-600 dark:text-amber-400 mt-1">
          ⚠️ El rol actual ({rolNoDisponible}) no está disponible en el plan vigente.
          Seleccione un rol válido.
        </p>
      )}
    </div>
  )
}
