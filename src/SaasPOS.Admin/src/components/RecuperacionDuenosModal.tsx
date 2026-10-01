import { useCallback, useEffect, useState } from 'react'
import { Modal } from './ui/Modal'
import { Button } from './ui/Button'
import { formatDateTime } from '../lib/dateUtils'
import {
  obtenerSolicitudesRecuperacionDuenos,
  aprobarRecuperacionDueno,
  rechazarRecuperacionDueno,
  obtenerTemporalRecuperacionDueno,
} from '../lib/api'
import type { PendingRecoveryAdmin } from '../types/passwordRecovery'

interface RecuperacionDuenosModalProps {
  isOpen: boolean
  onClose: () => void
}

/**
 * Modal cross-tenant para que el SuperAdmin gestione las solicitudes de recuperación de
 * contraseña de usuarios con rol Dueño de cualquier comercio (Requirements 13.1–13.10).
 *
 * Lista las solicitudes con una columna de comercio (Req 13.1, 13.2) y permite:
 * - Aprobar: muestra un aviso previo de cierre de sesión, exige confirmación tras leerlo,
 *   y al confirmar revela la contraseña temporal en tipografía monoespaciada con botón para
 *   copiarla al portapapeles; el bloque permanece visible mientras la temporal no expire (24h)
 *   con reconsulta ilimitada (Req 13.3, 13.4, 13.5, 13.6, 13.8).
 * - Rechazar: abre un campo para el motivo obligatorio; impide confirmar si está vacío (Req 13.7).
 * - Muestra una leyenda permanente indicando que la ausencia de clave temporal se debe a la
 *   expiración del límite de 24 horas (Req 13.9).
 * - Aplica la paleta existente con soporte de modo claro y oscuro (Req 13.10).
 */
