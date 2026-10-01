import { createContext, useContext, useState, useCallback, type ReactNode } from 'react'
import { jwtDecode } from 'jwt-decode'
import type { AuthState, JwtClaims } from '../types/auth'
import { clearApiCache } from '../lib/api'

interface AuthContextValue extends AuthState {
  login: (token: string) => void
  logout: () => void
}

const AUTH_TOKEN_KEY = 'saas-admin-token'

const AuthContext = createContext<AuthContextValue | undefined>(undefined)

function decodeToken(token: string): JwtClaims | null {
  try {
    const decoded = jwtDecode<JwtClaims>(token)
    // Only allow SuperAdmin role in this panel
    if (decoded.role !== 'SuperAdmin') {
      return null
    }
    return decoded
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
      // Token expired or invalid role, clean up
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
      // Reject non-SuperAdmin tokens
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
    clearApiCache()
    setState({ token: null, claims: null, isAuthenticated: false })
  }, [])

  return (
    <AuthContext.Provider value={{ ...state, login, logout }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return context
}
