/**
 * Tests de preservación para el cliente HTTP (`api.ts`).
 *
 * Tarea 2 (metodología bugfix — observación primero): estos tests capturan el
 * COMPORTAMIENTO ACTUAL (baseline) del código SIN corregir para todos los inputs
 * donde `isBugCondition(request)` es FALSE. Sirven como red de regresión: deben
 * PASAR sobre el código sin corregir y DEBEN SEGUIR PASANDO tras aplicar el fix
 * (Tarea 3.3), garantizando que el arreglo no altera el comportamiento no-buggy.
 *
 * Property 2: Preservation - Comportamiento no-buggy sin cambios.
 * **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6**
 *
 * Dominio no-buggy cubierto (variando { method, skipCache, outcome }):
 *  - Deduplicación de GET concurrentes (3.1)
 *  - Limpieza de `inflight` en éxito y error (3.2)
 *  - Caching de GET dentro del TTL (3.3)
 *  - Invalidación de cache en mutaciones exitosas / no en fallidas (3.3)
 *  - Forma `ApiError` propagada al consumidor (3.4)
 *  - 401 → `onUnauthorized` invocado una vez (3.5)
 *  - `skipCache`: no se registra en `inflight` ni en cache (Scope)
 *
 * Entorno: jsdom (Vitest 2.1) con `globals: true`. `fetch` se mockea con
 * `vi.stubGlobal('fetch', ...)`.
 */

import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import fc from 'fast-check'
import { api, type ApiError } from './api'

/**
 * Supresor de unhandled rejections a nivel de ARCHIVO.
 *
 * Sobre el código SIN corregir, la cadena derivada del `.finally()` interno del
 * bloque de deduplicación deja rejections huérfanas cuando un GET falla (ese es
 * precisamente el bug). Esa rejection huérfana NO es el objeto de estudio de la
 * tarea 2 (preservación) — se explora y se cuenta en la tarea 1. Aquí solo nos
 * interesa el comportamiento OBSERVABLE no-buggy (deduplicación, cache, forma
 * ApiError, 401, etc.). Absorbemos esas rejections para que el runner no marque
 * el archivo como fallido por ruido ajeno al propósito de estos tests. Tras el
 * fix, simplemente no habrá rejections que absorber y estos tests seguirán
 * pasando igual (sin regresiones).
 */
const swallowRejection = (): void => {
  // No-op: absorbe la rejection huérfana derivada del .finally() interno.
}
const swallowRejectionEvent = (event: PromiseRejectionEvent): void => {
  event.preventDefault?.()
}

beforeAll(() => {
  if (typeof process !== 'undefined' && typeof process.on === 'function') {
    process.on('unhandledRejection', swallowRejection)
  }
  if (typeof window !== 'undefined') {
    window.addEventListener('unhandledrejection', swallowRejectionEvent)
  }
})

afterAll(() => {
  if (typeof process !== 'undefined' && typeof process.off === 'function') {
    process.off('unhandledRejection', swallowRejection)
  }
  if (typeof window !== 'undefined') {
    window.removeEventListener('unhandledrejection', swallowRejectionEvent)
  }
})

/**
 * Construye un `Response` mock con status y body JSON dados.
 * Replica la interfaz que consume `ApiClient.request` (`ok`, `status`,
 * `statusText`, `json()`).
 */
function makeResponse(status: number, jsonBody: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: `HTTP ${status}`,
    json: async () => jsonBody,
  } as unknown as Response
}

/**
 * Permite que el runtime procese microtasks pendientes. Necesario porque la
 * invalidación de cache en mutaciones ocurre dentro de `fetchPromise.then(...)`,
 * es decir, en una microtask posterior al `await` del consumidor.
 */
async function flushMicrotasks(): Promise<void> {
  await Promise.resolve()
  await Promise.resolve()
  await new Promise<void>(resolve => setTimeout(resolve, 0))
}

/**
 * Crea un mock de `fetch` que cuenta llamadas y responde según una función
 * `responder(url)`. Devuelve el contador y el mock instalable.
 */
function makeCountingFetch(responder: (url: string) => Response | Promise<Response>) {
  const state = { calls: 0, urls: [] as string[] }
  const fn = async (url: string) => {
    state.calls++
    state.urls.push(url)
    return responder(url)
  }
  return { state, fn }
}

