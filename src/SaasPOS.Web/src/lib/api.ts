/**
 * API client con inyección de JWT y cache automático para GET requests.
 * - GET requests se cachean por 30 segundos (configurable por endpoint).
 * - Mutaciones (POST/PUT/PATCH/DELETE) invalidan el cache del endpoint afectado.
 * - Requests duplicados en vuelo se deduplicane (misma URL → una sola petición de red).
 */

import { detectTimezone } from './dateUtils'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'https://api.saas.com'
const AUTH_TOKEN_KEY = 'saas-pos-token'

/** TTL por defecto del cache en milisegundos (30 segundos) */
const DEFAULT_CACHE_TTL = 30_000

interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: unknown
  /** Si true, omite el cache para esta petición GET específica */
  skipCache?: boolean
}

interface ApiError {
  status: number
  message: string
  details?: unknown
}

interface CacheEntry<T = unknown> {
  data: T
  timestamp: number
  ttl: number
}

class ApiClient {
  private baseUrl: string
  /** Cache en memoria: key = endpoint URL completa, value = datos + timestamp */
  private cache = new Map<string, CacheEntry>()
  /** Requests en vuelo para deduplicación: key = endpoint, value = promise */
  private inflight = new Map<string, Promise<unknown>>()
  /** Callback invocado cuando el servidor responde 401 (sesión invalidada) */
  private onUnauthorized: (() => void) | null = null

  constructor(baseUrl: string) {
    this.baseUrl = baseUrl
  }

  /**
   * Registra un callback que se ejecuta cuando el servidor responde 401.
   * Permite al AuthContext cerrar la sesión y redirigir al login.
   */
  setOnUnauthorized(callback: () => void): void {
    this.onUnauthorized = callback
  }

  private getToken(): string | null {
    try {
      return localStorage.getItem(AUTH_TOKEN_KEY)
    } catch {
      return null
    }
  }

  /**
   * Verifica si una entrada del cache sigue vigente.
   */
  private isCacheValid(entry: CacheEntry): boolean {
    return Date.now() - entry.timestamp < entry.ttl
  }

  /**
   * Invalida entradas del cache que coincidan con el prefijo del endpoint.
   * Ejemplo: POST a /api/tenants/productos invalida:
   *   - /api/tenants/productos
   *   - /api/tenants/productos?search=...
   *   - /api/tenants/productos/5/precios-volumen
   */
  private invalidateCache(endpoint: string): void {
    // Extraer el recurso base (sin query params ni IDs numéricos al final)
    const basePath = endpoint.split('?')[0] ?? endpoint
    // Segmentos para matching parcial
    const segments = basePath.split('/').filter(Boolean)

    const keysToDelete: string[] = []
    for (const key of this.cache.keys()) {
      const keyBase = key.split('?')[0] ?? key
      // Invalida si el endpoint del cache comienza con el basePath de la mutación
      // o si comparten los primeros segmentos del recurso
      if (keyBase.startsWith(basePath) || basePath.startsWith(keyBase)) {
        keysToDelete.push(key)
      } else {
        // Invalidar también si comparten el recurso padre
        // Ejemplo: DELETE /productos/5 invalida /productos
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
    keysToDelete.forEach(k => this.cache.delete(k))
  }

  /**
   * Limpia todo el cache. Útil al hacer logout.
   */
  clearCache(): void {
    this.cache.clear()
    this.inflight.clear()
  }

  private async request<T>(endpoint: string, options: RequestOptions = {}): Promise<T> {
    const { body, headers: customHeaders, skipCache, ...restOptions } = options
    const method = (restOptions.method ?? 'GET').toUpperCase()

    // Para GET: verificar cache primero
    if (method === 'GET' && !skipCache) {
      const cached = this.cache.get(endpoint)
      if (cached && this.isCacheValid(cached)) {
        return cached.data as T
      }

      // Deduplicar requests en vuelo al mismo endpoint
      const existing = this.inflight.get(endpoint)
      if (existing) {
        return existing as Promise<T>
      }
    }

    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...customHeaders as Record<string, string>,
    }

    const token = this.getToken()
    if (token) {
      headers['Authorization'] = `Bearer ${token}`
      // Inyectar zona horaria del navegador en cada request autenticada
      headers['X-Timezone'] = detectTimezone()
    }

    const config: RequestInit = {
      ...restOptions,
      headers,
    }

    if (body !== undefined) {
      config.body = JSON.stringify(body)
    }

    const fetchPromise = (async (): Promise<T> => {
      const response = await fetch(`${this.baseUrl}${endpoint}`, config)

      if (!response.ok) {
        const error: ApiError = {
          status: response.status,
          message: response.statusText,
        }
        try {
          error.details = await response.json()
        } catch {
          // Response body no es JSON
        }

        // Si el servidor responde 401, la sesión fue invalidada — cerrar sesión automáticamente
        if (response.status === 401 && this.onUnauthorized) {
          this.onUnauthorized()
        }

        throw error
      }

      // Manejar 204 No Content
      if (response.status === 204) {
        return undefined as T
      }

      const data = await response.json() as T

      // Para GET exitosos: almacenar en cache
      if (method === 'GET' && !skipCache) {
        this.cache.set(endpoint, {
          data,
          timestamp: Date.now(),
          ttl: DEFAULT_CACHE_TTL,
        })
      }

      return data
    })()

    // Registrar request en vuelo para deduplicación (solo GET)
    if (method === 'GET' && !skipCache) {
      this.inflight.set(endpoint, fetchPromise)
      // La cadena de limpieza es derivada de fetchPromise; adjuntar .catch() de no-op
      // para no dejar rechazos huérfanos (Uncaught in promise) cuando el GET falla.
      // El consumidor sigue recibiendo la promesa original (fetchPromise) intacta.
      fetchPromise
        .finally(() => {
          this.inflight.delete(endpoint)
        })
        .catch(() => {
          // El error real ya se propaga al consumidor vía la promesa original retornada
        })
    }

    // Para mutaciones: invalidar cache relacionado
    if (method !== 'GET') {
      // Invalidar después de que la mutación tenga éxito
      fetchPromise.then(() => {
        this.invalidateCache(endpoint)
      }).catch(() => {
        // Si falla la mutación, no invalidar
      })
    }

    return fetchPromise
  }

  get<T>(endpoint: string, options?: RequestOptions): Promise<T> {
    return this.request<T>(endpoint, { ...options, method: 'GET' })
  }

  post<T>(endpoint: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.request<T>(endpoint, { ...options, method: 'POST', body })
  }

  put<T>(endpoint: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.request<T>(endpoint, { ...options, method: 'PUT', body })
  }

  patch<T>(endpoint: string, body?: unknown, options?: RequestOptions): Promise<T> {
    return this.request<T>(endpoint, { ...options, method: 'PATCH', body })
  }

  delete<T>(endpoint: string, options?: RequestOptions): Promise<T> {
    return this.request<T>(endpoint, { ...options, method: 'DELETE' })
  }
}

export const api = new ApiClient(API_BASE_URL)
export type { ApiError }
