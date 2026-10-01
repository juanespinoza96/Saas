/**
 * Espejo en cliente de la Politica_Password estricta (Requirement 11).
 *
 * El backend (`SaasPOS.Application/Helpers/PasswordPolicy.cs`) es la ÚNICA fuente
 * de verdad y la autoridad final. Este módulo replica exactamente sus reglas y
 * mensajes para ofrecer validación inmediata en el cliente, evitando viajes
 * innecesarios al servidor. Cualquier cambio en la política del backend debe
 * reflejarse aquí.
 *
 * Reglas de la política estricta:
 * - Longitud mínima de 6 caracteres (Req 11.1).
 * - Al menos una letra mayúscula (Req 11.2).
 * - Al menos un dígito (Req 11.3).
 * - Al menos un carácter especial de la lista blanca {@, -, _} (Req 11.4).
 * - NINGÚN carácter fuera de [A-Za-z0-9@-_] (lista blanca estricta, Req 11.5).
 * - Si cumple todo lo anterior, se acepta (Req 11.6).
 */

/** Longitud mínima exigida a la contraseña. */
export const MIN_LENGTH = 6

/** Conjunto exacto de caracteres especiales permitidos por la lista blanca estricta. */
export const SPECIAL_CHARS = '@-_'

/**
 * Resultado de la validación de una contraseña contra la política estricta.
 * Refleja la estructura de `PasswordValidationResult` del backend.
 */
export interface PasswordValidationResult {
  /** Indica si la contraseña cumple todas las reglas de la política. */
  isValid: boolean
  /** Mensajes descriptivos de las reglas incumplidas (vacío cuando isValid es verdadero). */
  errors: string[]
  /** Primer mensaje de error, o cadena vacía si la contraseña es válida (conveniencia para la UI). */
  firstError: string
}

/**
 * Valida una contraseña contra la política estricta y devuelve un resultado con
 * éxito/fallo y un mensaje descriptivo por cada regla incumplida.
 *
 * Replica el comportamiento de `PasswordPolicy.Validate` del backend, incluyendo
 * el orden de las reglas y el texto exacto de los mensajes de error.
 *
 * @param password - La contraseña a validar.
 * @returns Un PasswordValidationResult con el detalle de la validación.
 */
export function validatePassword(password: string): PasswordValidationResult {
  const errors: string[] = []

  // Una contraseña nula/indefinida o vacía se trata como violación de longitud.
  if (!password) {
    errors.push(`La contraseña debe tener al menos ${MIN_LENGTH} caracteres.`)
    return buildResult(errors)
  }

  // Regla 1 (Requirement 11.1): longitud mínima.
  if (password.length < MIN_LENGTH) {
    errors.push(`La contraseña debe tener al menos ${MIN_LENGTH} caracteres.`)
  }

  let tieneMayuscula = false
  let tieneDigito = false
  let tieneEspecialPermitido = false
  let tieneCaracterNoPermitido = false

  // Recorrido único de los caracteres para evaluar todas las reglas de composición.
  for (const c of password) {
    if (c >= 'A' && c <= 'Z') {
      tieneMayuscula = true
    } else if (c >= 'a' && c <= 'z') {
      // Letra minúscula: permitida por la lista blanca, sin regla propia.
    } else if (c >= '0' && c <= '9') {
      tieneDigito = true
    } else if (SPECIAL_CHARS.indexOf(c) >= 0) {
      tieneEspecialPermitido = true
    } else {
      // Cualquier otro carácter viola la lista blanca estricta.
      tieneCaracterNoPermitido = true
    }
  }

  // Regla 2 (Requirement 11.2): al menos una letra mayúscula.
  if (!tieneMayuscula) {
    errors.push('La contraseña debe contener al menos una letra mayúscula.')
  }

  // Regla 3 (Requirement 11.3): al menos un dígito.
  if (!tieneDigito) {
    errors.push('La contraseña debe contener al menos un número.')
  }

  // Regla 4 (Requirement 11.4): al menos un carácter especial de {@, -, _}.
  if (!tieneEspecialPermitido) {
    errors.push('La contraseña debe contener al menos uno de los caracteres especiales permitidos: @, - o _.')
  }

  // Regla 5 (Requirement 11.5): lista blanca estricta, sin caracteres fuera de [A-Za-z0-9@-_].
  if (tieneCaracterNoPermitido) {
    errors.push('La contraseña solo puede contener letras, números y los caracteres especiales @, - o _.')
  }

  // Regla 6 (Requirement 11.6): si no hay errores, la contraseña es aceptada.
  return buildResult(errors)
}

/**
 * Construye el resultado de validación a partir de la lista de errores acumulados.
 * Un arreglo vacío implica una contraseña válida.
 */
function buildResult(errors: string[]): PasswordValidationResult {
  return {
    isValid: errors.length === 0,
    errors,
    firstError: errors[0] ?? '',
  }
}
