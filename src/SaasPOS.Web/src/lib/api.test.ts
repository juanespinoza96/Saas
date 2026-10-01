/**
 * Tests de exploración de la condición del bug para el cliente HTTP (`api.ts`).
 *
 * Tarea 1 (metodología bugfix): estos tests codifican el COMPORTAMIENTO ESPERADO
 * (Property 1). Se ejecutan sobre el código SIN corregir para PROVOCAR
 * counterexamples que demuestren la rejection huérfana generada por la cadena
 * derivada del `.finally()` interno del bloque de deduplicación (`inflight`).
 *
 * RESULTADO ESPERADO EN CÓDIGO SIN CORREGIR: FALLAN (esto confirma el bug).
 * Cuando se aplique el fix (Tarea 3.1), estos mismos tests PASARÁN.
 *
 * Property 1: Bug Condition - GET rechazado no genera rejection no capturada.
 * **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
 */

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import fc from 'fast-check'
import { api, type ApiError } from './api'

/**
 * Rastreador de rejections de promesa no capturadas.
 *
 * El síntoma del bug es una rejection huérfana (`Uncaught (in promise)`). Para
 * observarla registramos listeners tanto sobre `window` (entorno jsdom) como
 * sobre `process` (runtime Node de Vitest), ya que la fiabilidad del reporte
 * depende del entorno. Contamos cualquier rejection capturada.
 */
class UnhandledRejectionTracker {
  private count = 0
  private reasons: unknown[] = []
  private readonly windowHandler: (event: PromiseRejectionEvent) => void
  private readonly processHandler: (reason: unknown) => void

  constructor() {
    this.windowHandler = (event: PromiseRejectionEvent) => {
      // Evitar que jsdom escriba el "Uncaught (in promise)" en la consola del test
      event.preventDefault?.()
      this.count++
      this.reasons.push(event.reason)
    }
    this.processHandler = (reason: unknown) => {
      this.count++
      this.reasons.push(reason)
    }
  }

  start(): void {
    this.count = 0
    this.reasons = []
    if (typeof window !== 'undefined') {
      window.addEventListener('unhandledrejection', this.windowHandler)
    }
    if (typeof process !== 'undefined' && typeof process.on === 'function') {
      process.on('unhandledRejection', this.processHandler)
    }
  }

  stop(): void {
    if (typeof window !== 'undefined') {
      window.removeEventListener('unhandledrejection', this.windowHandler)
    }
    if (typeof process !== 'undefined' && typeof process.off === 'function') {
      process.off('unhandledRejection', this.processHandler)
    }
  }

  getCount(): number {
    return this.count
  }

  getReasons(): unknown[] {
    return this.reasons
  }
}

/**
 * Permite que el runtime procese microtasks y macrotasks pendientes para que,
 * si existe una rejection huérfana, sea reportada ANTES de las aserciones.
 */
async function flushPendingRejections(): Promise<void> {
  // Drenar microtasks (la cadena derivada del .finally())
  await Promise.resolve()
  await Promise.resolve()
  // Drenar una macrotask para dar tiempo al runtime a emitir el evento
  await new Promise<void>(resolve => setTimeout(resolve, 0))
}

/**
 * Construye un `Response` mock con status y body JSON dados.
 */
function makeResponse(status: number, jsonBody: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: `HTTP ${status}`,
    json: async () => jsonBody,
  } as unknown as Response
}

