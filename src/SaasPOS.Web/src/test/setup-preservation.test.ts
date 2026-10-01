/**
 * Tests de preservación (Property 2) para el bugfix del OOM de la suite de tests del POS.
 *
 * Este archivo es DELIBERADAMENTE LIGERO: NO monta componentes pesados del POS
 * (PosNormalPage, PosBarPage, TicketPrint, etc.), por lo que NO agota memoria ni
 * provoca "Worker exited unexpectedly" / "JavaScript heap out of memory". Su único
 * fin es fijar el baseline de las utilidades de test compartidas y documentar la
 * premisa de idempotencia del `afterEach` global que se introducirá en la tarea 3.2
 * (SIN añadir aquí ese `afterEach` global — eso pertenece a `src/test/setup.ts`).
 *
 * Validates: Requirements 3.4
 *
 * IMPORTANTE: Estos tests DEBEN PASAR sobre el código SIN corregir (baseline) y
 * seguir pasando después del fix.
 */
import { describe, it, expect, vi } from 'vitest'
import { cleanup, render } from '@testing-library/react'
import React from 'react'

describe('Preservación de utilidades de test compartidas (setup.ts) — Req 3.4', () => {
  // ─── Mock de localStorage (definido en src/test/setup.ts) ─────────────────────
  describe('mock de localStorage', () => {
    it('hace round-trip de setItem/getItem', () => {
      // Escribir y leer un valor debe devolver exactamente lo escrito.
      window.localStorage.setItem('clavePrueba', 'valorPrueba')
      expect(window.localStorage.getItem('clavePrueba')).toBe('valorPrueba')

      // Limpieza local para no filtrar estado al resto de tests de este archivo.
      window.localStorage.clear()
    })

    it('devuelve null para claves inexistentes', () => {
      // Una clave que no existe debe devolver null (comportamiento actual).
      expect(window.localStorage.getItem('claveQueNoExiste')).toBeNull()
    })

    it('removeItem elimina solo la clave indicada', () => {
      // Preparar dos claves y eliminar únicamente una.
      window.localStorage.setItem('a', '1')
      window.localStorage.setItem('b', '2')

      window.localStorage.removeItem('a')

      // La clave eliminada devuelve null; la otra se conserva intacta.
      expect(window.localStorage.getItem('a')).toBeNull()
      expect(window.localStorage.getItem('b')).toBe('2')

      window.localStorage.clear()
    })

    it('clear vacía por completo el almacenamiento', () => {
      // Tras escribir varias claves, clear debe dejar length en 0.
      window.localStorage.setItem('x', 'valorX')
      window.localStorage.setItem('y', 'valorY')

      window.localStorage.clear()

      expect(window.localStorage.length).toBe(0)
      expect(window.localStorage.getItem('x')).toBeNull()
      expect(window.localStorage.getItem('y')).toBeNull()
    })

    it('length y key reflejan el contenido actual', () => {
      // Baseline de las propiedades length/key del mock.
      window.localStorage.clear()
      window.localStorage.setItem('unica', 'valor')

      expect(window.localStorage.length).toBe(1)
      expect(window.localStorage.key(0)).toBe('unica')
      // Un índice fuera de rango devuelve null (comportamiento actual del mock).
      expect(window.localStorage.key(5)).toBeNull()

      window.localStorage.clear()
    })
  })

  // ─── Mock de import.meta.env (definido en src/test/setup.ts) ──────────────────
  describe('mock de import.meta.env', () => {
    it('expone el baseline observado de import.meta.env en el entorno de test', () => {
      // NOTA sobre el baseline observado: aunque src/test/setup.ts hace
      //   Object.defineProperty(import.meta, 'env', { value: { VITE_API_BASE_URL: 'http://localhost:5000', ... } })
      // Vite/Vitest reemplaza estáticamente las variables `VITE_*` en tiempo de
      // transform, por lo que `import.meta.env.VITE_API_BASE_URL` se resuelve como
      // cadena vacía en el entorno de test (no hay .env con ese valor cargado).
      // Este test fija ESE baseline observado: el fix del OOM NO debe alterarlo (Req 3.4).
      expect(import.meta.env.VITE_API_BASE_URL).toBe('')

      // MODE / DEV / PROD sí reflejan el entorno de test de Vitest y deben
      // permanecer inalterados tras el fix.
      expect(import.meta.env.MODE).toBe('test')
      expect(import.meta.env.DEV).toBe(true)
      expect(import.meta.env.PROD).toBe(false)
    })
  })
})

describe('Premisa de idempotencia del afterEach global a introducir en 3.2 — Req 3.4', () => {
  it('llamar cleanup() dos veces seguidas no lanza (sin nada montado)', () => {
    // El afterEach global de la tarea 3.2 llamará cleanup() aunque un afterEach
    // local ya lo haya llamado. Este test documenta que esa doble llamada es segura.
    expect(() => {
      cleanup()
      cleanup()
    }).not.toThrow()
  })

  it('cleanup() es seguro tras un render trivial y puede repetirse', () => {
    // Montaje trivial (un solo nodo) para simular el caso real: un test que
    // renderiza algo y luego el afterEach global desmonta.
    render(React.createElement('div', null, 'ok'))

    // Primera limpieza (equivalente a un afterEach local) y segunda limpieza
    // (equivalente al afterEach global de 3.2): ninguna debe lanzar.
    expect(() => {
      cleanup()
      cleanup()
    }).not.toThrow()
  })

  it('vi.clearAllTimers/useRealTimers/restoreAllMocks pueden llamarse con seguridad', () => {
    // El afterEach global de 3.2 combinará estas llamadas. Aquí se verifica que
    // encadenarlas es idempotente y no rompe hooks locales existentes.
    expect(() => {
      vi.clearAllTimers()
      vi.useRealTimers()
      vi.restoreAllMocks()
    }).not.toThrow()

    // Repetir la secuencia también debe ser seguro (idempotencia).
    expect(() => {
      vi.clearAllTimers()
      vi.useRealTimers()
      vi.restoreAllMocks()
    }).not.toThrow()
  })
})
