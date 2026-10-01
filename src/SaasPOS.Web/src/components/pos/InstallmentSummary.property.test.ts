/**
 * Tests de propiedades (Property-Based Testing) para calcularValorCuota.
 * Verifica que el cálculo de cuotas cumple invariantes matemáticas
 * para cualquier combinación válida de total y número de cuotas.
 *
 * Feature: mejoras-operativas-v2
 * Property 1: Cálculo de cuota es Total / CuotasMeses redondeado a 2 decimales
 * Validates: Requirements 1.5
 */

import fc from 'fast-check'
import { describe, it, expect } from 'vitest'
import { calcularValorCuota } from './InstallmentSummary'

// Generador de cuotas válidas (opciones de diferido)
const cuotasValidas = fc.constantFrom(3, 6, 9, 12, 18)

// Generador de totales positivos (rango realista de ventas)
// Usamos Math.fround para cumplir con la restricción de fast-check de 32-bit floats
// El mínimo de 0.19 garantiza que total / 18 (cuota máxima) >= 0.01 tras redondeo
const totalPositivo = fc.float({ min: Math.fround(0.19), max: Math.fround(999999), noNaN: true })
  .filter(n => n >= 0.19 && isFinite(n))

describe('calcularValorCuota - Property-Based Tests', () => {
  /**
   * Propiedad 1: El resultado siempre es igual a Math.round((total / cuotas) * 100) / 100
   * Validates: Requirements 1.5
   */
  it('el resultado siempre es Math.round((total / cuotas) * 100) / 100', () => {
    fc.assert(
      fc.property(totalPositivo, cuotasValidas, (total, cuotas) => {
        const resultado = calcularValorCuota(total, cuotas)
        const esperado = Math.round((total / cuotas) * 100) / 100
        expect(resultado).toBe(esperado)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 2: El resultado siempre tiene como máximo 2 decimales
   * Validates: Requirements 1.5
   */
  it('el resultado siempre tiene como máximo 2 decimales', () => {
    fc.assert(
      fc.property(totalPositivo, cuotasValidas, (total, cuotas) => {
        const resultado = calcularValorCuota(total, cuotas)
        // Multiplicar por 100 y verificar que es un entero (tolerancia de punto flotante IEEE 754)
        const multiplicado = resultado * 100
        expect(Math.abs(multiplicado - Math.round(multiplicado))).toBeLessThan(1e-6)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 3: resultado * cuotas está dentro de tolerancia 0.01 * cuotas respecto al total
   * (cota de error de redondeo)
   * Validates: Requirements 1.5
   */
  it('resultado * cuotas está dentro de tolerancia de redondeo respecto al total', () => {
    fc.assert(
      fc.property(totalPositivo, cuotasValidas, (total, cuotas) => {
        const resultado = calcularValorCuota(total, cuotas)
        const totalReconstruido = resultado * cuotas
        const tolerancia = 0.01 * cuotas
        expect(Math.abs(totalReconstruido - total)).toBeLessThanOrEqual(tolerancia)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 4: El resultado siempre es > 0 para total positivo y cuotas válidas
   * Validates: Requirements 1.5
   */
  it('el resultado siempre es mayor a 0 para total positivo y cuotas válidas', () => {
    fc.assert(
      fc.property(totalPositivo, cuotasValidas, (total, cuotas) => {
        const resultado = calcularValorCuota(total, cuotas)
        expect(resultado).toBeGreaterThan(0)
      }),
      { numRuns: 100 }
    )
  })
})