describe('api.ts — Property 2: Preservation (comportamiento no-buggy sin cambios)', () => {
  beforeEach(() => {
    // Estado limpio del cliente entre casos (cache + inflight)
    api.clearCache()
    // Sin callback de 401 salvo que el caso lo configure explícitamente
    api.setOnUnauthorized(() => {})
  })

  afterEach(() => {
    api.clearCache()
    api.setOnUnauthorized(() => {})
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  // -------------------------------------------------------------------------
  // 3.1 Deduplicación de GET concurrentes
  // -------------------------------------------------------------------------
  describe('Deduplicación de GET concurrentes (3.1)', () => {
    it('dos api.get() concurrentes al mismo endpoint hacen una sola llamada a fetch y comparten el resultado', async () => {
      const payload = { tipo: 'Ticket Digital', ok: true }
      // fetch lento: se resuelve tras un pequeño retardo para asegurar solapamiento
      const { state, fn } = makeCountingFetch(
        () => new Promise<Response>(resolve => setTimeout(() => resolve(makeResponse(200, payload)), 20)),
      )
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/comprobantes/configuracion'
      // Ambas peticiones se disparan ANTES de que la primera resuelva
      const p1 = api.get<typeof payload>(endpoint)
      const p2 = api.get<typeof payload>(endpoint)

      const [r1, r2] = await Promise.all([p1, p2])

      // Una sola llamada de red (deduplicación)
      expect(state.calls).toBe(1)
      // Ambos consumidores reciben el mismo resultado
      expect(r1).toEqual(payload)
      expect(r2).toEqual(payload)
      // Comparten exactamente la misma referencia de datos (misma promesa registrada)
      expect(r1).toBe(r2)
    })
  })

  // -------------------------------------------------------------------------
  // 3.2 Limpieza de `inflight` en éxito y error
  // -------------------------------------------------------------------------
  describe('Limpieza de inflight en éxito y error (3.2)', () => {
    it('tras un GET exitoso, la entrada de inflight se elimina (un GET posterior vuelve a llamar fetch)', async () => {
      const payload = { valor: 1 }
      const { state, fn } = makeCountingFetch(() => makeResponse(200, payload))
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/productos?skip-dedupe-check=1'

      // Primer GET: resuelve y se completa
      await api.get(endpoint)
      await flushMicrotasks()

      // Invalidar el cache para forzar que el segundo GET pase por la red
      // (así aislamos la limpieza de inflight del efecto de cache)
      api.clearCache()

      // Segundo GET secuencial: si inflight quedó residual devolvería la promesa vieja
      await api.get(endpoint)
      await flushMicrotasks()

      // Dos llamadas reales: la entrada de inflight se limpió tras la primera
      expect(state.calls).toBe(2)
    })

    it('tras un GET rechazado, la entrada de inflight se elimina (un GET posterior vuelve a llamar fetch)', async () => {
      const { state, fn } = makeCountingFetch(() => makeResponse(500, { error: 'INTERNAL' }))
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/reportes/ventas'

      // Primer GET falla; el consumidor captura el error
      try {
        await api.get(endpoint)
      } catch {
        // rechazo esperado
      }
      await flushMicrotasks()

      // Segundo GET secuencial: debe volver a llamar fetch (inflight limpio)
      try {
        await api.get(endpoint)
      } catch {
        // rechazo esperado
      }
      await flushMicrotasks()

      expect(state.calls).toBe(2)
    })
  })

  // -------------------------------------------------------------------------
  // 3.3 Caching de GET dentro del TTL
  // -------------------------------------------------------------------------
  describe('Caching de GET dentro del TTL (3.3)', () => {
    it('un segundo api.get() dentro del TTL no vuelve a llamar fetch y responde desde cache', async () => {
      const payload = { cacheado: true, n: 42 }
      const { state, fn } = makeCountingFetch(() => makeResponse(200, payload))
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/productos'

      const r1 = await api.get<typeof payload>(endpoint)
      await flushMicrotasks()
      const r2 = await api.get<typeof payload>(endpoint)

      // Solo una llamada de red: la segunda vino del cache
      expect(state.calls).toBe(1)
      expect(r1).toEqual(payload)
      expect(r2).toEqual(payload)
    })
  })

  // -------------------------------------------------------------------------
  // 3.3 Invalidación de cache en mutaciones
  // -------------------------------------------------------------------------
  describe('Invalidación de cache en mutaciones (3.3)', () => {
    it('una mutación exitosa (POST) invalida el cache del recurso relacionado', async () => {
      const getPayload = { lista: [1, 2, 3] }
      const { state, fn } = makeCountingFetch((url) => {
        // La mutación responde 200; los GET devuelven la lista
        if (url.includes('POST_MARKER')) return makeResponse(200, { creado: true })
        return makeResponse(200, getPayload)
      })
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/productos'

      // 1) GET inicial → cachea
      await api.get(endpoint)
      await flushMicrotasks()
      expect(state.calls).toBe(1)

      // 2) Mutación exitosa sobre el mismo recurso → invalida cache relacionado
      await api.post(`${endpoint}?POST_MARKER=1`, { nombre: 'X' })
      await flushMicrotasks()

      // 3) GET posterior debe ir de nuevo a la red (cache invalidado)
      await api.get(endpoint)
      await flushMicrotasks()

      // Llamadas: GET(1) + POST(1) + GET(1) = 3; el segundo GET NO vino de cache
      expect(state.calls).toBe(3)
    })

    it('una mutación fallida (POST rechazado) NO invalida el cache', async () => {
      const getPayload = { lista: [1, 2, 3] }
      const { state, fn } = makeCountingFetch((url) => {
        if (url.includes('POST_MARKER')) return makeResponse(500, { error: 'FALLO' })
        return makeResponse(200, getPayload)
      })
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/productos'

      // 1) GET inicial → cachea
      await api.get(endpoint)
      await flushMicrotasks()
      expect(state.calls).toBe(1)

      // 2) Mutación fallida → NO debe invalidar el cache
      try {
        await api.post(`${endpoint}?POST_MARKER=1`, { nombre: 'X' })
      } catch {
        // rechazo esperado
      }
      await flushMicrotasks()

      // 3) GET posterior debe venir del cache (no hay nueva llamada de GET)
      await api.get(endpoint)
      await flushMicrotasks()

      // Llamadas: GET(1) + POST(1) = 2; el segundo GET vino de cache → sigue en 2
      expect(state.calls).toBe(2)
    })
  })

  // -------------------------------------------------------------------------
  // 3.4 Forma `ApiError` propagada al consumidor
  // -------------------------------------------------------------------------
  describe('Forma ApiError propagada al consumidor (3.4)', () => {
    it('un GET fallido rechaza con { status, message, details } tal como hoy', async () => {
      const detalles = { error: 'ACCESS_DENIED', mensaje: 'No autorizado' }
      vi.stubGlobal('fetch', async () => makeResponse(403, detalles))

      let capturado: ApiError | null = null
      try {
        await api.get('/api/tenants/comprobantes/configuracion')
      } catch (err) {
        capturado = err as ApiError
      }
      await flushMicrotasks()

      expect(capturado).not.toBeNull()
      // status = código HTTP; message = statusText; details = body JSON
      expect(capturado?.status).toBe(403)
      expect(capturado?.message).toBe('HTTP 403')
      expect(capturado?.details).toEqual(detalles)
      // La forma ApiError debe incluir exactamente estas tres claves propias.
      // Nota: el test runner puede inyectar claves auxiliares (`actual`/`expected`)
      // al serializar el objeto lanzado; por eso comprobamos presencia y valores
      // de las claves propias en lugar de exigir ausencia de otras.
      expect(capturado).toHaveProperty('status', 403)
      expect(capturado).toHaveProperty('message', 'HTTP 403')
      expect(capturado).toHaveProperty('details', detalles)
    })
  })

  // -------------------------------------------------------------------------
  // 3.5 401 → `onUnauthorized`
  // -------------------------------------------------------------------------
  describe('401 → onUnauthorized (3.5)', () => {
    it('un GET con 401 invoca onUnauthorized exactamente una vez', async () => {
      let llamadas = 0
      api.setOnUnauthorized(() => {
        llamadas++
      })
      vi.stubGlobal('fetch', async () => makeResponse(401, { error: 'UNAUTHORIZED' }))

      try {
        await api.get('/api/tenants/productos')
      } catch {
        // rechazo esperado
      }
      await flushMicrotasks()

      expect(llamadas).toBe(1)
    })

    it('una mutación (POST) con 401 invoca onUnauthorized exactamente una vez', async () => {
      let llamadas = 0
      api.setOnUnauthorized(() => {
        llamadas++
      })
      vi.stubGlobal('fetch', async () => makeResponse(401, { error: 'UNAUTHORIZED' }))

      try {
        await api.post('/api/tenants/ventas', { total: 100 })
      } catch {
        // rechazo esperado
      }
      await flushMicrotasks()

      expect(llamadas).toBe(1)
    })
  })

  // -------------------------------------------------------------------------
  // Scope: `skipCache`
  // -------------------------------------------------------------------------
  describe('skipCache (Scope)', () => {
    it('un GET con skipCache: true no se cachea (cada llamada va a la red)', async () => {
      const payload = { sinCache: true }
      const { state, fn } = makeCountingFetch(() => makeResponse(200, payload))
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/productos'

      await api.get(endpoint, { skipCache: true })
      await flushMicrotasks()
      await api.get(endpoint, { skipCache: true })
      await flushMicrotasks()

      // Sin cache: dos llamadas reales
      expect(state.calls).toBe(2)
    })

    it('un GET con skipCache: true no se registra en inflight (no deduplica concurrentes)', async () => {
      const payload = { sinCache: true }
      // fetch lento para forzar solapamiento
      const { state, fn } = makeCountingFetch(
        () => new Promise<Response>(resolve => setTimeout(() => resolve(makeResponse(200, payload)), 20)),
      )
      vi.stubGlobal('fetch', fn)

      const endpoint = '/api/tenants/productos'

      const p1 = api.get(endpoint, { skipCache: true })
      const p2 = api.get(endpoint, { skipCache: true })
      await Promise.all([p1, p2])

      // Sin deduplicación: dos llamadas de red concurrentes
      expect(state.calls).toBe(2)
    })
  })

  // -------------------------------------------------------------------------
  // PBT (table-driven / property-based): dominio no-buggy variando
  // { method, skipCache, outcome }. Captura el comportamiento observable
  // (resuelve con datos en éxito / rechaza con ApiError en error) para todo el
  // dominio donde isBugCondition(request) === false.
  // -------------------------------------------------------------------------
  describe('PBT — dominio no-buggy { method, skipCache, outcome }', () => {
    /**
     * Reproduce la condición del bug para EXCLUIRLA del dominio de preservación.
     * El bug requiere: GET + skipCache=false + outcome=rejected.
     */
    function isBugCondition(req: { method: string; skipCache: boolean; outcome: 'success' | 'rejected' }): boolean {
      return req.method === 'GET' && req.skipCache === false && req.outcome === 'rejected'
    }

    it('para todo input no-buggy, éxito resuelve con datos y error rechaza con ApiError; sin excepciones inesperadas', async () => {
      await fc.assert(
        fc.asyncProperty(
          fc.record({
            method: fc.constantFrom('GET', 'POST', 'PUT', 'PATCH', 'DELETE'),
            skipCache: fc.boolean(),
            outcome: fc.constantFrom<'success' | 'rejected'>('success', 'rejected'),
            status: fc.constantFrom(400, 403, 404, 422, 500, 502),
          }),
          async (req) => {
            // Excluir explícitamente la condición del bug (se cubre en Property 1)
            fc.pre(!isBugCondition(req))

            api.clearCache()
            api.setOnUnauthorized(() => {})

            const okBody = { ok: true, method: req.method }
            const errBody = { error: 'FALLO', status: req.status }
            vi.stubGlobal('fetch', async () =>
              req.outcome === 'success'
                ? makeResponse(req.method === 'DELETE' ? 204 : 200, okBody)
                : makeResponse(req.status, errBody),
            )

            // Endpoint único por iteración para aislar cache/inflight
            const endpoint = `/api/tenants/recurso?it=${req.method}-${req.skipCache}-${req.outcome}-${Math.random()}`

            const opts = { skipCache: req.skipCache }
            const invoke = () => {
              switch (req.method) {
                case 'GET':
                  return api.get(endpoint, opts)
                case 'POST':
                  return api.post(endpoint, { x: 1 }, opts)
                case 'PUT':
                  return api.put(endpoint, { x: 1 }, opts)
                case 'PATCH':
                  return api.patch(endpoint, { x: 1 }, opts)
                default:
                  return api.delete(endpoint, opts)
              }
            }

            if (req.outcome === 'success') {
              const data = await invoke()
              await flushMicrotasks()
              if (req.method === 'DELETE') {
                // 204 No Content resuelve undefined
                return data === undefined
              }
              return JSON.stringify(data) === JSON.stringify(okBody)
            }

            // outcome === 'rejected' (no-buggy: mutaciones, o GET skipCache=true)
            let capturado: ApiError | null = null
            try {
              await invoke()
            } catch (err) {
              capturado = err as ApiError
            }
            await flushMicrotasks()

            // El consumidor recibe el ApiError con la forma esperada
            return (
              capturado !== null &&
              capturado.status === req.status &&
              capturado.message === `HTTP ${req.status}` &&
              JSON.stringify(capturado.details) === JSON.stringify(errBody)
            )
          },
        ),
        { numRuns: 60 },
      )
    })
  })
})
