// SuperAdmin panel only allows the SuperAdmin role
export type AdminRole = 'SuperAdmin'

export interface JwtClaims {
  sub: string
  role: AdminRole
  jti: string
  exp: number
  // Marca temporal (epoch segundos) del login original; se preserva en cada renovación
  // silenciosa para poder aplicar el tope absoluto de sesión. OBLIGATORIO.
  login_at: number
  // Marca temporal (epoch segundos) de emisión del token. OBLIGATORIO en el Admin:
  // se incluye solo por paridad de tipos con el backend (el reloj deriva la vida como `exp - now`).
  iat: number
  nombre?: string
}

export interface AuthState {
  token: string | null
  claims: JwtClaims | null
  isAuthenticated: boolean
}