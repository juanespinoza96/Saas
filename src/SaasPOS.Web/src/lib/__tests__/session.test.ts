// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Tests de unidad y PBT de UI de `computeWarningThresholds` (función pura de
// umbrales de aviso) y de las constantes de sesión del POS. Cubre la Tarea 9.1:
// escalado determinista de umbrales con la garantía de orden estricto.
//
// _Requirements: 5.2, 5.4, 5.6, 7.4_

import { describe, it, expect } from 'vitest'
import fc from 'fast-check'
import {
  computeWarningThresholds,
  InactivityMinutesByRole,
  REFRESH_RETRY_DELAY_MS,
} from '../session'

const MIN = 60_000

describe('computeWarningThresholds (ventanas normales W ≥ 15 min)', () => {
  it('fija 5min/60s/10s para una ventana de 60 min (Cajero)', () => {
    const { early, modal, block } = computeWarningThresholds(60 * MIN)
    expect(early).toBe(5 * MIN)
    expect(modal).toBe(60_000)
    expect(block).toBe(10_000)
  })

  it('fija 5min/60s/10s para una ventana de 30 min (Dueño/Gerente)', () => {
    const { early, modal, block } = computeWarningThresholds(30 * MIN)
    expect(early).toBe(5 * MIN)
    expect(modal).toBe(60_000)
    expect(block).toBe(10_000)
  })

  it('fija 5min/60s/10s para la ventana mínima "normal" de 15 min (SuperAdmin)', () => {
    const { early, modal, block } = computeWarningThresholds(15 * MIN)
    expect(early).toBe(5 * MIN)
    expect(modal).toBe(60_000)
    expect(block).toBe(10_000)
  })
})

describe('computeWarningThresholds (orden estricto early > modal > block > 0)', () => {
  it('mantiene el orden estricto en una ventana corta (p. ej. 2 min)', () => {
    const { early, modal, block } = computeWarningThresholds(2 * MIN)
    expect(early).toBeGreaterThan(modal)
    expect(modal).toBeGreaterThan(block)
    expect(block).toBeGreaterThan(0)
    // early nunca excede la ventana completa.
    expect(early).toBeLessThanOrEqual(2 * MIN)
  })

  it('acota early a [30s, 5min] para ventanas muy cortas', () => {
    // Ventana de 40 s: mitad = 20 s, acotada a 30 s como mínimo.
    const { early } = computeWarningThresholds(40_000)
    expect(early).toBe(30_000)
  })
})

describe('InactivityMinutesByRole (espejo del backend)', () => {
  it('coincide con los valores aprobados por rol (incluida la "ñ" de Dueño)', () => {
    expect(InactivityMinutesByRole.SuperAdmin).toBe(15)
    expect(InactivityMinutesByRole.Dueño).toBe(30)
    expect(InactivityMinutesByRole.Gerente).toBe(30)
    expect(InactivityMinutesByRole.Supervisor).toBe(60)
    expect(InactivityMinutesByRole.Bodeguero).toBe(60)
    expect(InactivityMinutesByRole.Cajero).toBe(60)
  })

  it('expone el retardo de reintento compartido de 3000 ms', () => {
    expect(REFRESH_RETRY_DELAY_MS).toBe(3000)
  })
})

// PBT de UI (opcional pero recomendado por la Tarea 9.1).
describe('PBT computeWarningThresholds', () => {
  // Property A: para toda ventana realista (W ≥ 1 min) se cumple
  // early > modal > block > 0 y early ≤ W. Se acota a W ≥ 1 min porque las
  // ventanas de la spec son ≥ 15 min y la garantía `early ≤ W` solo aplica a
  // ventanas donde el clamp mínimo de early (30 s) no supera la ventana; por
  // debajo de ~30 s la función prioriza mantener los umbrales visibles.
  it('Property A: orden estricto y early ≤ W para toda ventana realista (W ≥ 1 min)', () => {
    fc.assert(
      fc.property(
        // Ventanas desde 1 min hasta 2 h, en ms.
        fc.integer({ min: MIN, max: 2 * 60 * MIN }),
        (W) => {
          const { early, modal, block } = computeWarningThresholds(W)
          return (
            early > modal &&
            modal > block &&
            block > 0 &&
            early <= W
          )
        },
      ),
      { numRuns: 200 },
    )
  })

  // Property B: para toda ventana W ≥ 15 min los valores son fijos 5min/60s/10s.
  it('Property B: W ≥ 15 min ⇒ valores fijos 5min/60s/10s', () => {
    fc.assert(
      fc.property(
        fc.integer({ min: 15 * MIN, max: 6 * 60 * MIN }),
        (W) => {
          const { early, modal, block } = computeWarningThresholds(W)
          return early === 5 * MIN && modal === 60_000 && block === 10_000
        },
      ),
      { numRuns: 200 },
    )
  })
})