export function RecuperacionDuenosModal({ isOpen, onClose }: RecuperacionDuenosModalProps) {
  const [solicitudes, setSolicitudes] = useState<PendingRecoveryAdmin[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Solicitud sobre la que se muestra el aviso previo de aprobación (antes de confirmar).
  const [avisoRequestId, setAvisoRequestId] = useState<number | null>(null)
  // Solicitud en proceso de rechazo (con campo de motivo abierto).
  const [rechazoRequestId, setRechazoRequestId] = useState<number | null>(null)
  const [motivoRechazo, setMotivoRechazo] = useState('')

  // Contraseñas temporales reveladas por solicitud (Req 13.5): requestId -> texto plano.
  const [temporales, setTemporales] = useState<Record<number, string>>({})
  // Feedback "Copiado" temporal por solicitud (Req 13.6).
  const [copiadoRequestId, setCopiadoRequestId] = useState<number | null>(null)
  // Errores por tarjeta (aprobación/rechazo/copia/reconsulta) para no romper la lista completa.
  const [erroresPorSolicitud, setErroresPorSolicitud] = useState<Record<number, string>>({})
  // Indicadores de acción en curso por solicitud, para deshabilitar botones.
  const [accionEnCurso, setAccionEnCurso] = useState<number | null>(null)

  /** Carga (o recarga) las solicitudes de recuperación de dueños desde el backend. */
  const cargarSolicitudes = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await obtenerSolicitudesRecuperacionDuenos()
      setSolicitudes(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al cargar las solicitudes de recuperación.')
    } finally {
      setLoading(false)
    }
  }, [])

  // Al abrir el modal se cargan las solicitudes y se limpia el estado transitorio.
  useEffect(() => {
    if (!isOpen) return
    setAvisoRequestId(null)
    setRechazoRequestId(null)
    setMotivoRechazo('')
    setTemporales({})
    setCopiadoRequestId(null)
    setErroresPorSolicitud({})
    setAccionEnCurso(null)
    void cargarSolicitudes()
  }, [isOpen, cargarSolicitudes])

  /** Registra un mensaje de error asociado a una solicitud concreta. */
  function setErrorSolicitud(requestId: number, mensaje: string) {
    setErroresPorSolicitud(prev => ({ ...prev, [requestId]: mensaje }))
  }

  /** Limpia el error asociado a una solicitud concreta. */
  function limpiarErrorSolicitud(requestId: number) {
    setErroresPorSolicitud(prev => {
      const siguiente = { ...prev }
      delete siguiente[requestId]
      return siguiente
    })
  }

  /**
   * Confirma la aprobación tras haber mostrado el aviso previo de cierre de sesión (Req 13.3, 13.4).
   * Al éxito revela la contraseña temporal (Req 13.5) y recarga el listado para reflejar el estado.
   */
  async function handleConfirmarAprobacion(requestId: number) {
    setAccionEnCurso(requestId)
    limpiarErrorSolicitud(requestId)
    try {
      const respuesta = await aprobarRecuperacionDueno(requestId)
      setTemporales(prev => ({ ...prev, [requestId]: respuesta.tempPassword }))
      setAvisoRequestId(null)
      await cargarSolicitudes()
    } catch (err) {
      setErrorSolicitud(requestId, err instanceof Error ? err.message : 'Error al aprobar la solicitud.')
    } finally {
      setAccionEnCurso(null)
    }
  }

  /** Confirma el rechazo con el motivo obligatorio (Req 13.7). */
  async function handleConfirmarRechazo(requestId: number) {
    const motivo = motivoRechazo.trim()
    // Bloqueo defensivo: no confirmar si el motivo está vacío (Req 13.7).
    if (!motivo) return

    setAccionEnCurso(requestId)
    limpiarErrorSolicitud(requestId)
    try {
      await rechazarRecuperacionDueno(requestId, { motivoRechazo: motivo })
      setRechazoRequestId(null)
      setMotivoRechazo('')
      await cargarSolicitudes()
    } catch (err) {
      setErrorSolicitud(requestId, err instanceof Error ? err.message : 'Error al rechazar la solicitud.')
    } finally {
      setAccionEnCurso(null)
    }
  }

  /**
   * Reconsulta la contraseña temporal vigente de una solicitud Aprobada (Req 13.8). Permite volver
   * a mostrar el bloque monoespaciado cuando el modal se reabre o si aún no se ha revelado.
   */
  async function handleRevelarTemporal(requestId: number) {
    setAccionEnCurso(requestId)
    limpiarErrorSolicitud(requestId)
    try {
      const respuesta = await obtenerTemporalRecuperacionDueno(requestId)
      setTemporales(prev => ({ ...prev, [requestId]: respuesta.tempPassword }))
    } catch (err) {
      setErrorSolicitud(
        requestId,
        err instanceof Error ? err.message : 'No se pudo obtener la contraseña temporal.',
      )
    } finally {
      setAccionEnCurso(null)
    }
  }

  /** Copia la contraseña temporal al portapapeles (Req 13.6) y muestra feedback breve. */
  async function handleCopiarTemporal(requestId: number, tempPassword: string) {
    try {
      await navigator.clipboard.writeText(tempPassword)
      limpiarErrorSolicitud(requestId)
      setCopiadoRequestId(requestId)
      window.setTimeout(() => {
        setCopiadoRequestId(prev => (prev === requestId ? null : prev))
      }, 2000)
    } catch {
      setErrorSolicitud(requestId, 'No se pudo copiar al portapapeles. Cópiela manualmente.')
    }
  }

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Recuperación de contraseña — Dueños">
      <div className="space-y-4">
        {/* Leyenda permanente sobre la expiración de 24 horas (Req 13.9). */}
        <div className="rounded-lg border border-yellow-200 dark:border-yellow-800 bg-yellow-50 dark:bg-yellow-900/20 p-3">
          <p className="text-xs text-yellow-800 dark:text-yellow-300">
            La ausencia de una clave temporal en una solicitud aprobada se debe a que expiró el
            límite de 24 horas de vigencia. Pasado ese plazo, la contraseña temporal deja de estar
            disponible y el usuario deberá generar una nueva solicitud.
          </p>
        </div>

        {/* Error general de carga. */}
        {error && (
          <div className="rounded-lg border border-red-200 dark:border-red-800 bg-red-50 dark:bg-red-900/20 p-3">
            <p className="text-sm text-red-700 dark:text-red-400">{error}</p>
          </div>
        )}

        {/* Estado de carga. */}
        {loading ? (
          <div className="flex items-center justify-center py-10">
            <div className="animate-spin rounded-full h-7 w-7 border-b-2 border-action-confirm" />
          </div>
        ) : solicitudes.length === 0 ? (
          <p className="text-center py-8 text-sm text-gray-400 dark:text-dark-text/50">
            No hay solicitudes de recuperación de dueños pendientes.
          </p>
        ) : (
          <ul className="space-y-3 max-h-[60vh] overflow-y-auto pr-1">
            {solicitudes.map((solicitud) => (
              <SolicitudCard
                key={solicitud.requestId}
                solicitud={solicitud}
                avisoAbierto={avisoRequestId === solicitud.requestId}
                rechazoAbierto={rechazoRequestId === solicitud.requestId}
                motivoRechazo={motivoRechazo}
                temporal={temporales[solicitud.requestId] ?? null}
                copiado={copiadoRequestId === solicitud.requestId}
                errorSolicitud={erroresPorSolicitud[solicitud.requestId] ?? null}
                procesando={accionEnCurso === solicitud.requestId}
                onAbrirAviso={() => {
                  setRechazoRequestId(null)
                  setAvisoRequestId(solicitud.requestId)
                  limpiarErrorSolicitud(solicitud.requestId)
                }}
                onCancelarAviso={() => setAvisoRequestId(null)}
                onConfirmarAprobacion={() => handleConfirmarAprobacion(solicitud.requestId)}
                onAbrirRechazo={() => {
                  setAvisoRequestId(null)
                  setRechazoRequestId(solicitud.requestId)
                  setMotivoRechazo('')
                  limpiarErrorSolicitud(solicitud.requestId)
                }}
                onCancelarRechazo={() => {
                  setRechazoRequestId(null)
                  setMotivoRechazo('')
                }}
                onCambiarMotivo={setMotivoRechazo}
                onConfirmarRechazo={() => handleConfirmarRechazo(solicitud.requestId)}
                onRevelarTemporal={() => handleRevelarTemporal(solicitud.requestId)}
                onCopiarTemporal={(tempPassword) =>
                  handleCopiarTemporal(solicitud.requestId, tempPassword)
                }
              />
            ))}
          </ul>
        )}

        <div className="flex justify-end pt-2">
          <Button variant="neutral" onClick={onClose}>
            Cerrar
          </Button>
        </div>
      </div>
    </Modal>
  )
}

