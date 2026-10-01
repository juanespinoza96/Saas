export type UserRole =
  | 'SuperAdmin'
  | 'Dueño'
  | 'Gerente'
  | 'Supervisor'
  | 'Bodeguero'
  | 'Cajero'

export interface JwtClaims {
  sub: string
  comercio_id: string
  role: UserRole
  jti: string
  exp: number
  comercio_nombre?: string
  sucursal_id?: string
  // Claim de cambio de contraseña: presente solo cuando el usuario debe cambiar su contraseña
  must_change_password?: boolean
}

export interface AuthState {
  token: string | null
  claims: JwtClaims | null
  isAuthenticated: boolean
}
