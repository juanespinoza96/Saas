/**
 * Tests de propiedades (Property-Based Testing) para sanitizeHtml.
 * Verifica que la función de sanitización elimina patrones de inyección HTML/JS
 * preservando texto plano legítimo para cualquier cadena de entrada.
 *
 * Feature: mejoras-operativas-v2
 * Property 12: Sanitización elimina patrones de inyección HTML/JS
 * Validates: Requirements 7.10
 */

import fc from 'fast-check'
import { describe, it, expect } from 'vitest'
import { sanitizeHtml } from './sanitize'

// Generador de texto plano sin caracteres HTML ni patrones de eventos
const textoPlano = fc.string().filter(s =>
  !s.includes('<') &&
  !s.includes('>') &&
  !/on\w+=/i.test(s) &&
  !/javascript\s*:/i.test(s)
)

// Generador de contenido arbitrario para inyectar dentro de etiquetas
const contenidoArbitrario = fc.string({ minLength: 0, maxLength: 50 })

// Generador de prefijos y sufijos seguros
const textoSeguro = fc.string({ minLength: 0, maxLength: 30 }).filter(s =>
  !s.includes('<') && !s.includes('>')
)

// Generador de nombres de eventos inline
const eventosInline = fc.constantFrom(
  'onclick', 'onerror', 'onload', 'onmouseover',
  'onfocus', 'onblur', 'onsubmit', 'onchange'
)

describe('sanitizeHtml - Property-Based Tests', () => {
  /**
   * Propiedad 1: Texto plano se preserva sin alteraciones.
   * Para cualquier cadena sin etiquetas HTML, la sanitización no modifica el contenido.
   * Validates: Requirements 7.10
   */
  it('texto plano se preserva sin alteraciones', () => {
    fc.assert(
      fc.property(textoPlano, (input) => {
        expect(sanitizeHtml(input)).toBe(input)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 2: Etiquetas <script> son eliminadas del resultado.
   * Para cualquier cadena envuelta en <script>...</script>, el resultado no contiene <script.
   * Validates: Requirements 7.10
   */
  it('etiquetas <script> son eliminadas del resultado', () => {
    fc.assert(
      fc.property(textoSeguro, contenidoArbitrario, textoSeguro, (prefix, content, suffix) => {
        const input = `${prefix}<script>${content}</script>${suffix}`
        const result = sanitizeHtml(input)
        expect(result.toLowerCase()).not.toContain('<script')
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 3: Manejadores de eventos inline son eliminados.
   * Para cualquier etiqueta HTML con on{event}="...", el resultado no contiene ese patrón.
   * Validates: Requirements 7.10
   */
  it('manejadores de eventos inline son eliminados', () => {
    fc.assert(
      fc.property(eventosInline, contenidoArbitrario, (evento, contenido) => {
        const input = `<div ${evento}="alert('${contenido}')">texto</div>`
        const result = sanitizeHtml(input)
        // Verificar que no quede el patrón on{event}= en el resultado
        const regex = new RegExp(`${evento}\\s*=`, 'i')
        expect(regex.test(result)).toBe(false)
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 4: URLs con protocolo javascript: son eliminadas.
   * Para cualquier cadena que contenga javascript:, el resultado no lo contiene.
   * Validates: Requirements 7.10
   */
  it('URLs con protocolo javascript: son eliminadas', () => {
    fc.assert(
      fc.property(textoSeguro, contenidoArbitrario, textoSeguro, (prefix, payload, suffix) => {
        const input = `${prefix}javascript:${payload}${suffix}`
        const result = sanitizeHtml(input)
        expect(result.toLowerCase()).not.toContain('javascript:')
      }),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 5: Etiquetas iframe, object y embed son eliminadas.
   * Para cualquier cadena con <iframe>, <object> o <embed>, el resultado no las contiene.
   * Validates: Requirements 7.10
   */
  it('etiquetas iframe, object y embed son eliminadas', () => {
    const etiquetasPeligrosas = fc.constantFrom('iframe', 'object', 'embed')

    fc.assert(
      fc.property(
        etiquetasPeligrosas,
        textoSeguro,
        contenidoArbitrario,
        textoSeguro,
        (tag, prefix, content, suffix) => {
          const input = `${prefix}<${tag}>${content}</${tag}>${suffix}`
          const result = sanitizeHtml(input)
          expect(result.toLowerCase()).not.toContain(`<${tag}`)
        }
      ),
      { numRuns: 100 }
    )
  })

  /**
   * Propiedad 6: La sanitización es idempotente.
   * Para cualquier entrada, aplicar sanitizeHtml dos veces produce el mismo resultado que una vez.
   * Validates: Requirements 7.10
   */
  it('la sanitización es idempotente', () => {
    // Generador que incluye patrones peligrosos mezclados con texto
    const inputConPatrones = fc.oneof(
      textoPlano,
      fc.tuple(textoSeguro, contenidoArbitrario, textoSeguro).map(
        ([p, c, s]) => `${p}<script>${c}</script>${s}`
      ),
      fc.tuple(textoSeguro, contenidoArbitrario).map(
        ([p, c]) => `${p}javascript:${c}`
      ),
      fc.tuple(eventosInline, contenidoArbitrario).map(
        ([ev, c]) => `<div ${ev}="${c}">text</div>`
      ),
      fc.string()
    )

    fc.assert(
      fc.property(inputConPatrones, (input) => {
        const primeraVez = sanitizeHtml(input)
        const segundaVez = sanitizeHtml(primeraVez)
        expect(segundaVez).toBe(primeraVez)
      }),
      { numRuns: 100 }
    )
  })
})
