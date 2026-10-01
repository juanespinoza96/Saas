import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import type { UserRole } from '../types/auth'

interface ProtectedRouteProps {
  /** If specified, only these roles can access the route */
  allowedRoles?: UserRole[]
}

/**
 * Route guard that redirects unauthenticated users to login
 * and unauthorized roles to dashboard.
 */
export function ProtectedRoute({ allowedRoles }: ProtectedRouteProps) {
  const { isAuthenticated, claims } = useAuth()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />
  }

  if (allowedRoles && allowedRoles.length > 0 && claims) {
    if (!allowedRoles.includes(claims.role)) {
      return <Navigate to="/dashboard" replace />
    }
  }

  return <Outlet />
}
