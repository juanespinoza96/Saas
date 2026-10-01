import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

/** Ruta canónica del cambio de contraseña obligatorio. */
const CAMBIAR_PASSWORD_PATH = '/cambiar-password'

/**
 * Guard de navegación que fuerza el cambio de contraseña obligatorio.
 *
 * Cuando el Claim_Cambio (`must_change_password`) está activo, el usuario inició
 * sesión con una Contraseña_Temporal y debe establecer una contraseña definitiva
 * antes de usar el resto del sistema:
 * - Si `mustChangePassword` es verdadero y la ruta actual NO es
 *   `/cambiar-password`, redirige a esa pantalla y bloquea el acceso al resto de
 *   rutas protegidas (Req 9.1, 9.2).
 * - Si `mustChangePassword` es verdadero y ya está en `/cambiar-password`, deja
 *   pasar para que la pantalla de cambio se renderice (evita un bucle de
 *   redirección).
 * - Si `mustChangePassword` es falso, no interfiere con la navegación normal.
 *   Al completar el cambio, el AuthContext apaga el flag (Req 9.3) y este guard
 *   deja de forzar la redirección.
 *
 * Se coloca dentro de `ProtectedRoute`, por lo que solo aplica a usuarios ya
 * autenticados.
 */
export function RequirePasswordChangeGuard() {
  const { mustChangePassword } = useAuth()
  const location = useLocation()

  // El claim está activo: forzar la pantalla de cambio salvo que ya estemos en ella.
  if (mustChangePassword && location.pathname !== CAMBIAR_PASSWORD_PATH) {
    return <Navigate to={CAMBIAR_PASSWORD_PATH} replace />
  }

  return <Outlet />
}
