import { createContext, useContext, useState, useCallback, useEffect, type ReactNode } from 'react'
import { jwtDecode } from 'jwt-decode'
import type { AuthState, JwtClaims } from '../types/auth'
import { clearApiCache, setOnUnauthorized } from '../lib/api'
import { useInactivityTimeout } from '../hooks/useInactivityTimeout'
import { useSessionClock } from '../hooks/useSessionClock'
import { AvisoTemprano } from '../components/sesion/AvisoTemprano'
import { ModalConfirmacion } from '../components/sesion/ModalConfirmacion'
import { BloqueoTotal } from '../components/sesion/BloqueoTotal'

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

  // Reloj único de sesión: ÚNICA fuente de verdad del lado cliente (Req 7.1, 7.2).
  // Gobierna la renovación silenciosa del access token y el disparo de las tres
  // etapas de aviso de expiración (`stage`/`remainingMs`), que se renderizan más
  // abajo de forma secuencial y no solapada (AvisoTemprano/ModalConfirmacion/
  // BloqueoTotal). El Admin solo admite SuperAdmin → Nivel_Estricto (15 min / 4 h).
  const sessionClock = useSessionClock({
    token: state.token,
    claims: state.claims,
    enabled: state.isAuthenticated,
    logout,
    // Al renovar silenciosamente, re-decodificar claims y reprogramar vía `login`.
    onTokenRenewed: login,
  })

  // Señal de actividad: cada evento de actividad REINICIA el reloj único
  // (reinicia `ultimaActividad`/`deadline`), en lugar de cerrar sesión directamente.
  // No hay temporizador paralelo que compita con el reloj único (Req 2.5, 7.2, 7.3).
  useInactivityTimeout(sessionClock.resetClock, state.isAuthenticated)

  // Registrar interceptor de 401 para cerrar sesión automáticamente cuando el
  // servidor invalida el token (usuario desactivado, JTI bloqueado, tope absoluto,
  // etc.). Paridad con el POS (Req 8.2).
  useEffect(() => {
    setOnUnauthorized(logout)
  }, [logout])

  return (
    <AuthContext.Provider value={{ ...state, login, logout }}>
      {children}

      {/* Avisos de expiración de sesión: se muestran de forma secuencial y no
          solapada según la etapa derivada del reloj único (Req 5.x, 6.x). Solo
          con sesión activa. */}
      {state.isAuthenticated && sessionClock.stage === 'early' && (
        <AvisoTemprano remainingMs={sessionClock.remainingMs} />
      )}
      {state.isAuthenticated && sessionClock.stage === 'modal' && (
        <ModalConfirmacion
          remainingMs={sessionClock.remainingMs}
          onSeguirConectado={sessionClock.renewNow}
        />
      )}
      {state.isAuthenticated && sessionClock.stage === 'block' && (
        <BloqueoTotal remainingMs={sessionClock.remainingMs} onExpirado={logout} />
      )}
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
