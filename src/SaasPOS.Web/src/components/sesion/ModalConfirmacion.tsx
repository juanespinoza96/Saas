// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Modal de confirmación (etapa `modal` del reloj único): diálogo centrado que
// avisa, con una cuenta regresiva, de que la sesión está a punto de cerrarse e
// invita a seguir conectado (Req 5.3, 5.4, 6.2). A diferencia del aviso temprano,
// este SÍ roba el foco (focus trap) con foco inicial en "Seguir conectado", usa
// `role="alertdialog"` y `aria-live="polite"`. Pulsar "Seguir conectado" fuerza
// una renovación inmediata que reinicia el reloj y descarta el modal.

import { useEffect, useRef } from 'react'
import { Button } from '../ui/Button'

interface ModalConfirmacionProps {
  /** Milisegundos restantes hasta el cierre de sesión. */
  remainingMs: number
  /** Renueva la sesión de inmediato (botón "Seguir conectado"). */
  onSeguirConectado: () => void
}

/**
 * Formatea los ms restantes como segundos enteros (la ventana del modal es corta,
 * típicamente 60→0), redondeando hacia arriba para no mostrar 0 antes de tiempo.
 */
function formatearSegundos(ms: number): number {
  return Math.max(0, Math.ceil(ms / 1000))
}

export function ModalConfirmacion({
  remainingMs,
  onSeguirConectado,
}: ModalConfirmacionProps) {
  const botonRef = useRef<HTMLButtonElement>(null)
  const contenedorRef = useRef<HTMLDivElement>(null)

  // Foco inicial en el botón "Seguir conectado" al montar el modal.
  useEffect(() => {
    botonRef.current?.focus()
  }, [])

  // Focus trap: mantener el foco dentro del diálogo mientras está abierto.
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key !== 'Tab') return
      const contenedor = contenedorRef.current
      if (!contenedor) return

      const focusables = contenedor.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      )
      if (focusables.length === 0) return

      const primero = focusables[0]
      const ultimo = focusables[focusables.length - 1]
      if (!primero || !ultimo) return

      if (e.shiftKey && document.activeElement === primero) {
        e.preventDefault()
        ultimo.focus()
      } else if (!e.shiftKey && document.activeElement === ultimo) {
        e.preventDefault()
        primero.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [])

  const segundos = formatearSegundos(remainingMs)

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="sesion-modal-titulo"
      aria-describedby="sesion-modal-desc"
      aria-live="polite"
    >
      {/* Fondo oscurecido (no cierra al hacer clic: la única salida es renovar o esperar). */}
      <div className="absolute inset-0 bg-black/50" aria-hidden="true" />

      {/* Panel del diálogo */}
      <div
        ref={contenedorRef}
        className="
          relative w-full max-w-md
          bg-white dark:bg-dark-surface
          rounded-xl shadow-xl
          px-6 py-5
        "
      >
        <h3
          id="sesion-modal-titulo"
          className="text-lg font-semibold text-gray-900 dark:text-dark-text"
        >
          Tu sesión está por expirar
        </h3>
        <p
          id="sesion-modal-desc"
          className="mt-2 text-sm text-gray-600 dark:text-gray-300"
        >
          Por inactividad, tu sesión se cerrará en{' '}
          <span className="font-semibold">{segundos}</span>{' '}
          {segundos === 1 ? 'segundo' : 'segundos'}. Pulsa "Seguir conectado"
          para mantenerla activa.
        </p>

        <div className="mt-5 flex justify-end">
          <Button
            ref={botonRef}
            variant="confirm"
            onClick={onSeguirConectado}
          >
            Seguir conectado
          </Button>
        </div>
      </div>
    </div>
  )
}
