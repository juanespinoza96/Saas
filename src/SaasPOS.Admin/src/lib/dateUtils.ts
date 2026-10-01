/**
 * Módulo utilitario centralizado para manejo de zonas horarias y formateo de fechas.
 * Todas las páginas del sistema usan estas funciones para garantizar consistencia.
 */

/** Patrón IANA: al menos Región/Ciudad (permite sub-regiones como America/Indiana/Knox) */
const IANA_PATTERN = /^[A-Za-z]+(?:\/[A-Za-z_-]+)+$/

/** Longitud máxima permitida para el valor del header X-Timezone */
const MAX_TIMEZONE_LENGTH = 64

/** Valor fallback cuando la detección falla o el valor es inválido */
const FALLBACK_TIMEZONE = 'UTC'

/**
 * Detecta la zona horaria IANA del navegador del usuario.
 * - Usa Intl.DateTimeFormat().resolvedOptions().timeZone
 * - Valida formato Región/Ciudad con regex
 * - Limita a 64 caracteres
 * - Fallback a "UTC" si la detección falla
 */
export function detectTimezone(): string {
  try {
    const tz = Intl.DateTimeFormat().resolvedOptions().timeZone

    // Validar que no sea undefined, null o vacío
    if (!tz || typeof tz !== 'string' || tz.trim() === '') {
      return FALLBACK_TIMEZONE
    }

    // Validar longitud máxima
    if (tz.length > MAX_TIMEZONE_LENGTH) {
      return FALLBACK_TIMEZONE
    }

    // Validar patrón IANA Región/Ciudad
    if (!IANA_PATTERN.test(tz)) {
      return FALLBACK_TIMEZONE
    }

    return tz
  } catch {
    // Si Intl no está disponible o lanza excepción
    return FALLBACK_TIMEZONE
  }
}

/**
 * Formatea una fecha ISO 8601 al formato DD/MM/YYYY HH:mm (ZZZ).
 * - Si el input es null, undefined, vacío o no parseable: retorna "—" (guión largo)
 * - Usa Intl.DateTimeFormat con la zona horaria del navegador para el formateo
 */
export function formatDateTime(isoString: string | null | undefined): string {
  // Valores nulos o vacíos → guión largo
  if (isoString === null || isoString === undefined || isoString.trim() === '') {
    return '—'
  }

  // Intentar parsear la fecha
  const date = new Date(isoString)

  // Verificar que la fecha sea válida
  if (isNaN(date.getTime())) {
    return '—'
  }

  try {
    const timezone = detectTimezone()

    // Formatear fecha: DD/MM/YYYY HH:mm
    const dateFormatter = new Intl.DateTimeFormat('es-EC', {
      day: '2-digit',
      month: '2-digit',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
      timeZone: timezone,
    })

    // Obtener las partes formateadas
    const parts = dateFormatter.formatToParts(date)
    const day = parts.find(p => p.type === 'day')?.value ?? '00'
    const month = parts.find(p => p.type === 'month')?.value ?? '00'
    const year = parts.find(p => p.type === 'year')?.value ?? '0000'
    const hour = parts.find(p => p.type === 'hour')?.value ?? '00'
    const minute = parts.find(p => p.type === 'minute')?.value ?? '00'

    // Obtener abreviatura de zona horaria
    const tzFormatter = new Intl.DateTimeFormat('es-EC', {
      timeZoneName: 'short',
      timeZone: timezone,
    })
    const tzParts = tzFormatter.formatToParts(date)
    const tzAbbr = tzParts.find(p => p.type === 'timeZoneName')?.value ?? timezone

    return `${day}/${month}/${year} ${hour}:${minute} (${tzAbbr})`
  } catch {
    // Si el formateo falla por cualquier razón, retornar guión largo
    return '—'
  }
}

/**
 * Retorna el header X-Timezone para inyectar en cada request del ApiClient.
 * El valor es la zona horaria detectada del navegador.
 */
export function getTimezoneHeader(): Record<string, string> {
  return { 'X-Timezone': detectTimezone() }
}
