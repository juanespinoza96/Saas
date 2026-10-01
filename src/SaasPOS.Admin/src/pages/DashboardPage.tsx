import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiRequest } from '../lib/api'

interface IngresoPlan {
  planNombre: string
  totalIngresos: number
}

interface ComercioEstado {
  id: number
  ruc: string
  razonSocial: string
  planNombre: string
  estado: string
  fechaProximoCorte: string | null
}

interface DashboardData {
  ingresosPorPlan: IngresoPlan[]
  totalMensualProyectado: number
  comercios: ComercioEstado[]
}

export function DashboardPage() {
  const [data, setData] = useState<DashboardData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    async function fetchDashboard() {
      try {
        setLoading(true)
        const result = await apiRequest<DashboardData>('/api/admin/dashboard')
        setData(result)
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Error al cargar el dashboard')
      } finally {
        setLoading(false)
      }
    }
    fetchDashboard()
  }, [])

  if (loading) {
    return (
      <div className="space-y-6">
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Dashboard</h2>
        <div className="flex items-center justify-center py-16">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-action-confirm" />
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="space-y-6">
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Dashboard</h2>
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4">
          <p className="text-red-700 dark:text-red-300">{error}</p>
        </div>
      </div>
    )
  }

  if (!data) return null

  const totalIngresos = data.ingresosPorPlan.reduce((sum, p) => sum + p.totalIngresos, 0)

  // Compute state counts from the comercios list
  const comerciosPorEstado = {
    activos: data.comercios.filter(c => c.estado === 'Activo').length,
    porVencer: data.comercios.filter(c => c.estado === 'Por vencer').length,
    enMora: data.comercios.filter(c => c.estado === 'En mora').length,
    suspendidos: data.comercios.filter(c => c.estado === 'Suspendido').length,
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-900 dark:text-dark-text">Dashboard</h2>
        <Link
          to="/comercios"
          className="text-sm font-medium text-action-confirm hover:text-action-confirm/80 transition-colors"
        >
          Ver todos los comercios →
        </Link>
      </div>

      {/* Ingresos Section */}
      <section className="space-y-4">
        <h3 className="text-lg font-semibold text-gray-900 dark:text-dark-text">
          Ingresos
        </h3>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {/* Total acumulado */}
          <div className="bg-white dark:bg-dark-surface rounded-xl p-6 shadow-sm border border-gray-100 dark:border-dark-surface">
            <p className="text-sm text-gray-500 dark:text-dark-text/70">Total Ingresos Acumulados</p>
            <p className="text-3xl font-bold text-gray-900 dark:text-dark-text mt-1">
              ${totalIngresos.toLocaleString('es-EC', { minimumFractionDigits: 2 })}
            </p>
            <div className="mt-4 space-y-2">
              {data.ingresosPorPlan.map((plan) => (
                <div key={plan.planNombre} className="flex justify-between text-sm">
                  <span className="text-gray-600 dark:text-dark-text/70">{plan.planNombre}</span>
                  <span className="font-medium text-gray-900 dark:text-dark-text">
                    ${plan.totalIngresos.toLocaleString('es-EC', { minimumFractionDigits: 2 })}
                  </span>
                </div>
              ))}
            </div>
          </div>

          {/* Proyección mensual */}
          <div className="bg-white dark:bg-dark-surface rounded-xl p-6 shadow-sm border border-gray-100 dark:border-dark-surface">
            <p className="text-sm text-gray-500 dark:text-dark-text/70">Proyección Mensual</p>
            <p className="text-3xl font-bold text-gray-900 dark:text-dark-text mt-1">
              ${data.totalMensualProyectado.toLocaleString('es-EC', { minimumFractionDigits: 2 })}
            </p>
            <p className="text-xs text-gray-400 dark:text-dark-text/50 mt-2">
              Basado en comercios activos actuales
            </p>
          </div>
        </div>
      </section>

      {/* Estado de Comercios */}
      <section className="space-y-4">
        <h3 className="text-lg font-semibold text-gray-900 dark:text-dark-text">
          Estado de Comercios
        </h3>
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <StateCard
            label="Activos"
            count={comerciosPorEstado.activos}
            colorClass="border-l-action-confirm"
            textColorClass="text-action-confirm"
          />
          <StateCard
            label="Por Vencer"
            count={comerciosPorEstado.porVencer}
            colorClass="border-l-action-edit"
            textColorClass="text-action-edit"
            highlight
          />
          <StateCard
            label="En Mora"
            count={comerciosPorEstado.enMora}
            colorClass="border-l-action-danger"
            textColorClass="text-action-danger"
            highlight
          />
          <StateCard
            label="Suspendidos"
            count={comerciosPorEstado.suspendidos}
            colorClass="border-l-gray-400"
            textColorClass="text-gray-500"
          />
        </div>
      </section>
    </div>
  )
}

interface StateCardProps {
  label: string
  count: number
  colorClass: string
  textColorClass: string
  highlight?: boolean
}

function StateCard({ label, count, colorClass, textColorClass, highlight }: StateCardProps) {
  return (
    <div
      className={`bg-white dark:bg-dark-surface rounded-xl p-6 shadow-sm border border-gray-100 dark:border-dark-surface border-l-4 ${colorClass} ${
        highlight ? 'ring-1 ring-offset-1 ring-current ' + textColorClass : ''
      }`}
    >
      <p className="text-sm text-gray-500 dark:text-dark-text/70">{label}</p>
      <p className={`text-3xl font-bold mt-1 ${textColorClass}`}>{count}</p>
      {highlight && count > 0 && (
        <p className={`text-xs mt-2 font-medium ${textColorClass}`}>
          ⚠ Requiere atención
        </p>
      )}
    </div>
  )
}
