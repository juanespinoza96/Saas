/**
 * Tipos del flujo de recuperación de contraseña jerárquica en el POS.
 * Reflejan el contrato del backend (PendingRecoveryDto) expuesto por
 * GET /api/tenants/auth/password-recovery/pending.
 */

/** Estados posibles de una solicitud de recuperación. */
export type EstadoSolicitudRecuperacion = 'Pendiente' | 'Aprobada' | 'Rechazada'

/**
 * DTO de una solicitud de recuperación pendiente o resuelta, tal como la
 * entrega el backend a un aprobador (Dueño o Gerente) del mismo comercio.
 */
export interface PendingRecoveryDto {
  /** Identificador de la solicitud. */
  requestId: number
  /** Identificador del usuario solicitante. */
  usuarioId: number
  /** Nombre del solicitante. */
  nombre: string
  /** Correo del solicitante. */
  email: string
  /** Rol del solicitante (Cajero, Bodeguero, Supervisor, Gerente...). */
  rol: string
  /** Estado actual de la solicitud. */
  estado: EstadoSolicitudRecuperacion
  /** Marca de tiempo ISO 8601 de la solicitud. */
  fechaSolicitud: string
  /** Expiración de la contraseña temporal (null mientras está Pendiente). */
  fechaExpiracion: string | null
  /** Minutos restantes antes de expirar (solicitud pendiente o temporal). */
  minutosRestantes: number
  /** Indica si la solicitud aún tiene una contraseña temporal vigente. */
  tienePasswordTemporalVigente: boolean
}

/**
 * Respuesta del endpoint de aprobación y de la reconsulta de la contraseña
 * temporal (POST password-recovery/approve/{id}). Refleja el DTO backend
 * ApproveResponse: la contraseña temporal en texto plano y su expiración.
 */
export interface ApproveResponse {
  /** Contraseña temporal en texto plano (12 caracteres, cumple la política). */
  tempPassword: string
  /** Marca de tiempo ISO 8601 (UTC) de expiración de la contraseña temporal. */
  fechaExpiracion: string
}

/** Body del endpoint de rechazo (POST password-recovery/reject/{id}). */
export interface RejectRecoveryRequest {
  /** Motivo obligatorio del rechazo. */
  motivoRechazo: string
}
