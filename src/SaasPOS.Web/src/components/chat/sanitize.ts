/**
 * Utilidad de sanitización de HTML para respuestas de IA.
 * Elimina patrones de inyección peligrosos (XSS) preservando texto plano legítimo.
 * Requisitos: 7.10, Propiedad 12 del diseño.
 */

/**
 * Patrones de etiquetas HTML peligrosas que deben eliminarse completamente.
 * Incluye el contenido entre las etiquetas de apertura y cierre.
 */
const DANGEROUS_TAGS_REGEX = /<\s*(script|iframe|object|embed|link|meta|style)\b[^>]*>[\s\S]*?<\s*\/\s*\1\s*>|<\s*(script|iframe|object|embed|link|meta|style)\b[^>]*\/?>/gi

/**
 * Patrón para detectar manejadores de eventos inline (onclick, onerror, onload, etc.)
 */
const EVENT_HANDLERS_REGEX = /\s*on\w+\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>]*)/gi

/**
 * Patrón para detectar URLs con protocolo javascript:
 */
const JAVASCRIPT_URL_REGEX = /javascript\s*:/gi

/**
 * Patrón para detectar atributos href/src con javascript:
 */
const DANGEROUS_ATTRS_REGEX = /(href|src|action)\s*=\s*(?:"[^"]*javascript\s*:[^"]*"|'[^']*javascript\s*:[^']*')/gi

/**
 * Sanitiza una cadena de texto eliminando patrones peligrosos de inyección HTML/JS.
 * - Elimina etiquetas <script>, <iframe>, <object>, <embed>, <link>, <meta>, <style>
 * - Elimina manejadores de eventos inline (onclick=, onerror=, onload=, onmouseover=, etc.)
 * - Elimina URLs con protocolo javascript:
 * - Preserva texto plano legítimo sin alteraciones
 *
 * @param input - La cadena de texto a sanitizar
 * @returns La cadena sanitizada libre de patrones peligrosos
 */
export function sanitizeHtml(input: string): string {
  if (!input) return ''

  let result = input

  // Eliminar etiquetas peligrosas y su contenido
  result = result.replace(DANGEROUS_TAGS_REGEX, '')

  // Eliminar atributos href/src con javascript: (antes de limpiar eventos)
  result = result.replace(DANGEROUS_ATTRS_REGEX, '')

  // Eliminar manejadores de eventos inline
  result = result.replace(EVENT_HANDLERS_REGEX, '')

  // Eliminar URLs javascript: restantes
  result = result.replace(JAVASCRIPT_URL_REGEX, '')

  return result
}
