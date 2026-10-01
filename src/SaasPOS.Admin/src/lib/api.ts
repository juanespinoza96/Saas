/**
 * API client para el panel SuperAdmin con cache automático para GET requests.
 * - GET requests se cachean por 30 segundos.
 * - Mutaciones (POST/PUT/PATCH/DELETE) invalidan el cache del endpoint afectado.
 * - Requests duplicados en vuelo se deduplican.
 */

import { detectTimezone } from './dateUtils'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'https://api.saas.com'
const AUTH_TOKEN_KEY = 'saas-admin-token'

/** TTL por defecto del cache en milisegundos (30 segundos) */
const DEFAULT_CACHE_TTL = 30_000

interface RequestOptions {
  method?: string
  body?: unknown
  headers?: Record<string, string>
  /** Si true, omite el cache para esta petición GET específica */
  skipCache?: boolean
}

interface CacheEntry<T = unknown> {
  data: T
  timestamp: number
  ttl: number
}

/** Cache en memoria */
const cache = new Map<string, CacheEntry>()
/** Requests en vuelo para deduplicación */
const inflight = new Map<string, Promise<unknown>>()

function getAuthToken(): string | null {
  try {
    return localStorage.getItem(AUTH_TOKEN_KEY)
  } catch {
    return null
  }
}

function isCacheValid(entry: CacheEntry): boolean {
  return Date.now() - entry.timestamp < entry.ttl
}

/**
 * Invalida entradas del cache que coincidan con el prefijo del endpoint.
 */
function invalidateCache(endpoint: string): void {
  const basePath = endpoint.split('?')[0] ?? endpoint
  const segments = basePath.split('/').filter(Boolean)

  const keysToDelete: string[] = []
  for (const key of cache.keys()) {
    const keyBase = key.split('?')[0] ?? key
    if (keyBase.startsWith(basePath) || basePath.startsWith(keyBase)) {
      keysToDelete.push(key)
    } else {
      const keySegments = keyBase.split('/').filter(Boolean)
      const minLen = Math.min(segments.length, keySegments.length)
      if (minLen >= 3) {
        const sharedPrefix = segments.slice(0, 3).join('/')
        const keyPrefix = keySegments.slice(0, 3).join('/')
        if (sharedPrefix === keyPrefix) {
          keysToDelete.push(key)
        }
      }
    }
  }
  keysToDelete.forEach(k => cache.delete(k))
}

/**
 * Limpia todo el cache. Útil al hacer logout.
 */
export function clearApiCache(): void {
  cache.clear()
  inflight.clear()
}

import type {
  CreateTrialRequest,
  TrialResultDto,
  TrialDto,
  TrialResumenDto,
  ConvertTrialRequest,
  ConversionResultDto,
  ExtendTrialRequest,
  ExtensionResultDto,
} from '../types/trial'

import type {
  PendingRecoveryAdmin,
  ApproveResponse,
  RejectRecoveryRequest,
} from '../types/passwordRecovery'

export async function apiRequest<T>(endpoint: string, options: RequestOptions = {}): Promise<T> {
  const { method = 'GET', body, headers = {}, skipCache } = options
  const upperMethod = method.toUpperCase()

  // Para GET: verificar cache primero
  if (upperMethod === 'GET' && !skipCache) {
    const cached = cache.get(endpoint)
    if (cached && isCacheValid(cached)) {
      return cached.data as T
    }

    // Deduplicar requests en vuelo
    const existing = inflight.get(endpoint)
    if (existing) {
      return existing as Promise<T>
    }
  }

  const token = getAuthToken()
  const requestHeaders: Record<string, string> = {
    'Content-Type': 'application/json',
    'X-Timezone': detectTimezone(),
    ...headers,
  }

  if (token) {
    requestHeaders['Authorization'] = `Bearer ${token}`
  }

  const fetchPromise = (async (): Promise<T> => {
    const response = await fetch(`${API_BASE_URL}${endpoint}`, {
      method: upperMethod,
      headers: requestHeaders,
      body: body ? JSON.stringify(body) : undefined,
    })

    if (!response.ok) {
      throw new Error(`API Error: ${response.status} ${response.statusText}`)
    }

    const data = await response.json() as T

    // Para GET exitosos: almacenar en cache
    if (upperMethod === 'GET' && !skipCache) {
      cache.set(endpoint, {
        data,
        timestamp: Date.now(),
        ttl: DEFAULT_CACHE_TTL,
      })
    }

    return data
  })()

  // Registrar request en vuelo para deduplicación (solo GET)
  if (upperMethod === 'GET' && !skipCache) {
    inflight.set(endpoint, fetchPromise)
    fetchPromise.finally(() => {
      inflight.delete(endpoint)
    })
  }

  // Para mutaciones: invalidar cache relacionado después del éxito
  if (upperMethod !== 'GET') {
    fetchPromise.then(() => {
      invalidateCache(endpoint)
    }).catch(() => {
      // Si falla la mutación, no invalidar
    })
  }

  return fetchPromise
}


