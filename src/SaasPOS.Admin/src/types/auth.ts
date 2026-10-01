// SuperAdmin panel only allows the SuperAdmin role
export type AdminRole = 'SuperAdmin'

export interface JwtClaims {
  sub: string
  role: AdminRole
  jti: string
  exp: number
  nombre?: string
}

export interface AuthState {
  token: string | null
  claims: JwtClaims | null
  isAuthenticated: boolean
}
