import { useState, useEffect } from 'react'
import { api } from '../lib/api'

/** DTO que retorna el endpoint /api/tenants/roles-disponibles */
export interface RolDisponible {
  id: string
  nombre: string
}

interface UseRolesDisponiblesReturn {
  roles: RolDisponible[]
  isLoading: boolean
  error: string | null
}

/**
 * Hook que consulta los roles disponibles para el plan del comercio actual.
 * Llama a GET /api/tenants/roles-disponibles con el token JWT inyectado automáticamente.
 * Maneja estados de carga, error y datos.
 */
export function useRolesDisponibles(): UseRolesDisponiblesReturn {
  const [roles, setRoles] = useState<RolDisponible[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    async function fetchRoles() {
      setIsLoading(true)
      setError(null)

      try {
        const data = await api.get<RolDisponible[]>('/api/tenants/roles-disponibles')

        if (!cancelled) {
          setRoles(data)
        }
      } catch (err: unknown) {
        if (!cancelled) {
          if (err && typeof err === 'object' && 'message' in err) {
            setError((err as { message: string }).message)
          } else {
            setError('No se pudieron cargar los roles disponibles')
          }
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false)
        }
      }
    }

    fetchRoles()

    return () => {
      cancelled = true
    }
  }, [])

  return { roles, isLoading, error }
}
