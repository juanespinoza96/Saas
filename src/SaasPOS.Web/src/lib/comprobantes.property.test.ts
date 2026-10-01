/**
 * Tests de propiedades (Property-Based Testing) para el selector de comprobantes.
 * Verifica que la función de filtrado devuelve exactamente los tipos habilitados,
 * sin tipos adicionales ni faltantes.
 *
 * Feature: mejoras-operativas-v2
 * Property 7: Selector de comprobante muestra exactamente los tipos habilitados
 * Validates: Requirements 6.5
 */

import fc from 'fast-check'
import { describe, it, expect } from 'vitest'
import {
  TIPOS_COMPROBANTE,
  obtenerTiposHabilitados,
  type ConfiguracionComprobanteDto,
} from './comprobantes'

/**
 * Generador de subconjuntos no vacíos de tipos de comprobante.
 * Genera un array de booleanos (uno por cada tipo) y garantiza que al menos uno sea true,
 * representando el estado habilitado/deshabilitado de cada tipo.
 */
const subconjuntoNoVacioHabilitados = fc
  .tuple(fc.boolean(), fc.boolean(), fc.boolean())
  .filter(([a, b, c]) => a || b || c) // Al menos uno habilitado (invariante Req 6.4)
  .map(([a, b, c]): ConfiguracionComprobanteDto[] => [
    { tipoComprobante: TIPOS_COMPROBANTE[0], habilitado: a },
    { tipoComprobante: TIPOS_COMPROBANTE[1], habilitado: b },
    { tipoComprobante: TIPOS_COMPROBANTE[2], habilitado: c },
  ])

describe('obtenerTiposHabilitados - Property-Based Tests', () => {
  /**
   * Propiedad principal: Para cualquier subconjunto no vacío S de tipos configurados
   * como habilitados, obtenerTiposHabilitados devuelve exactamente los elementos de S.
   * Validates: Requirements 6.5
   */
  it('devuelve exactamente los tipos marcados como habilitados, sin adicionales ni faltantes', () => {
    fc.assert(
      fc.property(subconjuntoNoVacioHabilitados, (configuracion) => {
        const resultado = obtenerTiposHabilitados(configuracion)

        // Calcular el conjunto esperado: tipos cuyo habilitado es true
        const esperados = configuracion
          .filter((c) => c.habilitado)
          .map((c) => c.tipoComprobante)

        // Verificar que el resultado contiene exactamente los esperados
        expect(resultado).toHaveLength(esperados.length)
        expect(resultado).toEqual(esperados)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad: El resultado nunca contiene tipos que no están habilitados.
   * Validates: Requirements 6.5
   */
  it('nunca incluye tipos deshabilitados en el resultado', () => {
    fc.assert(
      fc.property(subconjuntoNoVacioHabilitados, (configuracion) => {
        const resultado = obtenerTiposHabilitados(configuracion)

        const tiposDeshabilitados = configuracion
          .filter((c) => !c.habilitado)
          .map((c) => c.tipoComprobante)

        // Ningún tipo deshabilitado debe aparecer en el resultado
        for (const tipo of tiposDeshabilitados) {
          expect(resultado).not.toContain(tipo)
        }
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad: Todos los tipos habilitados aparecen en el resultado (completitud).
   * Validates: Requirements 6.5
   */
  it('todos los tipos habilitados están presentes en el resultado', () => {
    fc.assert(
      fc.property(subconjuntoNoVacioHabilitados, (configuracion) => {
        const resultado = obtenerTiposHabilitados(configuracion)

        const tiposHabilitados = configuracion
          .filter((c) => c.habilitado)
          .map((c) => c.tipoComprobante)

        // Cada tipo habilitado debe estar en el resultado
        for (const tipo of tiposHabilitados) {
          expect(resultado).toContain(tipo)
        }
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad: El resultado preserva el orden original de la configuración.
   * Validates: Requirements 6.5
   */
  it('preserva el orden relativo de los tipos según la configuración de entrada', () => {
    fc.assert(
      fc.property(subconjuntoNoVacioHabilitados, (configuracion) => {
        const resultado = obtenerTiposHabilitados(configuracion)

        // Verificar que los elementos del resultado mantienen su orden relativo
        // respecto a la configuración original
        let lastIndex = -1
        for (const tipo of resultado) {
          const currentIndex = configuracion.findIndex(
            (c) => c.tipoComprobante === tipo
          )
          expect(currentIndex).toBeGreaterThan(lastIndex)
          lastIndex = currentIndex
        }
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad: El resultado es un subconjunto de TIPOS_COMPROBANTE válidos.
   * Validates: Requirements 6.5
   */
  it('el resultado solo contiene tipos válidos del sistema', () => {
    fc.assert(
      fc.property(subconjuntoNoVacioHabilitados, (configuracion) => {
        const resultado = obtenerTiposHabilitados(configuracion)

        // Cada elemento del resultado debe pertenecer al conjunto de tipos válidos
        for (const tipo of resultado) {
          expect(TIPOS_COMPROBANTE as readonly string[]).toContain(tipo)
        }
      }),
      { numRuns: 100 }
    )
  })
})
