import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react'
import { jwtDecode } from 'jwt-decode'
import type { AuthState, JwtClaims, UserRole } from '../types/auth'
import { useInactivityTimeout } from '../hooks/useInactivityTimeout'
import { api } from '../lib/api'

interface AuthContextValue extends AuthState {
  login: (token: string) => void
  logout: () => void
  hasRole: (role: UserRole | UserRole[]) => boolean
  comercioNombre: string
  mustChangePassword: boolean
  // Libera la navegación tras un cambio de contraseña obligatorio exitoso (Req 9.3).
  // Apaga el claim `must_change_password` en el estado en memoria para que el
  // Guard_Router deje de forzar la ruta /cambiar-password sin necesidad de un
  // nuevo token (el backend responde 200 sin re-emitir el JWT).
  clearMustChangePassword: () => void
}

const AUTH_TOKEN_KEY = 'saas-pos-token'

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

function decodeToken(token: string): JwtClaims | null {
  try {
    return jwtDecode<JwtClaims>(token)
  } catch {
    return null
  }
}

function getInitialState(): AuthState {
  try {
    const token = localStorage.getItem(AUTH_TOKEN_KEY)
    if (token) {
      const claims = decodeToken(token)
      if (claims && claims.exp * 1000 > Date.now()) {
        return { token, claims, isAuthenticated: true }
      }
      // Token expired, clean up
      localStorage.removeItem(AUTH_TOKEN_KEY)
    }
  } catch {
    // localStorage not available
  }
  return { token: null, claims: null, isAuthenticated: false }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(getInitialState)

  const login = useCallback((token: string) => {
    const claims = decodeToken(token)
    if (!claims) {
      return
    }
    try {
      localStorage.setItem(AUTH_TOKEN_KEY, token)
    } catch {
      // localStorage not available
    }
    setState({ token, claims, isAuthenticated: true })
  }, [])

  const logout = useCallback(() => {
    try {
      localStorage.removeItem(AUTH_TOKEN_KEY)
    } catch {
      // localStorage not available
    }
    // Limpiar cache de API para evitar datos stale entre sesiones
    api.clearCache()
    setState({ token: null, claims: null, isAuthenticated: false })
  }, [])

  // Apaga el flag de cambio obligatorio en el estado en memoria (Req 9.3).
  // No re-emite el token: solo actualiza el claim decodificado de la sesión
  // actual para que `mustChangePassword` pase a falso y el Guard libere la
  // navegación. En el próximo login el backend ya no incluirá el claim porque
  // la Bandera_Cambio del usuario quedó desactivada al guardar la contraseña.
  const clearMustChangePassword = useCallback(() => {
    setState(prev => {
      if (!prev.claims) return prev
      return {
        ...prev,
        claims: { ...prev.claims, must_change_password: false },
      }
    })
  }, [])

  const hasRole = useCallback(
    (role: UserRole | UserRole[]) => {
      if (!state.claims) return false
      const roles = Array.isArray(role) ? role : [role]
      return roles.includes(state.claims.role)
    },
    [state.claims],
  )

  const comercioNombre = state.claims?.comercio_nombre ?? 'Mi Comercio'

  // Indica si el usuario debe cambiar su contraseña, según el claim del JWT (Req 8.1, 9.1)
  const mustChangePassword = state.claims?.must_change_password === true

  // Inactivity timeout: auto-logout after 30 minutes (Req 3.3)
  useInactivityTimeout(logout, 30 * 60 * 1000, state.isAuthenticated)

  // Registrar interceptor de 401 para cerrar sesión automáticamente
  // cuando el servidor invalida el token (usuario desactivado, JTI bloqueado, etc.)
  useEffect(() => {
    api.setOnUnauthorized(logout)
  }, [logout])

  return (
    <AuthContext.Provider
      value={{
        ...state,
        login,
        logout,
        hasRole,
        comercioNombre,
        mustChangePassword,
        clearMustChangePassword,
      }}
    >
      {children}
    </AuthContext.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
