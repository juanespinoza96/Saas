/**
 * Tests de propiedades (Property-Based Testing) para formateo de moneda.
 * Verifica que el formateo cumple el patrón "$X.XX por mes" para cualquier
 * valor de cuota positivo con hasta 2 decimales.
 *
 * Feature: mejoras-operativas-v2, Property 2: Formateo de moneda cumple patrón "$X.XX por mes"
 * Validates: Requirements 2.2
 */

import fc from 'fast-check'
import { describe, it, expect } from 'vitest'
import { formatearMoneda, formatearCuotaMensual } from './InstallmentSummary'

// Generador de valores de cuota positivos con hasta 2 decimales
// Simula valores reales de cuotas (desde $0.01 hasta un máximo razonable)
const valorCuotaPositivo = fc
  .integer({ min: 1, max: 99999999 })
  .map(n => n / 100) // Produce valores con exactamente 0-2 decimales

describe('Formateo de moneda - Property-Based Tests', () => {
  /**
   * Propiedad: formatearMoneda siempre produce un string con patrón "$X.XX"
   * Validates: Requirements 2.2
   */
  it('formatearMoneda siempre produce un string con patrón "$X.XX"', () => {
    fc.assert(
      fc.property(valorCuotaPositivo, (valor) => {
        const resultado = formatearMoneda(valor)
        // El resultado debe coincidir con el patrón $<dígitos>.<2 dígitos>
        expect(resultado).toMatch(/^\$\d+\.\d{2}$/)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad: formatearCuotaMensual siempre produce un string con patrón "$X.XX por mes"
   * Validates: Requirements 2.2
   */
  it('formatearCuotaMensual siempre produce un string con patrón "$X.XX por mes"', () => {
    fc.assert(
      fc.property(valorCuotaPositivo, (valor) => {
        const resultado = formatearCuotaMensual(valor)
        // El resultado debe coincidir con el patrón $<dígitos>.<2 dígitos> por mes
        expect(resultado).toMatch(/^\$\d+\.\d{2} por mes$/)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad: formatearCuotaMensual es consistente con formatearMoneda + " por mes"
   * Validates: Requirements 2.2
   */
  it('formatearCuotaMensual es consistente con formatearMoneda + " por mes"', () => {
    fc.assert(
      fc.property(valorCuotaPositivo, (valor) => {
        const moneda = formatearMoneda(valor)
        const cuotaMensual = formatearCuotaMensual(valor)
        expect(cuotaMensual).toBe(`${moneda} por mes`)
      }),
      { numRuns: 100 }
    )
  })
})
