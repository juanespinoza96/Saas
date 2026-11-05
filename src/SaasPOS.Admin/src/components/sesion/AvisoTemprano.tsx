// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Aviso temprano (etapa `early` del reloj único): banner NO intrusivo que informa
// de que la sesión está por expirar, sin bloquear la interacción del usuario. No
// roba el foco ni atrapa la navegación: cualquier actividad reinicia el reloj y
// descarta este aviso automáticamente (Req 5.1, 5.2). Usa `aria-live="polite"`
// para anunciarse sin interrumpir al lector de pantalla.

interface AvisoTempranoProps {
  /** Milisegundos restantes hasta el cierre de sesión por inactividad. */
  remainingMs: number
}

/**
 * Formatea los ms restantes como "m:ss" (p. ej. 4:05) para mostrar al usuario.
 */
function formatearRestante(ms: number): string {
  const totalSeg = Math.max(0, Math.ceil(ms / 1000))
  const min = Math.floor(totalSeg / 60)
  const seg = totalSeg % 60
  return `${min}:${seg.toString().padStart(2, '0')}`
}

export function AvisoTemprano({ remainingMs }: AvisoTempranoProps) {
  return (
    <div
      role="status"
      aria-live="polite"
      className="
        fixed bottom-4 right-4 z-40 max-w-sm
        rounded-lg shadow-lg
        bg-action-edit text-white
        px-4 py-3
        flex items-center gap-3
      "
    >
      <svg
        className="h-5 w-5 flex-shrink-0"
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
      >
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={2}
          d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"
        />
      </svg>
      <p className="text-sm font-medium">
        Tu sesión expirará en {formatearRestante(remainingMs)} por inactividad.
        Cualquier interacción la mantendrá activa.
      </p>
    </div>
  )
}