describe('api.ts — Property 1: Bug Condition (GET rechazado no genera rejection no capturada)', () => {
  let tracker: UnhandledRejectionTracker

  beforeEach(() => {
    tracker = new UnhandledRejectionTracker()
    // Estado limpio del cliente entre casos (cache + inflight)
    api.clearCache()
  })

  afterEach(() => {
    tracker.stop()
    api.clearCache()
    vi.unstubAllGlobals()
  })

  /**
   * PBT acotado: para el conjunto de inputs donde isBugCondition(request) es true
   * (method === 'GET', skipCache === false, outcome === 'rejected'), variando el
   * status del error HTTP, un GET rechazado NO debe dejar rejections huérfanas.
   *
   * En el código SIN corregir este test FALLA (>= 1 unhandledrejection).
   */
  it('no produce ninguna unhandledrejection para GET rechazados con distintos status (403/500)', async () => {
    await fc.assert(
      fc.asyncProperty(
        // Generar múltiples GET rechazados variando el status del error
        fc.constantFrom(403, 500, 404, 422, 502),
        async (status) => {
          const tracker2 = new UnhandledRejectionTracker()
          tracker2.start()
          api.clearCache()

          const errorBody = { error: 'ACCESS_DENIED', codigo: status }
          vi.stubGlobal('fetch', async () => makeResponse(status, errorBody))

          // Endpoint único por iteración para aislar la entrada de inflight
          const endpoint = `/api/tenants/comprobantes/configuracion?s=${status}-${Math.random()}`

          // El consumidor captura la promesa ORIGINAL vía try/catch
          let capturado: ApiError | null = null
          try {
            await api.get(endpoint)
          } catch (err) {
            capturado = err as ApiError
          }

          // Permitir que la cadena derivada del .finally() reporte su rejection
          await flushPendingRejections()

          const orphanCount = tracker2.getCount()
          tracker2.stop()

          // Aserciones (Expected Behavior Properties):
          // 1) el consumidor recibe el error original sin cambios (forma ApiError)
          expect(capturado).not.toBeNull()
          expect(capturado?.status).toBe(status)
          // 2) NO hay rejections huérfanas (esto FALLA en código sin corregir)
          return orphanCount === 0
        },
      ),
      { numRuns: 25 },
    )
  })

  it('GET 403 (ACCESS_DENIED): consumidor recibe ApiError, inflight limpio y 0 rejections huérfanas', async () => {
    tracker.start()

    const errorBody = { error: 'ACCESS_DENIED' }
    vi.stubGlobal('fetch', async () => makeResponse(403, errorBody))

    const endpoint = '/api/tenants/comprobantes/configuracion'

    let capturado: ApiError | null = null
    try {
      await api.get(endpoint)
    } catch (err) {
      capturado = err as ApiError
    }

    await flushPendingRejections()

    // El error original se propaga sin cambios al consumidor (misma forma ApiError)
    expect(capturado).not.toBeNull()
    expect(capturado?.status).toBe(403)
    expect(capturado?.details).toEqual(errorBody)

    // inflight limpio: un segundo GET vuelve a llamar a fetch (no hay promesa en vuelo residual)
    let fetchCalls = 0
    vi.stubGlobal('fetch', async () => {
      fetchCalls++
      return makeResponse(403, errorBody)
    })
    try {
      await api.get(endpoint)
    } catch {
      // se espera rechazo
    }
    await flushPendingRejections()
    expect(fetchCalls).toBe(1)

    // 0 rejections huérfanas (FALLA en código sin corregir)
    expect(tracker.getCount()).toBe(0)
  })

  it('GET 500: 0 rejections huérfanas y error propagado', async () => {
    tracker.start()

    vi.stubGlobal('fetch', async () => makeResponse(500, { error: 'INTERNAL' }))

    let capturado: ApiError | null = null
    try {
      await api.get('/api/tenants/reportes/ventas')
    } catch (err) {
      capturado = err as ApiError
    }

    await flushPendingRejections()

    expect(capturado?.status).toBe(500)
    // 0 rejections huérfanas (FALLA en código sin corregir)
    expect(tracker.getCount()).toBe(0)
  })

  it('GET con fallo de red: 0 rejections huérfanas y error propagado', async () => {
    tracker.start()

    const networkError = new TypeError('Failed to fetch')
    vi.stubGlobal('fetch', async () => {
      throw networkError
    })

    let capturado: unknown = null
    try {
      await api.get('/api/tenants/productos')
    } catch (err) {
      capturado = err
    }

    await flushPendingRejections()

    // El consumidor recibe el error de red original sin cambios
    expect(capturado).toBe(networkError)
    // 0 rejections huérfanas (FALLA en código sin corregir)
    expect(tracker.getCount()).toBe(0)
  })
})