interface SolicitudCardProps {
  solicitud: PendingRecoveryAdmin
  avisoAbierto: boolean
  rechazoAbierto: boolean
  motivoRechazo: string
  temporal: string | null
  copiado: boolean
  errorSolicitud: string | null
  procesando: boolean
  onAbrirAviso: () => void
  onCancelarAviso: () => void
  onConfirmarAprobacion: () => void
  onAbrirRechazo: () => void
  onCancelarRechazo: () => void
  onCambiarMotivo: (valor: string) => void
  onConfirmarRechazo: () => void
  onRevelarTemporal: () => void
  onCopiarTemporal: (tempPassword: string) => void
}

/** Devuelve las clases de color del badge de estado, con soporte claro/oscuro (Req 13.10). */
function getEstadoBadgeClasses(estado: string): string {
  switch (estado) {
    case 'Pendiente':
      return 'bg-action-edit/10 text-action-edit dark:bg-action-edit/20'
    case 'Aprobada':
      return 'bg-action-confirm/10 text-action-confirm dark:bg-action-confirm/20'
    case 'Rechazada':
      return 'bg-action-danger/10 text-action-danger dark:bg-action-danger/20'
    default:
      return 'bg-gray-100 text-gray-600 dark:bg-gray-700/30 dark:text-gray-400'
  }
}

