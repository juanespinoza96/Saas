import { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import { api } from '../../lib/api'

type PlanNivel = 'Básico' | 'Intermedio' | 'Empresarial'

interface ConfiguracionPlan {
  planNivel: PlanNivel
}

/**
 * Contenido del header: nombre del comercio y botón de Chat IA.
 * El botón de Chat IA solo se muestra para Plan Empresarial (Req 7.1).
 * Se renderiza dentro del <header> del AppLayout.
 */
export function Header() {
  const { comercioNombre, claims } = useAuth()
  const [planNivel, setPlanNivel] = useState<PlanNivel | null>(null)

  // Cargar plan del comercio para decidir si mostrar botón de Chat IA
  useEffect(() => {
    async function fetchPlan() {
      try {
        const config = await api.get<ConfiguracionPlan>('/api/tenants/configuracion')
        setPlanNivel(config.planNivel)
      } catch {
        setPlanNivel(null)
      }
    }
    fetchPlan()
  }, [])

  return (
    <div className="flex items-center justify-between w-full h-full">
      <div className="flex items-center gap-3">
        <h2 className="text-base md:text-lg font-semibold text-gray-900 dark:text-dark-text truncate">
          {comercioNombre}
        </h2>
      </div>
      <div className="flex items-center gap-2 md:gap-4">
        {/* Botón de Chat IA solo para Plan Empresarial (Req 7.1) */}
        {planNivel === 'Empresarial' && (
          <Link
            to="/chat-ia"
            className="flex items-center gap-2 px-2 md:px-3 py-2 text-sm font-medium text-purple-700 dark:text-purple-300 bg-purple-50 dark:bg-purple-900/20 hover:bg-purple-100 dark:hover:bg-purple-900/40 rounded-lg transition-colors"
            aria-label="Abrir Chat IA"
          >
            {/* Ícono sparkle/IA */}
            <svg
              className="h-5 w-5"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              xmlns="http://www.w3.org/2000/svg"
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M9.813 15.904L9 18.75l-.813-2.846a4.5 4.5 0 00-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 003.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 003.09 3.09L15.75 12l-2.846.813a4.5 4.5 0 00-3.09 3.09zM18.259 8.715L18 9.75l-.259-1.035a3.375 3.375 0 00-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 002.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 002.455 2.456L21.75 6l-1.036.259a3.375 3.375 0 00-2.455 2.456z"
              />
            </svg>
            <span className="hidden sm:inline">Chat IA</span>
          </Link>
        )}
        {claims && (
          <div className="flex items-center gap-2 text-sm text-gray-600 dark:text-gray-400">
            <span aria-label="Rol del usuario">{claims.role}</span>
          </div>
        )}
      </div>
    </div>
  )
}