// --- Funciones de API para Trials ---

/** Crea un comercio con período de prueba gratuita */
export function crearTrial(request: CreateTrialRequest): Promise<TrialResultDto> {
  return apiRequest<TrialResultDto>('/api/admin/trials', {
    method: 'POST',
    body: request,
  })
}

/** Obtiene el listado de todos los trials */
export function obtenerTrials(): Promise<TrialDto[]> {
  return apiRequest<TrialDto[]>('/api/admin/trials')
}

/** Obtiene el resumen de contadores de trials para el panel */
export function obtenerResumenTrials(): Promise<TrialResumenDto> {
  return apiRequest<TrialResumenDto>('/api/admin/trials/resumen')
}

/** Convierte un trial a suscripción paga */
export function convertirTrial(suscripcionId: number, request: ConvertTrialRequest): Promise<ConversionResultDto> {
  return apiRequest<ConversionResultDto>(`/api/admin/trials/${suscripcionId}/convertir`, {
    method: 'POST',
    body: request,
  })
}

/** Extiende el período de prueba de un trial */
export function extenderTrial(suscripcionId: number, request: ExtendTrialRequest): Promise<ExtensionResultDto> {
  return apiRequest<ExtensionResultDto>(`/api/admin/trials/${suscripcionId}/extender`, {
    method: 'POST',
    body: request,
  })
}


// --- Funciones de API para Recuperación de Contraseña (Dueños cross-tenant) ---

/**
 * Obtiene las solicitudes de recuperación de usuarios con rol Dueño de cualquier comercio.
 * Se omite el cache para reflejar siempre el estado más reciente al abrir el modal.
 */
export function obtenerSolicitudesRecuperacionDuenos(): Promise<PendingRecoveryAdmin[]> {
  return apiRequest<PendingRecoveryAdmin[]>('/api/admin/password-recovery/pending', {
    skipCache: true,
  })
}

/** Aprueba una solicitud de recuperación y devuelve la contraseña temporal en texto plano. */
export function aprobarRecuperacionDueno(requestId: number): Promise<ApproveResponse> {
  return apiRequest<ApproveResponse>(`/api/admin/password-recovery/approve/${requestId}`, {
    method: 'POST',
  })
}

/** Rechaza una solicitud de recuperación con un motivo obligatorio. */
export function rechazarRecuperacionDueno(
  requestId: number,
  request: RejectRecoveryRequest,
): Promise<void> {
  return apiRequest<void>(`/api/admin/password-recovery/reject/${requestId}`, {
    method: 'POST',
    body: request,
  })
}

/**
 * Reconsulta la contraseña temporal vigente de una solicitud Aprobada (reconsulta ilimitada
 * mientras la temporal no expire). Se omite el cache para no servir una temporal ya vencida.
 */
export function obtenerTemporalRecuperacionDueno(requestId: number): Promise<ApproveResponse> {
  return apiRequest<ApproveResponse>(`/api/admin/password-recovery/${requestId}/temp-password`, {
    skipCache: true,
  })
}
