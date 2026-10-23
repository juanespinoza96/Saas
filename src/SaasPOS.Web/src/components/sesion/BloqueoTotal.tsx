// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Bloqueo total (etapa `block` del reloj único): overlay opaco a pantalla completa
// que impide toda interacción con la aplicación durante los últimos segundos antes
// del cierre (Req 5.5, 5.6, 6.3). Es el aviso más intrusivo: `role="alertdialog"`,
// focus trap y `aria-live="assertive"` para interrumpir al lector de pantalla.
// Al llegar la cuenta a 0, el reloj único dispara el logout; este componente
// invoca `onExpirado` como salvaguarda para cerrar sesión y redirigir a /login.

import { useEffect, useRef } from 'react'

interface BloqueoTotalProps {
  /** Milisegundos restantes hasta el cierre definitivo de sesión. */
  remainingMs: number
  /**
   * Se invoca cuando la cuenta llega a 0: cierra sesión y redirige a /login.
   * El reloj único también hace logout por su cuenta al alcanzar el deadline;
   * esta llamada es la salvaguarda del propio componente visual.
   */
  onExpirado: () => void
}

/**
 * Formatea los ms restantes como segundos enteros (ventana corta, típicamente
 * 10→0), redondeando hacia arriba para no mostrar 0 antes de tiempo.
 */
function formatearSegundos(ms: number): number {
  return Math.max(0, Math.ceil(ms / 1000))
}

export function BloqueoTotal({ remainingMs, onExpirado }: BloqueoTotalProps) {
  const contenedorRef = useRef<HTMLDivElement>(null)
  // Evita invocar `onExpirado` más de una vez cuando el tiempo llega a 0.
  const expiradoRef = useRef(false)

  // Al llegar a 0: disparar el cierre de sesión una sola vez.
  useEffect(() => {
    if (remainingMs <= 0 && !expiradoRef.current) {
      expiradoRef.current = true
      onExpirado()
    }
  }, [remainingMs, onExpirado])

  // Focus trap: el foco permanece dentro del overlay (no hay controles
  // accionables, así que se mantiene en el propio contenedor).
  useEffect(() => {
    contenedorRef.current?.focus()

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Tab') {
        // No hay elementos enfocables salida: atrapar el Tab en el contenedor.
        e.preventDefault()
        contenedorRef.current?.focus()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [])

  const segundos = formatearSegundos(remainingMs)

  return (
    <div
      ref={contenedorRef}
      tabIndex={-1}
      className="
        fixed inset-0 z-[60]
        flex flex-col items-center justify-center gap-4
        bg-black/90 text-white
        px-6 text-center
        outline-none
      "
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="sesion-bloqueo-titulo"
      aria-describedby="sesion-bloqueo-desc"
      aria-live="assertive"
    >
      <svg
        className="h-12 w-12"
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
      >
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={1.5}
          d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
        />
      </svg>
      <h2 id="sesion-bloqueo-titulo" className="text-2xl font-bold">
        Sesión bloqueada por inactividad
      </h2>
      <p id="sesion-bloqueo-desc" className="text-base max-w-md">
        Tu sesión se cerrará en{' '}
        <span className="font-bold">{segundos}</span>{' '}
        {segundos === 1 ? 'segundo' : 'segundos'}. Serás redirigido al inicio de
        sesión.
      </p>
    </div>
  )
}
