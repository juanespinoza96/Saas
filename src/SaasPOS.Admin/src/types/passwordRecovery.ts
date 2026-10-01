// Tipos TypeScript para el módulo de Recuperación de Contraseña Jerárquica (panel Admin).
// El backend serializa en camelCase, por lo que los nombres de campo aquí reflejan esa convención.

/** Estado de una solicitud de recuperación. */
export type EstadoSolicitud = 'Pendiente' | 'Aprobada' | 'Rechazada'

/**
 * Solicitud de recuperación de un usuario con rol Dueño, en su variante cross-tenant Admin.
 * Corresponde a PendingRecoveryAdminDto del backend e incluye la información del comercio
 * del solicitante para mostrar la columna de comercio en el modal (Req 13.2).
 */
export interface PendingRecoveryAdmin {
  /** Identificador de la SolicitudRecuperacion. */
  requestId: number
  /** Identificador del usuario solicitante. */
  usuarioId: number
  /** Nombre del usuario solicitante. */
  nombre: string
  /** Correo del usuario solicitante. */
  email: string
  /** Rol del usuario solicitante (siempre Dueño en la variante Admin). */
  rol: string
  /** Estado de la solicitud: Pendiente, Aprobada o Rechazada. */
  estado: EstadoSolicitud
  /** Marca de tiempo (UTC, ISO 8601) en que se creó la solicitud. */
  fechaSolicitud: string
  /** Fecha de expiración de la temporal; null mientras la solicitud está Pendiente. */
  fechaExpiracion: string | null
  /** Minutos restantes antes de la expiración (0 si ya expiró o no aplica). */
  minutosRestantes: number
  /** Indica si la contraseña temporal aún es vigente (Aprobada y sin expirar). */
  tienePasswordTemporalVigente: boolean
  /** Identificador del comercio (tenant) del usuario solicitante. */
  comercioId: number
  /** Nombre del comercio del usuario solicitante. */
  comercioNombre: string
}

/**
 * Respuesta de aprobación y de reconsulta de la temporal. Corresponde a ApproveResponse
 * del backend: contraseña temporal en texto plano y su fecha de expiración (Req 4.6, 5.1).
 */
export interface ApproveResponse {
  /** Contraseña temporal en texto plano (12 caracteres, cumple la política). */
  tempPassword: string
  /** Fecha de expiración (UTC, ISO 8601) de la contraseña temporal. */
  fechaExpiracion: string
}

/** Cuerpo de la solicitud de rechazo: motivo obligatorio (Req 13.7, 15.2). */
export interface RejectRecoveryRequest {
  /** Motivo del rechazo; no puede estar vacío ni contener solo espacios. */
  motivoRechazo: string
}
