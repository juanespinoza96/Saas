/**
 * Pruebas del espejo en cliente de la Politica_Password estricta
 * (`src/lib/passwordPolicy.ts`), tarea 23.1 del spec
 * `recuperacion-password-jerarquica`.
 *
 * Cubre el Requirement 11.6 (aceptación con lista blanca estricta) mediante:
 * - Una tabla de casos representativos (válidos e inválidos por cada regla).
 * - Generación ligera con fast-check que construye contraseñas válidas por
 *   construcción y verifica que `validatePassword` las acepta, y contraseñas
 *   con un carácter fuera de la lista blanca que deben rechazarse.
 *
 * El backend (`PasswordPolicy.cs`) es la autoridad final; este espejo debe
 * comportarse de forma idéntica.
 */
import { describe, it, expect } from 'vitest'
import fc from 'fast-check'
import { validatePassword, MIN_LENGTH, SPECIAL_CHARS } from './passwordPolicy'

// ─── Conjuntos de caracteres de la lista blanca [A-Za-z0-9@-_] ────────────────
const MAYUSCULAS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
const MINUSCULAS = 'abcdefghijklmnopqrstuvwxyz'
const DIGITOS = '0123456789'
// SPECIAL_CHARS = '@-_' (importado del módulo bajo prueba).

describe('validatePassword — política estricta en cliente (Req 11.6)', () => {
  // ─── Tabla de casos: contraseñas VÁLIDAS ────────────────────────────────────
  describe('acepta contraseñas que cumplen todas las reglas', () => {
    const casosValidos: Array<{ password: string; descripcion: string }> = [
      { password: 'Abc1@x', descripcion: 'longitud mínima exacta (6) con las 4 clases' },
      { password: 'Passw0rd@', descripcion: 'especial @' },
      { password: 'Passw0rd-', descripcion: 'especial -' },
      { password: 'Passw0rd_', descripcion: 'especial _' },
      { password: 'A1@aaa', descripcion: 'mezcla mínima válida' },
      { password: 'ZZZZ9_zzzz', descripcion: 'sin espacios, solo lista blanca' },
      { password: 'Temp0ral@2024', descripcion: 'similar a una contraseña temporal generada' },
    ]

    it.each(casosValidos)('acepta "$password" ($descripcion)', ({ password }) => {
      const resultado = validatePassword(password)
      expect(resultado.isValid).toBe(true)
      expect(resultado.errors).toHaveLength(0)
      expect(resultado.firstError).toBe('')
    })
  })

  // ─── Tabla de casos: contraseñas INVÁLIDAS por cada regla ───────────────────
  describe('rechaza contraseñas que incumplen alguna regla', () => {
    const casosInvalidos: Array<{ password: string; descripcion: string }> = [
      { password: '', descripcion: 'vacía (longitud)' },
      { password: 'A1@a', descripcion: 'demasiado corta (< 6)' },
      { password: 'abc1@xyz', descripcion: 'sin mayúscula' },
      { password: 'Abcd@xyz', descripcion: 'sin dígito' },
      { password: 'Abcd1xyz', descripcion: 'sin carácter especial permitido' },
      { password: 'Abc1@ xyz', descripcion: 'contiene espacio (fuera de la lista blanca)' },
      { password: 'Abc1@#xyz', descripcion: 'contiene # (fuera de la lista blanca)' },
      { password: 'Abc1@áxyz', descripcion: 'contiene acento (fuera de la lista blanca)' },
      { password: 'Abc1@!xyz', descripcion: 'contiene ! (fuera de la lista blanca)' },
    ]

    it.each(casosInvalidos)('rechaza "$password" ($descripcion)', ({ password }) => {
      const resultado = validatePassword(password)
      expect(resultado.isValid).toBe(false)
      expect(resultado.errors.length).toBeGreaterThan(0)
      // firstError es una conveniencia para la UI: debe reflejar el primer error.
      expect(resultado.firstError).toBe(resultado.errors[0])
    })
  })

  // ─── Generación ligera (fast-check): contraseñas válidas por construcción ───
  it('acepta cualquier contraseña construida con las 4 clases y solo lista blanca', () => {
    // Alfabeto completo de la lista blanca para el "relleno".
    const listaBlanca = MAYUSCULAS + MINUSCULAS + DIGITOS + SPECIAL_CHARS

    fc.assert(
      fc.property(
        fc.constantFrom(...MAYUSCULAS.split('')), // al menos una mayúscula (Req 11.2)
        fc.constantFrom(...DIGITOS.split('')), // al menos un dígito (Req 11.3)
        fc.constantFrom(...SPECIAL_CHARS.split('')), // al menos un especial permitido (Req 11.4)
        // Relleno de longitud variable con caracteres de la lista blanca.
        fc.array(fc.constantFrom(...listaBlanca.split('')), { minLength: 3, maxLength: 20 }),
        // Semilla para barajar de forma determinista los caracteres obligatorios.
        fc.nat(),
        (may, dig, esp, relleno, semilla) => {
          // Construir la contraseña garantizando longitud >= MIN_LENGTH: 3
          // caracteres obligatorios + relleno de al menos 3 => mínimo 6.
          const chars = [may, dig, esp, ...relleno]
          // Barajado ligero determinista (rotación por la semilla) para que la
          // posición de los caracteres obligatorios varíe entre iteraciones.
          const offset = semilla % chars.length
          const barajada = [...chars.slice(offset), ...chars.slice(0, offset)]
          const password = barajada.join('')

          const resultado = validatePassword(password)
          expect(resultado.isValid).toBe(true)
          expect(resultado.errors).toHaveLength(0)
        },
      ),
      { numRuns: 200 },
    )
  })

  // ─── Generación ligera (fast-check): un carácter fuera de la lista blanca ───
  it('rechaza contraseñas por lo demás válidas que incluyen un carácter fuera de [A-Za-z0-9@-_]', () => {
    // Caracteres representativos fuera de la lista blanca estricta.
    const fueraDeListaBlanca = ' !#$%^&*()+=[]{}|;:,.<>?/áéíñ'

    fc.assert(
      fc.property(
        fc.constantFrom(...fueraDeListaBlanca.split('')),
        // Posición donde insertar el carácter prohibido dentro de una base válida.
        fc.nat(),
        (prohibido, pos) => {
          // Base que cumple todas las reglas: mayúscula, dígito, especial y longitud.
          const base = 'Abc1@xyz'
          const indice = pos % (base.length + 1)
          const password = base.slice(0, indice) + prohibido + base.slice(indice)

          const resultado = validatePassword(password)
          // La presencia de un carácter fuera de la lista blanca siempre invalida.
          expect(resultado.isValid).toBe(false)
          expect(
            resultado.errors.some(e => e.includes('solo puede contener')),
          ).toBe(true)
        },
      ),
      { numRuns: 200 },
    )
  })

  // ─── Frontera de longitud (Req 11.1) ────────────────────────────────────────
  it('rechaza por longitud las contraseñas más cortas que el mínimo aunque tengan las demás clases', () => {
    fc.assert(
      fc.property(
        // Longitudes estrictamente menores que MIN_LENGTH.
        fc.integer({ min: 1, max: MIN_LENGTH - 1 }),
        (longitud) => {
          // Base con las 4 clases y luego recortada por debajo del mínimo.
          const password = 'A1@aaa'.slice(0, longitud)
          const resultado = validatePassword(password)
          expect(resultado.isValid).toBe(false)
          expect(
            resultado.errors.some(e => e.includes(`al menos ${MIN_LENGTH} caracteres`)),
          ).toBe(true)
        },
      ),
      { numRuns: 50 },
    )
  })
})
