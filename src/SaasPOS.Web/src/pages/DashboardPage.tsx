import { useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { useApiRequest } from '../hooks/useApiRequest'
import { useConfiguracion } from '../hooks/useConfiguracion'
import { api } from '../lib/api'

interface VentasDiaSummary {
  totalVentas: number
  cantidadTransacciones: number
  ticketPromedio: number
}

interface VentaListItem {
  id: number
  total: number
  fechaVenta: string
}

interface NotificacionesResponse {
  total: number
}

/**
 * Verifica si un error es un 403 con código VENTAS_VISIBILITY_RESTRICTED.
 * Este error es esperado cuando un Cajero sin permiso intenta acceder a ventas.
 */
function isVentasVisibilityRestricted(err: unknown): boolean {
  if (err && typeof err === 'object' && 'status' in err) {
    const apiErr = err as { status: number; details?: { error?: string } }
    if (apiErr.status === 403) {
      return apiErr.details?.error === 'VENTAS_VISIBILITY_RESTRICTED'
    }
  }
  return false
}

/**
 * Página principal del Dashboard con resumen de ventas diarias,
 * contador de notificaciones y botones de acceso rápido según rol.
 * - Badge de notificaciones para Gerente/Dueño (Req 14.2)
 * - Resumen de ventas del día (Req 4.3, 4.4, 4.5)
 * - Accesos rápidos basados en rol
 */
export function DashboardPage() {
  const { hasRole, comercioNombre } = useAuth()
  const navigate = useNavigate()
  const { config, fetchConfig } = useConfiguracion()

  // Determinar si el usuario es Cajero y si las ventas deben ocultarse (Req 4.3, 4.4, 4.5)
  // Usar ?? false para asumir "sin permiso" hasta que config se cargue, evitando race condition
  const esCajero = hasRole('Cajero')
  const mostrarVentas = !esCajero || (config?.mostrarVentasAlCajero ?? false)

  const {
    data: ventasData,
    loading: ventasLoading,
    execute: fetchVentas,
  } = useApiRequest<VentasDiaSummary>(
    useCallback(() => {
      const hoy = new Date()
      const desde = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate()).toISOString()
      return api.get<VentaListItem[]>(`/api/tenants/ventas?desde=${desde}`).then((ventas) => {
        const items = Array.isArray(ventas) ? ventas : []
        const total = items.reduce((sum, v) => sum + v.total, 0)
        const count = items.length
        return {
          totalVentas: total,
          cantidadTransacciones: count,
          ticketPromedio: count > 0 ? total / count : 0,
        }
      }).catch((err: unknown) => {
        // Manejo defensivo: si recibimos 403 VENTAS_VISIBILITY_RESTRICTED, tratar como "sin permiso"
        // y retornar datos vacíos silenciosamente en lugar de propagar el error
        if (isVentasVisibilityRestricted(err)) {
          return { totalVentas: 0, cantidadTransacciones: 0, ticketPromedio: 0 }
        }
        throw err
      })
    }, []),
  )

  const {
    data: notificacionesData,
    loading: notifLoading,
    execute: fetchNotificaciones,
  } = useApiRequest<NotificacionesResponse>(
    useCallback(() => api.get<NotificacionesResponse>('/api/tenants/notificaciones?leida=false'), []),
  )

  // Cargar configuración de sucursal al montar el componente
  useEffect(() => {
    fetchConfig()
  }, [fetchConfig])

  // Solo obtener datos de ventas si config ya cargó y el usuario tiene permiso (Req 4.3, 4.4)
  // Guardia: no disparar fetchVentas hasta que config !== null para evitar race condition
  useEffect(() => {
    if (config !== null && mostrarVentas) {
      fetchVentas()
    }
    fetchNotificaciones()
  }, [config, mostrarVentas, fetchVentas, fetchNotificaciones])

  const notifCount = notificacionesData?.total ?? 0
  const showNotifBadge = hasRole(['Gerente', 'Dueño']) && notifCount > 0

  // Accesos rápidos según rol del usuario
  const quickActions = getQuickActions(hasRole)

  return (
    <div className="space-y-6">
      {/* Encabezado */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
            Dashboard
          </h1>
          <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
            {comercioNombre} — {new Date().toLocaleDateString('es-EC', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' })}
          </p>
        </div>

        {/* Badge de notificaciones (Req 14.2) */}
        {hasRole(['Gerente', 'Dueño']) && (
          <button
            onClick={() => navigate('/notificaciones')}
            className="relative p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-dark-bg transition-colors"
            aria-label={`Notificaciones${showNotifBadge ? ` (${notifCount} pendientes)` : ''}`}
          >
            <svg
              className="w-6 h-6 text-gray-600 dark:text-gray-300"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
              />
            </svg>
            {showNotifBadge && (
              <span className="absolute -top-1 -right-1 inline-flex items-center justify-center w-5 h-5 text-xs font-bold text-white bg-action-danger rounded-full">
                {notifCount > 99 ? '99+' : notifCount}
              </span>
            )}
          </button>
        )}
      </div>

      {/* Cards de resumen de ventas — ocultar si Cajero y mostrarVentasAlCajero es FALSE (Req 4.3, 4.5) */}
      {mostrarVentas && (
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <SummaryCard
            title="Ventas del Día"
            value={ventasLoading ? '...' : `$${(ventasData?.totalVentas ?? 0).toFixed(2)}`}
            subtitle="Total facturado"
            icon="dollar"
          />
          <SummaryCard
            title="Transacciones"
            value={ventasLoading ? '...' : String(ventasData?.cantidadTransacciones ?? 0)}
            subtitle="Ventas realizadas"
            icon="receipt"
          />
          <SummaryCard
            title="Ticket Promedio"
            value={ventasLoading ? '...' : `$${(ventasData?.ticketPromedio ?? 0).toFixed(2)}`}
            subtitle="Por transacción"
            icon="chart"
          />
        </div>
      )}

      {/* Sección de notificaciones para Gerente/Dueño */}
      {hasRole(['Gerente', 'Dueño']) && (
        <div className="bg-white dark:bg-dark-surface rounded-xl shadow-sm border border-gray-200 dark:border-gray-700 p-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text">
              Notificaciones Pendientes
            </h2>
            <span
              className={`inline-flex items-center justify-center px-3 py-1 rounded-full text-sm font-medium ${
                notifCount > 0
                  ? 'bg-action-danger/10 text-action-danger'
                  : 'bg-gray-100 dark:bg-dark-bg text-gray-500 dark:text-gray-400'
              }`}
            >
              {notifLoading ? '...' : notifCount}
            </span>
          </div>
          {notifCount > 0 && (
            <button
              onClick={() => navigate('/notificaciones')}
              className="mt-2 text-sm text-action-confirm hover:underline"
            >
              Ver notificaciones →
            </button>
          )}
        </div>
      )}

      {/* Botones de acceso rápido según rol */}
      <div>
        <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text mb-3">
          Accesos Rápidos
        </h2>
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3">
          {quickActions.map(action => (
            <button
              key={action.path}
              onClick={() => navigate(action.path)}
              className="flex flex-col items-center gap-2 p-4 rounded-xl bg-white dark:bg-dark-surface border border-gray-200 dark:border-gray-700 hover:border-action-confirm dark:hover:border-action-confirm transition-colors shadow-sm"
            >
              <span className="text-2xl" aria-hidden="true">
                {action.icon}
              </span>
              <span className="text-sm font-medium text-gray-700 dark:text-gray-300 text-center">
                {action.label}
              </span>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}

/* -- Componente de Card de Resumen -- */

interface SummaryCardProps {
  title: string
  value: string
  subtitle: string
  icon: 'dollar' | 'receipt' | 'chart'
}

function SummaryCard({ title, value, subtitle, icon }: SummaryCardProps) {
  const iconMap = {
    dollar: '💰',
    receipt: '🧾',
    chart: '📊',
  }

  return (
    <div className="bg-white dark:bg-dark-surface rounded-xl shadow-sm border border-gray-200 dark:border-gray-700 p-5">
      <div className="flex items-center gap-3">
        <span className="text-2xl" aria-hidden="true">
          {iconMap[icon]}
        </span>
        <div>
          <p className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide">
            {title}
          </p>
          <p className="text-xl font-bold text-gray-900 dark:text-dark-text">
            {value}
          </p>
          <p className="text-xs text-gray-400 dark:text-gray-500 mt-0.5">
            {subtitle}
          </p>
        </div>
      </div>
    </div>
  )
}

/* -- Accesos Rápidos por Rol -- */

interface QuickAction {
  label: string
  path: string
  icon: string
}

type RoleChecker = (role: import('../types/auth').UserRole | import('../types/auth').UserRole[]) => boolean

function getQuickActions(hasRole: RoleChecker): QuickAction[] {
  const actions: QuickAction[] = []

  // Cajero, Gerente, Dueño pueden acceder al POS
  if (hasRole(['Cajero', 'Gerente', 'Dueño'])) {
    actions.push({ label: 'Punto de Venta', path: '/pos', icon: '🛒' })
  }

  // Gerente, Dueño pueden gestionar productos
  if (hasRole(['Gerente', 'Dueño'])) {
    actions.push({ label: 'Productos', path: '/productos', icon: '📦' })
  }

  // Cajero, Gerente, Dueño pueden gestionar clientes
  if (hasRole(['Cajero', 'Gerente', 'Dueño'])) {
    actions.push({ label: 'Clientes', path: '/clientes', icon: '👥' })
  }

  // Gerente, Dueño, Supervisor pueden ver reportes
  if (hasRole(['Gerente', 'Dueño', 'Supervisor'])) {
    actions.push({ label: 'Reportes', path: '/reportes', icon: '📈' })
  }

  // Bodeguero, Gerente, Dueño pueden gestionar inventario
  if (hasRole(['Bodeguero', 'Gerente', 'Dueño'])) {
    actions.push({ label: 'Inventario', path: '/inventario', icon: '📋' })
  }

  // Gerente, Dueño pueden gestionar usuarios
  if (hasRole(['Gerente', 'Dueño'])) {
    actions.push({ label: 'Usuarios', path: '/usuarios', icon: '👤' })
  }

  return actions
}
