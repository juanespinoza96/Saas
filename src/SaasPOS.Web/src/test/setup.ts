import '@testing-library/jest-dom'
import 'vitest-axe/extend-expect'
import { afterEach, vi } from 'vitest'
import { cleanup } from '@testing-library/react'

// Mock localStorage for tests
const localStorageMock = (() => {
  let store: Record<string, string> = {}
  return {
    getItem: (key: string) => store[key] ?? null,
    setItem: (key: string, value: string) => {
      store[key] = value
    },
    removeItem: (key: string) => {
      delete store[key]
    },
    clear: () => {
      store = {}
    },
    get length() {
      return Object.keys(store).length
    },
    key: (index: number) => Object.keys(store)[index] ?? null,
  }
})()

Object.defineProperty(window, 'localStorage', { value: localStorageMock })

// Mock import.meta.env
Object.defineProperty(import.meta, 'env', {
  value: {
    VITE_API_BASE_URL: 'http://localhost:5000',
    MODE: 'test',
    DEV: true,
    PROD: false,
  },
})

// Liberar memoria entre tests: desmontar árboles renderizados (DOM de jsdom),
// limpiar temporizadores y restaurar/resetear mocks. Es idempotente y complementa
// los afterEach locales de cada archivo sin romperlos (llamar cleanup() dos veces
// es seguro). Ayuda a evitar la acumulación de nodos/timers/mocks entre casos.
afterEach(() => {
  cleanup()
  vi.clearAllTimers()
  vi.useRealTimers()
  // resetAllMocks() ADEMÁS de restoreAllMocks(): restore devuelve los spies a su
  // implementación original, pero NO libera las mockImplementation registradas con
  // vi.fn() (p. ej. el mock de api.get que captura productos/preciosMap por
  // iteración). reset() vacía esas implementaciones y suelta los closures retenidos,
  // que son la fuente principal de la fuga de memoria monótona entre iteraciones.
  vi.resetAllMocks()
  vi.restoreAllMocks()
  // Liberar los globals stubbeados (p. ej. vi.stubGlobal('print', ...)) para que
  // no se acumulen entre archivos que comparten el mismo proceso de worker.
  vi.unstubAllGlobals()
})
