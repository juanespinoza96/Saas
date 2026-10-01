import { useState, useEffect } from 'react'
import { api } from '../lib/api'
import { useAuth } from '../contexts/AuthContext'
import { formatDateTime } from '../lib/dateUtils'
import type {
  PendingRecoveryDto,
  EstadoSolicitudRecuperacion,
  RejectRecoveryRequest,
  ApproveResponse,
} from '../types/recuperacion'
import type { ApiError } from '../lib/api'
import { Button } from '../components/ui/Button'
import { Modal } from '../components/ui/Modal'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'
import { RolSelect } from '../components/ui/RolSelect'
import { ResponsiveTable } from '../components/ui/ResponsiveTable'

interface Usuario {
  id: string
  nombre: string
  email: string
  rol: string
  activo: boolean
  sucursalId: number | null
}

interface SucursalOption {
  id: string
  nombre: string
}

interface FormErrors {
  nombre?: string
  email?: string
  password?: string
}

/**
 * Formatea los minutos restantes de una solicitud/temporal como texto legible.
 * Ej.: 1380 → "23 h 0 min", 45 → "45 min". Valores <= 0 → "Expirada".
 */
function formatTiempoRestante(minutos: number): string {
  if (minutos <= 0) return 'Expirada'
  const horas = Math.floor(minutos / 60)
  const mins = minutos % 60
  if (horas > 0) return `${horas} h ${mins} min`
  return `${mins} min`
}

/**
 * Deriva el estado visible de una solicitud aplicando validación perezosa:
 * una solicitud Pendiente cuyo tiempo restante se agotó se muestra como Expirada.
 * Los estados Aprobada y Rechazada se conservan tal como los envía el backend.
 */
function estadoVisible(solicitud: PendingRecoveryDto): EstadoSolicitudRecuperacion | 'Expirada' {
  if (solicitud.estado === 'Pendiente' && solicitud.minutosRestantes <= 0) {
    return 'Expirada'
  }
  return solicitud.estado
}

/**
 * Mapea cada estado a las clases de color de la paleta existente con soporte
 * de modo claro/oscuro (Req 12.12, 12.13):
 * - Pendiente → ámbar (action-edit)
 * - Aprobada → verde (action-confirm)
 * - Rechazada / Expirada → rojo (action-danger)
 */
function clasesEstado(estado: EstadoSolicitudRecuperacion | 'Expirada'): {
  badge: string
  borde: string
} {
  switch (estado) {
    case 'Aprobada':
      return {
        badge: 'bg-action-confirm/15 text-action-confirm dark:bg-action-confirm/25',
        borde: 'border-l-4 border-l-action-confirm',
      }
    case 'Rechazada':
    case 'Expirada':
      return {
        badge: 'bg-action-danger/15 text-action-danger dark:bg-action-danger/25',
        borde: 'border-l-4 border-l-action-danger',
      }
    case 'Pendiente':
    default:
      return {
        badge: 'bg-action-edit/15 text-action-edit dark:bg-action-edit/25',
        borde: 'border-l-4 border-l-action-edit',
      }
  }
}

/**
 * Mapea el código HTTP de un error del cliente API a un mensaje descriptivo
 * para las acciones de aprobar/rechazar solicitudes de recuperación.
 * - 403 → jerarquía/comercio no autorizado
 * - 404 → solicitud inexistente
 * - 409 → estado incompatible (ya resuelta o expirada)
 * - 400 → petición inválida (p. ej. motivo faltante)
 */
function mensajeErrorAccion(error: unknown, accion: 'aprobar' | 'rechazar'): string {
  const status = (error as ApiError | undefined)?.status
  switch (status) {
    case 403:
      return 'No tiene autorización para procesar esta solicitud (jerarquía o comercio).'
    case 404:
      return 'La solicitud ya no existe o expiró.'
    case 409:
      return 'La solicitud ya fue resuelta o expiró; actualice el listado.'
    case 400:
      return accion === 'rechazar'
        ? 'Debe indicar un motivo de rechazo válido.'
        : 'No se pudo procesar la solicitud.'
    default:
      return accion === 'aprobar'
        ? 'No se pudo aprobar la solicitud. Intente nuevamente.'
        : 'No se pudo rechazar la solicitud. Intente nuevamente.'
  }
}

/**
 * Usuarios page.
 * CRUD for tenant users with role management, inline validation (Req 21.13),
 * modal forms (Req 21.9), and confirmation before deactivation (Req 21.15, 4.6).
 *
 * Incluye además la sección "Solicitudes de recuperación" visible solo para
 * Dueño/Gerente (Req 12.1, 12.2), con tarjetas por solicitud y estados por
 * color en modo claro/oscuro (Req 12.3, 12.11, 12.12, 12.13).
 */
