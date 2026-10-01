import { useState, useEffect, useRef, useCallback } from 'react'

/** Clave de localStorage para almacenar el timestamp de fin de bloqueo */
const LOCKOUT_KEY = 'loginLockoutUntil'

interface UseLoginLockoutReturn {
  /** Indica si el formulario de login está bloqueado */
  isLocked: boolean
  /** Tiempo restante en formato "MM:SS" */
  remainingTime: string
  /** Activa el bloqueo por la cantidad de segundos indicada */
  setLockout: (seconds: number) => void
  /** Limpia el bloqueo manualmente (elimina localStorage y timer) */
  clearLockout: () => void
}

/**
 * Hook que gestiona el estado de bloqueo del formulario de login.
 * - Lee `loginLockoutUntil` de localStorage al montar (Req 9.4)
 * - Inicia countdown timer si hay bloqueo activo (Req 9.2)
 * - Limpia localStorage y timer cuando expira el bloqueo (Req 9.5)
 * - Maneja gracefully localStorage no disponible y valores inválidos
 */
export function useLoginLockout(): UseLoginLockoutReturn {
  const [remainingSeconds, setRemainingSeconds] = useState<number>(0)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  /**
   * Lee de forma segura el valor de localStorage.
   * Retorna null si localStorage no está disponible o el valor es inválido.
   */
  const leerLockoutDeStorage = useCallback((): Date | null => {
    try {
      const valor = localStorage.getItem(LOCKOUT_KEY)
      if (!valor) return null

      const fecha = new Date(valor)
      // Verificar que la fecha es válida (no NaN)
      if (isNaN(fecha.getTime())) {
        // Valor inválido: eliminar la clave y no mostrar lockout
        localStorage.removeItem(LOCKOUT_KEY)
        return null
      }

      return fecha
    } catch {
      // localStorage no disponible (modo incógnito, storage lleno, etc.)
      return null
    }
  }, [])

  /**
   * Escribe de forma segura en localStorage.
   * Si localStorage no está disponible, falla silenciosamente.
   */
  const guardarLockoutEnStorage = useCallback((fechaFin: Date): void => {
    try {
      localStorage.setItem(LOCKOUT_KEY, fechaFin.toISOString())
    } catch {
      // Graceful degradation: no persistir si localStorage no disponible
    }
  }, [])

  /**
   * Elimina de forma segura la clave de localStorage.
   */
  const eliminarLockoutDeStorage = useCallback((): void => {
    try {
      localStorage.removeItem(LOCKOUT_KEY)
    } catch {
      // Graceful degradation
    }
  }, [])

  /** Detiene el interval timer activo */
  const detenerTimer = useCallback((): void => {
    if (intervalRef.current !== null) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
    }
  }, [])

  /** Calcula los segundos restantes desde ahora hasta la fecha de fin */
  const calcularSegundosRestantes = useCallback((fechaFin: Date): number => {
    const diff = Math.ceil((fechaFin.getTime() - Date.now()) / 1000)
    return Math.max(0, diff)
  }, [])

  /** Inicia el countdown timer que decrementa cada segundo */
  const iniciarCountdown = useCallback(
    (fechaFin: Date): void => {
      detenerTimer()

      const actualizar = () => {
        const segundos = calcularSegundosRestantes(fechaFin)
        if (segundos <= 0) {
          // Bloqueo expirado: limpiar todo (Req 9.5)
          detenerTimer()
          setRemainingSeconds(0)
          eliminarLockoutDeStorage()
        } else {
          setRemainingSeconds(segundos)
        }
      }

      // Actualizar inmediatamente y luego cada segundo
      actualizar()
      intervalRef.current = setInterval(actualizar, 1000)
    },
    [detenerTimer, calcularSegundosRestantes, eliminarLockoutDeStorage],
  )

  /**
   * Activa el bloqueo por la cantidad de segundos indicada (Req 9.2, 9.3).
   * Persiste el timestamp de fin en localStorage.
   */
  const setLockout = useCallback(
    (seconds: number): void => {
      if (seconds <= 0) return

      const fechaFin = new Date(Date.now() + seconds * 1000)
      guardarLockoutEnStorage(fechaFin)
      iniciarCountdown(fechaFin)
    },
    [guardarLockoutEnStorage, iniciarCountdown],
  )

  /**
   * Limpia el bloqueo manualmente: elimina localStorage, detiene timer,
   * resetea estado.
   */
  const clearLockout = useCallback((): void => {
    detenerTimer()
    setRemainingSeconds(0)
    eliminarLockoutDeStorage()
  }, [detenerTimer, eliminarLockoutDeStorage])

  // Al montar: leer localStorage y restaurar bloqueo si existe (Req 9.4)
  useEffect(() => {
    const fechaFin = leerLockoutDeStorage()
    if (fechaFin) {
      const segundos = calcularSegundosRestantes(fechaFin)
      if (segundos > 0) {
        iniciarCountdown(fechaFin)
      } else {
        // El bloqueo ya expiró: limpiar localStorage
        eliminarLockoutDeStorage()
      }
    }

    // Limpiar timer al desmontar
    return () => {
      detenerTimer()
    }
  }, [leerLockoutDeStorage, calcularSegundosRestantes, iniciarCountdown, eliminarLockoutDeStorage, detenerTimer])

  /** Formatea los segundos restantes en formato "MM:SS" (Req 9.2) */
  const formatearTiempo = (totalSegundos: number): string => {
    const minutos = Math.floor(totalSegundos / 60)
    const segundos = totalSegundos % 60
    return `${String(minutos).padStart(2, '0')}:${String(segundos).padStart(2, '0')}`
  }

  return {
    isLocked: remainingSeconds > 0,
    remainingTime: formatearTiempo(remainingSeconds),
    setLockout,
    clearLockout,
  }
}