/** Tarjeta individual de una solicitud de recuperación de un Dueño. */
function SolicitudCard({
  solicitud,
  avisoAbierto,
  rechazoAbierto,
  motivoRechazo,
  temporal,
  copiado,
  errorSolicitud,
  procesando,
  onAbrirAviso,
  onCancelarAviso,
  onConfirmarAprobacion,
  onAbrirRechazo,
  onCancelarRechazo,
  onCambiarMotivo,
  onConfirmarRechazo,
  onRevelarTemporal,
  onCopiarTemporal,
}: SolicitudCardProps) {
  const esPendiente = solicitud.estado === 'Pendiente'
  const temporalVigente = solicitud.estado === 'Aprobada' && solicitud.tienePasswordTemporalVigente
  const motivoVacio = motivoRechazo.trim().length === 0

  return (
    <li className="rounded-lg border border-gray-200 dark:border-dark-surface bg-white dark:bg-dark-bg p-4">
      {/* Encabezado: solicitante, comercio y estado. */}
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-medium text-gray-900 dark:text-dark-text truncate">{solicitud.nombre}</p>
          <p className="text-sm text-gray-500 dark:text-dark-text/70 truncate">{solicitud.email}</p>
          {/* Columna de comercio del solicitante (Req 13.2). */}
          <p className="text-xs text-gray-500 dark:text-dark-text/60 mt-1">
            Comercio: <span className="font-medium text-gray-700 dark:text-dark-text/80">{solicitud.comercioNombre}</span>
          </p>
          <p className="text-xs text-gray-400 dark:text-dark-text/50 mt-0.5">
            Rol: {solicitud.rol} · Solicitado: {formatDateTime(solicitud.fechaSolicitud)}
          </p>
        </div>
        <span
          className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium shrink-0 ${getEstadoBadgeClasses(solicitud.estado)}`}
        >
          {solicitud.estado}
        </span>
      </div>

      {/* Error específico de la solicitud. */}
      {errorSolicitud && (
        <div className="mt-3 rounded-lg border border-red-200 dark:border-red-800 bg-red-50 dark:bg-red-900/20 p-2">
          <p className="text-xs text-red-700 dark:text-red-400">{errorSolicitud}</p>
        </div>
      )}

      {/* Aviso previo de aprobación con confirmación (Req 13.3, 13.4). */}
      {esPendiente && avisoAbierto && (
        <div className="mt-3 rounded-lg border border-yellow-200 dark:border-yellow-800 bg-yellow-50 dark:bg-yellow-900/20 p-3 space-y-3">
          <p className="text-xs text-yellow-800 dark:text-yellow-300">
            Al aprobar esta solicitud, si el usuario solicitante tiene una sesión activa, dicha
            sesión se cerrará de inmediato y deberá volver a ingresar con la contraseña temporal.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="neutral" className="text-xs px-2 py-1" onClick={onCancelarAviso} disabled={procesando}>
              Cancelar
            </Button>
            <Button
              variant="confirm"
              className="text-xs px-2 py-1"
              onClick={onConfirmarAprobacion}
              disabled={procesando}
            >
              {procesando ? 'Aprobando...' : 'Confirmar aprobación'}
            </Button>
          </div>
        </div>
      )}

      {/* Campo de motivo de rechazo obligatorio (Req 13.7). */}
      {esPendiente && rechazoAbierto && (
        <div className="mt-3 space-y-2">
          <label
            htmlFor={`motivo-rechazo-${solicitud.requestId}`}
            className="block text-sm font-medium text-gray-700 dark:text-dark-text/80"
          >
            Motivo del rechazo (obligatorio)
          </label>
          <textarea
            id={`motivo-rechazo-${solicitud.requestId}`}
            value={motivoRechazo}
            onChange={(e) => onCambiarMotivo(e.target.value)}
            rows={2}
            className="w-full px-3 py-2 rounded-lg border border-gray-300 dark:border-dark-surface bg-white dark:bg-dark-surface text-gray-900 dark:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-danger focus:border-transparent"
            placeholder="Explique por qué se rechaza esta solicitud"
          />
          <div className="flex justify-end gap-2">
            <Button variant="neutral" className="text-xs px-2 py-1" onClick={onCancelarRechazo} disabled={procesando}>
              Cancelar
            </Button>
            <Button
              variant="danger"
              className="text-xs px-2 py-1"
              onClick={onConfirmarRechazo}
              disabled={procesando || motivoVacio}
            >
              {procesando ? 'Rechazando...' : 'Confirmar rechazo'}
            </Button>
          </div>
        </div>
      )}

      {/* Bloque de contraseña temporal revelada, monoespaciada, con botón copiar (Req 13.5, 13.6). */}
      {temporal && (
        <div className="mt-3 rounded-lg border border-action-confirm/30 bg-action-confirm/5 dark:bg-action-confirm/10 p-3 space-y-2">
          <p className="text-xs text-gray-600 dark:text-dark-text/70">
            Contraseña temporal (vigente por 24 horas):
          </p>
          <div className="flex items-center gap-2">
            <code className="flex-1 font-mono text-sm bg-white dark:bg-dark-surface border border-gray-200 dark:border-dark-surface rounded px-2 py-1 text-gray-900 dark:text-dark-text break-all">
              {temporal}
            </code>
            <Button
              variant="edit"
              className="text-xs px-2 py-1 shrink-0"
              onClick={() => onCopiarTemporal(temporal)}
              aria-label={`Copiar la contraseña temporal de ${solicitud.nombre}`}
            >
              {copiado ? 'Copiado' : 'Copiar'}
            </Button>
          </div>
          {solicitud.fechaExpiracion && (
            <p className="text-xs text-gray-400 dark:text-dark-text/50">
              Expira: {formatDateTime(solicitud.fechaExpiracion)}
            </p>
          )}
        </div>
      )}

      {/* Acciones principales por estado. */}
      <div className="mt-3 flex flex-wrap items-center gap-2">
        {esPendiente && !avisoAbierto && !rechazoAbierto && (
          <>
            <Button variant="confirm" className="text-xs px-2 py-1" onClick={onAbrirAviso} disabled={procesando}>
              Aprobar
            </Button>
            <Button variant="danger" className="text-xs px-2 py-1" onClick={onAbrirRechazo} disabled={procesando}>
              Rechazar
            </Button>
          </>
        )}

        {/* Reconsulta de la temporal mientras siga vigente y no esté ya mostrada (Req 13.8). */}
        {temporalVigente && !temporal && (
          <Button variant="edit" className="text-xs px-2 py-1" onClick={onRevelarTemporal} disabled={procesando}>
            {procesando ? 'Cargando...' : 'Ver contraseña temporal'}
          </Button>
        )}
      </div>
    </li>
  )
}
