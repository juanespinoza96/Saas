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
  // Marca temporal (epoch segundos) del login original; se preserva en cada renovación
  // silenciosa para poder aplicar el tope absoluto de sesión. OBLIGATORIO.
  login_at: number
  // Marca temporal (epoch segundos) de emisión del token. OPCIONAL en el POS:
  // la vida del token se deriva como `exp - now`, no a partir de `iat`.
  iat?: number
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
