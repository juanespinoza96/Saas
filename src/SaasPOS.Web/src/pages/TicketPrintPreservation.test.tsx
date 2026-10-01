/**
 * ARCHIVO MIGRADO — Pruebas de PRESERVACIÓN (Property 2) del ticket
 * Spec: ticket-digital-pdf-blanco (bugfix) · tarea 3.4 de tests-pos-memoria-oom
 *
 * Las tres propiedades de preservación que antes vivían en este archivo se
 * dividieron en archivos separados para resolver el OOM ("JavaScript heap out of
 * memory" cerca de ~4 GB) que ocurría al ejecutarlas juntas en un mismo worker.
 * Con `isolate: true` en vitest.config.ts, cada archivo corre en su propio proceso
 * y libera el heap al terminar, repartiendo el pico de memoria. Los generadores y
 * las aserciones se conservaron EXACTAMENTE; solo se separaron y se ajustaron
 * `numRuns`/timeouts y el ciclo de temporizadores por iteración.
 *
 *   (a) → TicketPrintPreservation.hidden.test.tsx   (ticket oculto en pantalla, Req 3.1)
 *   (b) → TicketPrintPreservation.payload.test.tsx  (payload de venta, Req 3.4)
 *   (c) → TicketPrintPreservation.print.test.tsx    (disparo de impresión, Req 3.2, 3.4)
 *
 * Este archivo se mantiene como marcador de la migración (la política del proyecto
 * no permite eliminar archivos de forma autónoma). No contiene pruebas activas.
 */
import { describe, it, expect } from 'vitest'

describe('TicketPrintPreservation (migrado a archivos hidden/payload/print)', () => {
  it('las propiedades de preservación se ejecutan en sus archivos separados', () => {
    // Marcador: la cobertura real está en los tres archivos .hidden/.payload/.print.
    expect(true).toBe(true)
  })
})
