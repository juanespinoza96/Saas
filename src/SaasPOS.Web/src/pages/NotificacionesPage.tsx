import { useState, useEffect } from 'react'
import { api } from '../lib/api'
import { formatDateTime } from '../lib/dateUtils'
import { Button } from '../components/ui/Button'

interface Notificacion {
  id: string
  titulo: string
  mensaje: string
  leida: boolean
  tipoNotificacion: string
  fechaEmision: string
}

/**
 * Notificaciones page.
 * Lists all notifications, highlights unread, allows marking as read (Req 14.2, 14.3).
 */
export function NotificacionesPage() {
  const [notificaciones, setNotificaciones] = useState<Notificacion[]>([])
  const [loading, setLoading] = useState(true)
  const [markingId, setMarkingId] = useState<string | null>(null)

  useEffect(() => {
    fetchNotificaciones()
  }, [])

  async function fetchNotificaciones() {
    setLoading(true)
    try {
      const data = await api.get<Notificacion[]>('/api/tenants/notificaciones')
      // Sort newest first
      const sorted = [...data].sort(
        (a, b) => new Date(b.fechaEmision).getTime() - new Date(a.fechaEmision).getTime(),
      )
      setNotificaciones(sorted)
    } catch {
      setNotificaciones([])
    } finally {
      setLoading(false)
    }
  }

  async function handleMarcarLeida(id: string) {
    setMarkingId(id)
    try {
      await api.patch(`/api/tenants/notificaciones/${id}/leer`)
      setNotificaciones(prev =>
        prev.map(n => (n.id === id ? { ...n, leida: true } : n)),
      )
    } catch {
      // Failed silently
    } finally {
      setMarkingId(null)
    }
  }

  function getTipoColor(tipo: string): string {
    switch (tipo.toLowerCase()) {
      case 'stock_bajo':
        return 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400'
      case 'venta':
        return 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400'
      case 'sistema':
        return 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
      default:
        return 'bg-gray-100 text-gray-700 dark:bg-gray-700 dark:text-gray-300'
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-12">
        <div className="animate-spin h-8 w-8 border-4 border-action-confirm border-t-transparent rounded-full" />
      </div>
    )
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-6">
        Notificaciones
      </h1>

      {notificaciones.length === 0 ? (
        <div className="text-center py-12">
          <svg
            className="mx-auto h-12 w-12 text-gray-400 mb-4"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
            />
          </svg>
          <p className="text-gray-500 dark:text-gray-400">
            No tienes notificaciones.
          </p>
        </div>
      ) : (
        <div className="space-y-3">
          {notificaciones.map(notif => (
            <div
              key={notif.id}
              className={`
                bg-white dark:bg-dark-surface rounded-lg shadow p-4 
                border-l-4 transition-all
                ${notif.leida
                  ? 'border-l-gray-300 dark:border-l-gray-600'
                  : 'border-l-action-confirm'}
              `}
            >
              <div className="flex items-start justify-between gap-4">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-1">
                    <h3
                      className={`text-sm ${
                        notif.leida
                          ? 'font-normal text-gray-700 dark:text-gray-300'
                          : 'font-bold text-gray-900 dark:text-dark-text'
                      }`}
                    >
                      {notif.titulo}
                    </h3>
                    <span
                      className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${getTipoColor(notif.tipoNotificacion)}`}
                    >
                      {notif.tipoNotificacion}
                    </span>
                  </div>
                  <p className="text-sm text-gray-600 dark:text-gray-400 mb-1">
                    {notif.mensaje}
                  </p>
                  <p className="text-xs text-gray-400 dark:text-gray-500">
                    {formatDateTime(notif.fechaEmision)}
                  </p>
                </div>
                {!notif.leida && (
                  <Button
                    variant="neutral"
                    className="text-xs shrink-0"
                    loading={markingId === notif.id}
                    onClick={() => handleMarcarLeida(notif.id)}
                  >
                    Marcar como leída
                  </Button>
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