export function UsuariosPage() {
  const { hasRole } = useAuth()

  // Solo Dueño y Gerente pueden ver la bandeja de recuperación (Req 12.1, 12.2)
  const puedeVerRecuperaciones = hasRole(['Dueño', 'Gerente'])
  const [usuarios, setUsuarios] = useState<Usuario[]>([])
  const [sucursales, setSucursales] = useState<SucursalOption[]>([])
  const [loading, setLoading] = useState(true)

  // Estado del límite de usuarios del plan
  const [puedeAgregar, setPuedeAgregar] = useState(false)
  const [limiteInfo, setLimiteInfo] = useState<{ limiteUsuarios: number | null; usuariosActuales: number } | null>(null)

  // Filtros
  const [filtroEstado, setFiltroEstado] = useState<'todos' | 'activos' | 'inactivos'>('todos')
  const [filtroSucursal, setFiltroSucursal] = useState<string>('todas')

  // Modal state
  const [showModal, setShowModal] = useState(false)
  const [editingUser, setEditingUser] = useState<Usuario | null>(null)
  const [saving, setSaving] = useState(false)

  // Form fields
  const [nombre, setNombre] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [rol, setRol] = useState('')
  const [sucursalId, setSucursalId] = useState('')
  const [errors, setErrors] = useState<FormErrors>({})

  // Estado para rastrear si los roles cargaron correctamente (creación y edición)
  const [rolesLoaded, setRolesLoaded] = useState(false)

  // Deactivation confirmation
  const [deactivateTarget, setDeactivateTarget] = useState<Usuario | null>(null)
  const [deactivating, setDeactivating] = useState(false)

  // Activation confirmation
  const [activateTarget, setActivateTarget] = useState<Usuario | null>(null)
  const [activating, setActivating] = useState(false)

  // Bandeja de solicitudes de recuperación (visible solo Dueño/Gerente)
  const [solicitudes, setSolicitudes] = useState<PendingRecoveryDto[]>([])
  const [loadingSolicitudes, setLoadingSolicitudes] = useState(false)

  // Aviso previo de aprobación (Req 12.5): solicitud pendiente de confirmar aprobación
  const [aprobarTarget, setAprobarTarget] = useState<PendingRecoveryDto | null>(null)
  const [aprobando, setAprobando] = useState(false)

  // Modal de rechazo con motivo obligatorio (Req 12.8, 12.9)
  const [rechazarTarget, setRechazarTarget] = useState<PendingRecoveryDto | null>(null)
  const [motivoRechazo, setMotivoRechazo] = useState('')
  const [rechazando, setRechazando] = useState(false)

  // Error de la acción en curso (aprobar/rechazar), mostrado dentro del modal
  const [accionError, setAccionError] = useState<string | null>(null)

  // Contraseñas temporales reveladas en texto plano, indexadas por requestId
  // (Req 12.6): se llenan al aprobar y al revelar una temporal vigente ya
  // aprobada. Se mantienen en memoria mientras la temporal no expire (Req 12.10).
  const [tempPasswords, setTempPasswords] = useState<Record<number, string>>({})

  // requestId de la temporal cuyo botón "Copiar" acaba de usarse, para mostrar
  // el feedback "Copiado" de forma temporal (accesibilidad, Req 12.7).
  const [copiadoRequestId, setCopiadoRequestId] = useState<number | null>(null)

  // requestId en proceso de revelado (reconsulta temp-password) tras recargar.
  const [revelandoRequestId, setRevelandoRequestId] = useState<number | null>(null)

  // Errores de revelado/copiado por solicitud, mostrados dentro de la tarjeta.
  const [tempErrores, setTempErrores] = useState<Record<number, string>>({})

  useEffect(() => {
    fetchData()
  }, [])

  // Cargar las solicitudes de recuperación solo cuando el rol lo permite
  useEffect(() => {
    if (puedeVerRecuperaciones) {
      fetchSolicitudes()
    }
  }, [puedeVerRecuperaciones])

  async function fetchSolicitudes() {
    setLoadingSolicitudes(true)
    try {
      const data = await api.get<PendingRecoveryDto[]>('/api/tenants/auth/password-recovery/pending')
      setSolicitudes(data)
    } catch {
      // Error al cargar solicitudes de recuperación
      setSolicitudes([])
    } finally {
      setLoadingSolicitudes(false)
    }
  }

  /** Abre el aviso previo de aprobación para una solicitud (Req 12.4, 12.5). */
  function openAprobar(solicitud: PendingRecoveryDto) {
    setAccionError(null)
    setAprobarTarget(solicitud)
  }

  /** Abre el modal de rechazo con el campo de motivo vacío (Req 12.4, 12.8). */
  function openRechazar(solicitud: PendingRecoveryDto) {
    setAccionError(null)
    setMotivoRechazo('')
    setRechazarTarget(solicitud)
  }

  /**
   * Confirma la aprobación tras el aviso (Req 12.5): invoca el endpoint de
   * aprobación, guarda la contraseña temporal devuelta en texto plano para
   * revelarla en la tarjeta (Req 12.6), cierra el aviso y refresca el listado.
   * Mapea 403/404/409 a mensajes descriptivos.
   */
  async function handleAprobar() {
    if (!aprobarTarget) return
    const { requestId } = aprobarTarget

    setAprobando(true)
    setAccionError(null)
    try {
      const respuesta = await api.post<ApproveResponse>(
        `/api/tenants/auth/password-recovery/approve/${requestId}`,
      )
      // Guardar la temporal en texto plano indexada por solicitud para revelar
      // el bloque monoespaciado con botón copiar (Req 12.6, 12.7).
      setTempPasswords(prev => ({ ...prev, [requestId]: respuesta.tempPassword }))
      setTempErrores(prev => {
        const { [requestId]: _omitido, ...resto } = prev
        return resto
      })
      setAprobarTarget(null)
      await fetchSolicitudes()
    } catch (error) {
      setAccionError(mensajeErrorAccion(error, 'aprobar'))
    } finally {
      setAprobando(false)
    }
  }

  /**
   * Revela la contraseña temporal de una solicitud ya Aprobada cuya temporal
   * sigue vigente (Req 12.10) reconsultando el backend. Se usa tras recargar la
   * página, cuando la temporal no está en memoria. Mapea 404/410 a un mensaje
   * de expiración/eliminación mostrado dentro de la tarjeta.
   */
  async function handleRevelarTemporal(requestId: number) {
    setRevelandoRequestId(requestId)
    setTempErrores(prev => {
      const { [requestId]: _omitido, ...resto } = prev
      return resto
    })
    try {
      const respuesta = await api.get<ApproveResponse>(
        `/api/tenants/auth/password-recovery/${requestId}/temp-password`,
        { skipCache: true },
      )
      setTempPasswords(prev => ({ ...prev, [requestId]: respuesta.tempPassword }))
    } catch (error) {
      const status = (error as ApiError | undefined)?.status
      const mensaje =
        status === 404 || status === 410
          ? 'La contraseña temporal ya expiró o fue eliminada.'
          : 'No se pudo obtener la contraseña temporal. Intente nuevamente.'
      setTempErrores(prev => ({ ...prev, [requestId]: mensaje }))
      // Actualizar el listado para reflejar la posible expiración perezosa.
      await fetchSolicitudes()
    } finally {
      setRevelandoRequestId(null)
    }
  }

  /**
   * Copia la contraseña temporal al portapapeles con
   * `navigator.clipboard.writeText` (Req 12.7) y muestra el feedback "Copiado"
   * de forma temporal. Ante fallo (permisos/entorno) muestra un mensaje en la
   * tarjeta correspondiente.
   */
  async function handleCopiarTemporal(requestId: number, tempPassword: string) {
    try {
      await navigator.clipboard.writeText(tempPassword)
      setCopiadoRequestId(requestId)
      setTempErrores(prev => {
        const { [requestId]: _omitido, ...resto } = prev
        return resto
      })
      // Ocultar el feedback "Copiado" tras 2 segundos.
      window.setTimeout(() => {
        setCopiadoRequestId(prev => (prev === requestId ? null : prev))
      }, 2000)
    } catch {
      setTempErrores(prev => ({
        ...prev,
        [requestId]: 'No se pudo copiar al portapapeles. Copie el valor manualmente.',
      }))
    }
  }

  /**
   * Confirma el rechazo (Req 12.8, 12.9): el motivo es obligatorio; si está
   * vacío o solo tiene espacios no se invoca el endpoint. Mapea 400/403/404 a
   * mensajes descriptivos y refresca el listado al terminar.
   */
  async function handleRechazar() {
    if (!rechazarTarget) return
    const motivo = motivoRechazo.trim()
    // Guardia defensiva: el botón ya está deshabilitado si el motivo está vacío
    if (!motivo) return

    setRechazando(true)
    setAccionError(null)
    try {
      const body: RejectRecoveryRequest = { motivoRechazo: motivo }
      await api.post(`/api/tenants/auth/password-recovery/reject/${rechazarTarget.requestId}`, body)
      setRechazarTarget(null)
      setMotivoRechazo('')
      await fetchSolicitudes()
    } catch (error) {
      setAccionError(mensajeErrorAccion(error, 'rechazar'))
    } finally {
      setRechazando(false)
    }
  }

  async function fetchData() {
    setLoading(true)
    try {
      const [usrs, sucs, limite] = await Promise.all([
        api.get<Usuario[]>('/api/tenants/usuarios'),
        api.get<SucursalOption[]>('/api/tenants/sucursales'),
        api.get<{ limiteUsuarios: number | null; usuariosActuales: number; puedeAgregar: boolean }>('/api/tenants/usuarios/limite'),
      ])
      setUsuarios(usrs)
      setSucursales(sucs)
      setPuedeAgregar(limite.puedeAgregar)
      setLimiteInfo({ limiteUsuarios: limite.limiteUsuarios, usuariosActuales: limite.usuariosActuales })
    } catch {
      // Error al cargar datos
    } finally {
      setLoading(false)
    }
  }

  function openCreateModal() {
    setEditingUser(null)
    setNombre('')
    setEmail('')
    setPassword('')
    setRol('')
    setSucursalId('')
    setErrors({})
    setShowModal(true)
  }

  function openEditModal(user: Usuario) {
    setEditingUser(user)
    setNombre(user.nombre)
    setEmail(user.email)
    setPassword('')
    setRol(user.rol)
    setSucursalId(String(user.sucursalId ?? ''))
    setErrors({})
    setShowModal(true)
  }

  function validateForm(): boolean {
    const newErrors: FormErrors = {}

    if (!nombre.trim()) {
      newErrors.nombre = 'El nombre es requerido'
    }

    if (!email.trim()) {
      newErrors.email = 'El email es requerido'
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      newErrors.email = 'Formato de email inválido'
    }

    if (!editingUser && !password.trim()) {
      newErrors.password = 'La contraseña es requerida'
    } else if (!editingUser && password.length < 6) {
      newErrors.password = 'La contraseña debe tener al menos 6 caracteres'
    }

    setErrors(newErrors)
    return Object.keys(newErrors).length === 0
  }

  async function handleSubmit() {
    if (!validateForm()) return

    setSaving(true)
    try {
      const body: Record<string, unknown> = {
        nombre: nombre.trim(),
        email: email.trim(),
        rol,
        sucursalId: sucursalId || null,
      }

      if (editingUser) {
        await api.put(`/api/tenants/usuarios/${editingUser.id}`, body)
      } else {
        body.password = password
        await api.post('/api/tenants/usuarios', body)
      }

      setShowModal(false)
      await fetchData()
    } catch {
      // Save failed
    } finally {
      setSaving(false)
    }
  }

  async function handleDeactivate() {
    if (!deactivateTarget) return

    setDeactivating(true)
    try {
      await api.patch(`/api/tenants/usuarios/${deactivateTarget.id}/desactivar`)
      setDeactivateTarget(null)
      await fetchData()
    } catch {
      // Deactivation failed
    } finally {
      setDeactivating(false)
    }
  }

  async function handleActivate() {
    if (!activateTarget) return

    setActivating(true)
    try {
      await api.patch(`/api/tenants/usuarios/${activateTarget.id}/activar`)
      setActivateTarget(null)
      await fetchData()
    } catch {
      // Activation failed
    } finally {
      setActivating(false)
    }
  }

  // Filtrado combinado (anidado) de usuarios
  const usuariosFiltrados = usuarios.filter(user => {
    // Filtro por estado
    if (filtroEstado === 'activos' && !user.activo) return false
    if (filtroEstado === 'inactivos' && user.activo) return false

    // Filtro por sucursal (comparación con String() por diferencia de tipos number vs string)
    if (filtroSucursal !== 'todas') {
      if (filtroSucursal === 'sin-sucursal') {
        if (user.sucursalId !== null) return false
      } else {
        if (String(user.sucursalId) !== filtroSucursal) return false
      }
    }

    return true
  })

  function getSucursalNombre(id: number | null): string {
    if (!id) return '—'
    const suc = sucursales.find(s => String(s.id) === String(id))
    return suc?.nombre ?? '—'
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
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
          Usuarios
        </h1>
        <div className="flex items-center gap-3">
          {limiteInfo && limiteInfo.limiteUsuarios !== null && (
            <span className="text-sm text-gray-500 dark:text-gray-400">
              {limiteInfo.usuariosActuales}/{limiteInfo.limiteUsuarios} usuarios
            </span>
          )}
          <Button
            variant="confirm"
            onClick={openCreateModal}
            disabled={!puedeAgregar}
            title={!puedeAgregar ? 'Se alcanzó el límite de usuarios del plan' : undefined}
          >
            Nuevo Usuario
          </Button>
        </div>
      </div>

      {/* Filtros */}
      <div className="flex flex-wrap gap-3 mb-4">
        <div>
          <label htmlFor="filtro-estado" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
            Estado
          </label>
          <select
            id="filtro-estado"
            value={filtroEstado}
            onChange={e => setFiltroEstado(e.target.value as 'todos' | 'activos' | 'inactivos')}
            className="rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
          >
            <option value="todos">Todos</option>
            <option value="activos">Activos</option>
            <option value="inactivos">Inactivos</option>
          </select>
        </div>
        <div>
          <label htmlFor="filtro-sucursal" className="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
            Sucursal
          </label>
          <select
            id="filtro-sucursal"
            value={filtroSucursal}
            onChange={e => setFiltroSucursal(e.target.value)}
            className="rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
          >
            <option value="todas">Todas</option>
            <option value="sin-sucursal">Sin asignar</option>
            {sucursales.map(s => (
              <option key={s.id} value={s.id}>
                {s.nombre}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Users table */}
      <div className="bg-white dark:bg-dark-surface rounded-lg shadow overflow-hidden">
        <ResponsiveTable>
          <table className="w-full text-sm text-left">
            <thead className="text-xs uppercase bg-gray-100 dark:bg-dark-bg text-gray-600 dark:text-gray-400">
              <tr>
                <th className="px-4 py-3 sticky-col">Nombre</th>
                <th className="px-4 py-3">Email</th>
                <th className="px-4 py-3">Rol</th>
                <th className="px-4 py-3">Sucursal</th>
                <th className="px-4 py-3">Estado</th>
                <th className="px-4 py-3">Acciones</th>
              </tr>
            </thead>
            <tbody>
              {usuariosFiltrados.length === 0 ? (
                <tr>
                  <td
                    colSpan={6}
                    className="px-4 py-8 text-center text-gray-500 dark:text-gray-400"
                  >
                    No hay usuarios que coincidan con los filtros.
                  </td>
                </tr>
              ) : (
                usuariosFiltrados.map(user => (
                  <tr
                    key={user.id}
                    className="border-b border-gray-200 dark:border-gray-700 hover:bg-gray-50 dark:hover:bg-dark-bg/50"
                  >
                    <td className="px-4 py-3 text-gray-900 dark:text-dark-text font-medium sticky-col">
                      {user.nombre}
                    </td>
                    <td className="px-4 py-3 text-gray-700 dark:text-gray-300">
                      {user.email}
                    </td>
                    <td className="px-4 py-3 text-gray-700 dark:text-gray-300">
                      {user.rol}
                    </td>
                    <td className="px-4 py-3 text-gray-700 dark:text-gray-300">
                      {getSucursalNombre(user.sucursalId)}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${
                          user.activo
                            ? 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400'
                            : 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400'
                        }`}
                      >
                        {user.activo ? 'Activo' : 'Inactivo'}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex gap-2">
                        <Button
                          variant="edit"
                          className="text-xs px-2 py-1"
                          onClick={() => openEditModal(user)}
                        >
                          Editar
                        </Button>
                        {user.activo ? (
                          <Button
                            variant="danger"
                            className="text-xs px-2 py-1"
                            onClick={() => setDeactivateTarget(user)}
                          >
                            Desactivar
                          </Button>
                        ) : (
                          <Button
                            variant="confirm"
                            className="text-xs px-2 py-1"
                            onClick={() => setActivateTarget(user)}
                          >
                            Activar
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </ResponsiveTable>
      </div>

      {/* Sección Solicitudes de recuperación — visible solo para Dueño/Gerente (Req 12.1, 12.2) */}
      {puedeVerRecuperaciones && (
        <section className="mt-10" aria-labelledby="titulo-solicitudes-recuperacion">
          <h2
            id="titulo-solicitudes-recuperacion"
            className="text-xl font-bold text-gray-900 dark:text-dark-text mb-2"
          >
            Solicitudes de recuperación
          </h2>

          {/* Leyenda permanente sobre la expiración de 24 horas (Req 12.11) */}
          <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
            La ausencia de una clave temporal se debe a la expiración del límite de 24 horas.
          </p>

          {loadingSolicitudes ? (
            <div className="flex items-center justify-center py-8">
              <div className="animate-spin h-6 w-6 border-4 border-action-confirm border-t-transparent rounded-full" />
            </div>
          ) : solicitudes.length === 0 ? (
            <div className="bg-white dark:bg-dark-surface rounded-lg shadow p-6 text-center text-gray-500 dark:text-gray-400">
              No hay solicitudes de recuperación.
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
              {solicitudes.map(solicitud => {
                const estado = estadoVisible(solicitud)
                const colores = clasesEstado(estado)
                return (
                  <article
                    key={solicitud.requestId}
                    className={`bg-white dark:bg-dark-surface rounded-lg shadow p-4 ${colores.borde}`}
                  >
                    {/* Encabezado: nombre + badge de estado por color */}
                    <div className="flex items-start justify-between gap-2 mb-3">
                      <h3 className="font-semibold text-gray-900 dark:text-dark-text break-words">
                        {solicitud.nombre}
                      </h3>
                      <span
                        className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium whitespace-nowrap ${colores.badge}`}
                      >
                        {estado}
                      </span>
                    </div>

                    {/* Detalle: correo, rol, fecha de solicitud y tiempo restante (Req 12.3) */}
                    <dl className="space-y-1.5 text-sm">
                      <div className="flex justify-between gap-2">
                        <dt className="text-gray-500 dark:text-gray-400">Correo</dt>
                        <dd className="text-gray-800 dark:text-gray-200 text-right break-all">
                          {solicitud.email}
                        </dd>
                      </div>
                      <div className="flex justify-between gap-2">
                        <dt className="text-gray-500 dark:text-gray-400">Rol</dt>
                        <dd className="text-gray-800 dark:text-gray-200 text-right">
                          {solicitud.rol}
                        </dd>
                      </div>
                      <div className="flex justify-between gap-2">
                        <dt className="text-gray-500 dark:text-gray-400">Solicitud</dt>
                        <dd className="text-gray-800 dark:text-gray-200 text-right">
                          {formatDateTime(solicitud.fechaSolicitud)}
                        </dd>
                      </div>
                      <div className="flex justify-between gap-2">
                        <dt className="text-gray-500 dark:text-gray-400">Tiempo restante</dt>
                        <dd className="text-gray-800 dark:text-gray-200 text-right font-medium">
                          {formatTiempoRestante(solicitud.minutosRestantes)}
                        </dd>
                      </div>
                    </dl>

                    {/*
                      Bloque de contraseña temporal (Req 12.6, 12.7, 12.10): se
                      muestra en solicitudes Aprobadas cuya temporal sigue
                      vigente. Si la temporal está en memoria (recién aprobada o
                      revelada), se muestra en tipografía monoespaciada con un
                      botón para copiarla al portapapeles; si no está en memoria
                      pero sigue vigente (p. ej. tras recargar), se ofrece un
                      botón para revelarla reconsultando el backend.
                    */}
                    {estado === 'Aprobada' && solicitud.tienePasswordTemporalVigente && (
                      <div className="mt-4 rounded-lg border border-action-confirm/40 bg-action-confirm/5 dark:bg-action-confirm/10 p-3">
                        <p className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2">
                          Contraseña temporal
                        </p>

                        {tempPasswords[solicitud.requestId] !== undefined ? (
                          <>
                            <div className="flex items-center gap-2">
                              {/* Bloque monoespaciado con la temporal en texto plano (Req 12.6) */}
                              <code
                                className="flex-1 select-all break-all rounded bg-gray-100 dark:bg-dark-bg px-2 py-2 font-mono text-sm text-gray-900 dark:text-dark-text"
                                aria-label={`Contraseña temporal de ${solicitud.nombre}`}
                              >
                                {tempPasswords[solicitud.requestId]}
                              </code>
                              <Button
                                variant="edit"
                                type="button"
                                className="min-h-[44px] whitespace-nowrap"
                                onClick={() =>
                                  handleCopiarTemporal(
                                    solicitud.requestId,
                                    tempPasswords[solicitud.requestId] as string,
                                  )
                                }
                                aria-label={`Copiar la contraseña temporal de ${solicitud.nombre}`}
                              >
                                {copiadoRequestId === solicitud.requestId ? 'Copiado' : 'Copiar'}
                              </Button>
                            </div>
                            {/* Feedback accesible del resultado del copiado (Req 12.7) */}
                            <span className="sr-only" role="status" aria-live="polite">
                              {copiadoRequestId === solicitud.requestId
                                ? 'Contraseña temporal copiada al portapapeles'
                                : ''}
                            </span>
                          </>
                        ) : (
                          <Button
                            variant="edit"
                            type="button"
                            className="min-h-[44px] w-full"
                            loading={revelandoRequestId === solicitud.requestId}
                            disabled={revelandoRequestId === solicitud.requestId}
                            onClick={() => handleRevelarTemporal(solicitud.requestId)}
                            aria-label={`Mostrar la contraseña temporal de ${solicitud.nombre}`}
                          >
                            Mostrar contraseña temporal
                          </Button>
                        )}

                        {tempErrores[solicitud.requestId] && (
                          <p className="mt-2 text-xs text-action-danger" role="alert">
                            {tempErrores[solicitud.requestId]}
                          </p>
                        )}
                      </div>
                    )}

                    {/*
                      Acciones Aprobar/Rechazar (Req 12.4): solo para solicitudes
                      cuyo estado visible sigue siendo Pendiente (no expiradas ni
                      ya resueltas). Áreas táctiles ≥44px (min-h-[44px]).
                    */}
                    {estado === 'Pendiente' && (
                      <div className="flex gap-2 mt-4">
                        <Button
                          variant="confirm"
                          className="flex-1 min-h-[44px]"
                          onClick={() => openAprobar(solicitud)}
                          aria-label={`Aprobar la solicitud de ${solicitud.nombre}`}
                        >
                          Aprobar
                        </Button>
                        <Button
                          variant="danger"
                          className="flex-1 min-h-[44px]"
                          onClick={() => openRechazar(solicitud)}
                          aria-label={`Rechazar la solicitud de ${solicitud.nombre}`}
                        >
                          Rechazar
                        </Button>
                      </div>
                    )}
                  </article>
                )
              })}
            </div>
          )}
        </section>
      )}

      {/* Create/Edit Modal (Req 21.9) */}
      <Modal
        open={showModal}
        onClose={() => setShowModal(false)}
        title={editingUser ? 'Editar Usuario' : 'Nuevo Usuario'}
        size="md"
      >
        <form
          onSubmit={e => {
            e.preventDefault()
            handleSubmit()
          }}
          className="space-y-4"
        >
          {/* Nombre */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Nombre *
            </label>
            <input
              type="text"
              value={nombre}
              onChange={e => setNombre(e.target.value)}
              className={`w-full rounded-lg border ${
                errors.nombre
                  ? 'border-action-danger'
                  : 'border-gray-300 dark:border-gray-600'
              } bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent`}
            />
            {errors.nombre && (
              <p className="mt-1 text-xs text-action-danger">{errors.nombre}</p>
            )}
          </div>

          {/* Email */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Email *
            </label>
            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              className={`w-full rounded-lg border ${
                errors.email
                  ? 'border-action-danger'
                  : 'border-gray-300 dark:border-gray-600'
              } bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent`}
            />
            {errors.email && (
              <p className="mt-1 text-xs text-action-danger">{errors.email}</p>
            )}
          </div>

          {/* Password (only on create) */}
          {!editingUser && (
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                Contraseña *
              </label>
              <input
                type="password"
                value={password}
                onChange={e => setPassword(e.target.value)}
                className={`w-full rounded-lg border ${
                  errors.password
                    ? 'border-action-danger'
                    : 'border-gray-300 dark:border-gray-600'
                } bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent`}
              />
              {errors.password && (
                <p className="mt-1 text-xs text-action-danger">{errors.password}</p>
              )}
            </div>
          )}

          {/* Rol */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Rol
            </label>
            {editingUser ? (
              <RolSelect
                value={rol}
                onChange={setRol}
                currentRolNoDisponible={editingUser.rol}
                onLoadStateChange={setRolesLoaded}
              />
            ) : (
              <RolSelect
                value={rol}
                onChange={setRol}
                onLoadStateChange={setRolesLoaded}
              />
            )}
          </div>

          {/* Sucursal */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Sucursal
            </label>
            <select
              value={sucursalId}
              onChange={e => setSucursalId(e.target.value)}
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-confirm focus:border-transparent"
            >
              <option value="">Sin asignar</option>
              {sucursales.map(s => (
                <option key={s.id} value={s.id}>
                  {s.nombre}
                </option>
              ))}
            </select>
          </div>

          {/* Actions */}
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="neutral" type="button" onClick={() => setShowModal(false)}>
              Cancelar
            </Button>
            <Button variant="confirm" type="submit" loading={saving} disabled={saving || !rolesLoaded}>
              {editingUser ? 'Guardar Cambios' : 'Crear Usuario'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Deactivation Confirm Dialog (Req 21.15, 4.6) */}
      <ConfirmDialog
        open={!!deactivateTarget}
        onClose={() => setDeactivateTarget(null)}
        onConfirm={handleDeactivate}
        title="Desactivar Usuario"
        confirmLabel="Desactivar"
        loading={deactivating}
      >
        <p>
          ¿Está seguro de que desea desactivar al usuario{' '}
          <strong>{deactivateTarget?.nombre}</strong>?
        </p>
        <p className="mt-2 text-action-danger font-medium">
          ⚠️ Las sesiones activas de este usuario serán terminadas inmediatamente.
        </p>
      </ConfirmDialog>

      {/* Activation Confirm Dialog */}
      <ConfirmDialog
        open={!!activateTarget}
        onClose={() => setActivateTarget(null)}
        onConfirm={handleActivate}
        title="Activar Usuario"
        confirmLabel="Activar"
        loading={activating}
      >
        <p>
          ¿Está seguro de que desea reactivar al usuario{' '}
          <strong>{activateTarget?.nombre}</strong>?
        </p>
      </ConfirmDialog>

      {/*
        Aviso previo de aprobación (Req 12.5): informa que si el solicitante
        tiene una sesión activa se cerrará de inmediato y deberá reingresar con
        la contraseña temporal; requiere confirmación explícita del aprobador.
      */}
      <ConfirmDialog
        open={!!aprobarTarget}
        onClose={() => {
          setAprobarTarget(null)
          setAccionError(null)
        }}
        onConfirm={handleAprobar}
        title="Aprobar solicitud de recuperación"
        confirmLabel="Confirmar aprobación"
        confirmVariant="confirm"
        loading={aprobando}
      >
        <p>
          Va a aprobar la solicitud de{' '}
          <strong>{aprobarTarget?.nombre}</strong>.
        </p>
        <p className="mt-2 text-action-danger font-medium">
          ⚠️ Si el usuario tiene una sesión activa en este momento, dicha sesión
          se cerrará de inmediato y deberá volver a ingresar mediante el proceso
          de recuperación usando la contraseña temporal.
        </p>
        {accionError && (
          <p className="mt-3 text-sm text-action-danger" role="alert">
            {accionError}
          </p>
        )}
      </ConfirmDialog>

      {/*
        Modal de rechazo con motivo obligatorio (Req 12.8, 12.9): el botón de
        confirmar permanece deshabilitado mientras el motivo esté vacío o solo
        contenga espacios.
      */}
      <Modal
        open={!!rechazarTarget}
        onClose={() => {
          setRechazarTarget(null)
          setMotivoRechazo('')
          setAccionError(null)
        }}
        title="Rechazar solicitud de recuperación"
        size="md"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-600 dark:text-gray-400">
            Indique el motivo del rechazo de la solicitud de{' '}
            <strong className="text-gray-800 dark:text-gray-200">
              {rechazarTarget?.nombre}
            </strong>
            . El motivo es obligatorio.
          </p>

          <div>
            <label
              htmlFor="motivo-rechazo"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Motivo del rechazo *
            </label>
            <textarea
              id="motivo-rechazo"
              value={motivoRechazo}
              onChange={e => setMotivoRechazo(e.target.value)}
              rows={3}
              autoFocus
              aria-required="true"
              className="w-full rounded-lg border border-gray-300 dark:border-gray-600 bg-white dark:bg-dark-bg px-3 py-2 text-sm text-gray-900 dark:text-dark-text focus:ring-2 focus:ring-action-danger focus:border-transparent"
              placeholder="Describa el motivo del rechazo…"
            />
          </div>

          {accionError && (
            <p className="text-sm text-action-danger" role="alert">
              {accionError}
            </p>
          )}

          <div className="flex justify-end gap-3">
            <Button
              variant="neutral"
              type="button"
              onClick={() => {
                setRechazarTarget(null)
                setMotivoRechazo('')
                setAccionError(null)
              }}
              disabled={rechazando}
            >
              Cancelar
            </Button>
            <Button
              variant="danger"
              type="button"
              onClick={handleRechazar}
              loading={rechazando}
              // Deshabilitado si el motivo está vacío o solo contiene espacios (Req 12.9)
              disabled={rechazando || motivoRechazo.trim().length === 0}
            >
              Confirmar rechazo
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
