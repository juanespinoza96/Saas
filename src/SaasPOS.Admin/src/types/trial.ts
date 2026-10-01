// Tipos TypeScript para el módulo de Prueba Gratuita (Trial)

// --- Request DTOs ---

/** Solicitud para crear un comercio con trial */
export interface CreateTrialRequest {
  /** RUC del comercio — exactamente 13 dígitos numéricos */
  ruc: string
  /** Razón social del comercio (1-200 caracteres) */
  razonSocial: string
}

/** Solicitud para convertir un trial a suscripción paga */
export interface ConvertTrialRequest {
  /** ID del plan destino (Básico, Intermedio o Empresarial) */
  planId: number
}

/** Solicitud para extender el período de trial */
export interface ExtendTrialRequest {
  /** Días adicionales a agregar (entre 1 y 15 inclusive) */
  diasAdicionales: number
}

// --- Response DTOs ---

/** Resultado de la creación de un trial */
export interface TrialResultDto {
  /** ID del comercio creado */
  comercioId: number
  /** ID de la suscripción creada */
  suscripcionId: number
  /** Razón social del comercio */
  razonSocial: string
  /** Fecha de inicio del trial (formato DateOnly: YYYY-MM-DD) */
  fechaInicio: string
  /** Fecha de próximo corte (formato DateOnly: YYYY-MM-DD) */
  fechaProximoCorte: string
}

/** Resultado de la conversión de trial a suscripción paga */
export interface ConversionResultDto {
  /** Nombre del plan asignado */
  nombrePlan: string
  /** Monto de la cuota mensual */
  montoCuota: number
  /** Nueva fecha de próximo corte (formato DateOnly: YYYY-MM-DD) */
  fechaProximoCorte: string
}

/** Resultado de la extensión de un trial */
export interface ExtensionResultDto {
  /** Nueva fecha de próximo corte tras la extensión (formato DateOnly: YYYY-MM-DD) */
  nuevaFechaProximoCorte: string
}

/** Datos de un trial individual para el listado */
export interface TrialDto {
  /** ID de la suscripción */
  suscripcionId: number
  /** ID del comercio */
  comercioId: number
  /** Razón social del comercio */
  razonSocial: string
  /** RUC del comercio */
  ruc: string
  /** Fecha de inicio del trial */
  fechaInicio: string
  /** Fecha de próximo corte */
  fechaProximoCorte: string
  /** Días restantes hasta la expiración (0 si ya expiró) */
  diasRestantes: number
  /** Estado actual de la suscripción ("Trial" o "Trial_Expirado") */
  estado: string
}

/** Resumen de contadores de trials para el panel */
export interface TrialResumenDto {
  /** Total de trials activos (Estado_Trial) */
  totalActivos: number
  /** Trials por expirar (Estado_Trial con ≤5 días restantes) */
  porExpirar: number
  /** Trials expirados pendientes de conversión (Estado_Trial_Expirado) */
  expirados: number
}
