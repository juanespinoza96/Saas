import { useState, type FormEvent } from 'react'
import { validatePassword } from '../../lib/passwordPolicy'
import { detectTimezone } from '../../lib/dateUtils'
import { Button } from '../ui/Button'

/** Clave bajo la que se almacena el JWT en localStorage (misma que usa el api client). */
const AUTH_TOKEN_KEY = 'saas-pos-token'

/** Base de la API (mismo origen que el api client de lib/api.ts). */
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'https://api.saas.com'

/** Endpoint del cambio voluntario de contraseña (Requirement 17, contrato 7). */
const CHANGE_PASSWORD_VOLUNTARY_URL = '/api/tenants/auth/change-password-voluntary'

/**
 * Cuerpo de la respuesta de error del backend para el cambio voluntario.
 * El controlador retorna `{ message }` en 400 (política) y en otros errores.
 */
interface VoluntaryErrorDetails {
  message?: string
}

/**
 * Bloque de cambio voluntario de contraseña de la Pantalla_Configuracion
 * (Requirement 17). Se muestra a cualquier usuario autenticado.
 *
 * Flujo:
 * - Recoge la contraseña actual y la nueva contraseña (+ confirmación).
 * - Valida la NUEVA contraseña en el cliente contra el espejo de la
 *   Politica_Password (`validatePassword`, Req 17.4/17.5) antes de enviar.
 * - Envía `POST /api/tenants/auth/change-password-voluntary` con
 *   `{ currentPassword, newPassword }` (Req 17.1).
 * - Mensajes de error descriptivos: 401 (contraseña actual incorrecta) y
 *   400/validación de cliente (incumplimiento de política).
 * - Al éxito (200): limpia el formulario y muestra confirmación. NO toca la
 *   Bandera_Cambio ni la Contraseña_Temporal (es cambio voluntario, Req 17.7).
 *
 * Nota sobre el 401: en este flujo un 401 significa "contraseña actual
 * incorrecta" (Req 17.3) y NO debe cerrar la sesión del usuario. El `api`
 * client de `lib/api.ts` dispara el interceptor global `onUnauthorized` (logout)
 * ante cualquier 401, por lo que aquí se usa `fetch` directo —replicando la
 * inyección de `Authorization` y `X-Timezone`— para manejar el 401 localmente
 * sin cerrar la sesión.
 *
 * Accesibilidad (WCAG): asociación label/input, `aria-invalid`/`aria-describedby`
 * en los campos, `role="alert"`/`aria-live` en los mensajes y áreas táctiles
 * mínimas de 44x44 px. Soporta modo claro/oscuro con la paleta existente.
 */
export function CambioVoluntarioPasswordCard() {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')

  // Controlan solo el atributo `type` de cada input (password ↔ text); el valor
  // permanece intacto en el estado al alternar la visibilidad.
  const [showCurrent, setShowCurrent] = useState(false)
  const [showNew, setShowNew] = useState(false)
  const [showConfirm, setShowConfirm] = useState(false)

  const [currentError, setCurrentError] = useState('')
  const [newError, setNewError] = useState('')
  const [confirmError, setConfirmError] = useState('')
  // Error general del envío (política del backend, 401 o error genérico).
  const [submitError, setSubmitError] = useState('')
  const [success, setSuccess] = useState(false)
  const [loading, setLoading] = useState(false)

  /** Limpia todos los campos y estados de visibilidad tras un cambio exitoso. */
  function resetForm() {
    setCurrentPassword('')
    setNewPassword('')
    setConfirmPassword('')
    setShowCurrent(false)
    setShowNew(false)
    setShowConfirm(false)
  }

  /**
   * Valida el formulario en cliente antes de enviar:
   * - La contraseña actual no puede estar vacía.
   * - La nueva contraseña debe cumplir la Politica_Password (Req 17.4/17.5).
   * - La confirmación debe coincidir con la nueva contraseña.
   */
  function validateForm(): boolean {
    let valid = true
    setCurrentError('')
    setNewError('')
    setConfirmError('')

    if (!currentPassword) {
      setCurrentError('Ingrese su contraseña actual')
      valid = false
    }

    const result = validatePassword(newPassword)
    if (!result.isValid) {
      // Mostrar el primer error de política para una guía inmediata al usuario.
      setNewError(result.firstError)
      valid = false
    }

    if (!confirmPassword) {
      setConfirmError('Confirme la nueva contraseña')
      valid = false
    } else if (confirmPassword !== newPassword) {
      setConfirmError('Las contraseñas no coinciden')
      valid = false
    }

    return valid
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setSubmitError('')
    setSuccess(false)

    if (!validateForm()) return

    setLoading(true)

    try {
      // Cabeceras equivalentes a las que inyecta el api client (JWT + zona horaria).
      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
      }
      const token = safeGetToken()
      if (token) {
        headers['Authorization'] = `Bearer ${token}`
        headers['X-Timezone'] = detectTimezone()
      }

      const response = await fetch(`${API_BASE_URL}${CHANGE_PASSWORD_VOLUNTARY_URL}`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
          currentPassword: currentPassword,
          newPassword: newPassword,
        }),
      })

      if (response.ok) {
        // Éxito (200): limpiar el formulario y mostrar confirmación.
        // No se toca la Bandera_Cambio ni la temporal (Req 17.7); el backend
        // solo actualiza el PasswordHash.
        resetForm()
        setSuccess(true)
        return
      }

      // Intentar leer el cuerpo de error `{ message }` del backend.
      let details: VoluntaryErrorDetails | undefined
      try {
        details = (await response.json()) as VoluntaryErrorDetails
      } catch {
        // El cuerpo no es JSON; se usarán mensajes por defecto.
      }

      if (response.status === 401) {
        // Contraseña actual incorrecta (Req 17.3). NO se cierra la sesión:
        // el error es del formulario, no de la sesión del usuario.
        setCurrentError('La contraseña actual es incorrecta.')
      } else if (response.status === 400) {
        // La nueva contraseña incumple la política según el backend (Req 17.5).
        setSubmitError(
          details?.message ?? 'La nueva contraseña no cumple la política de seguridad.',
        )
      } else {
        // Otro error (servidor, red, etc.).
        setSubmitError(
          details?.message ??
            'No se pudo cambiar la contraseña. Inténtelo nuevamente.',
        )
      }
    } catch {
      // Error de red u otro fallo del fetch.
      setSubmitError(
        'No se pudo cambiar la contraseña. Verifique su conexión e inténtelo nuevamente.',
      )
    } finally {
      setLoading(false)
    }
  }

  return (
    <section className="mt-8">
      <h2 className="text-lg font-semibold text-gray-900 dark:text-dark-text mb-2">
        Cambiar contraseña
      </h2>
      <p className="text-sm text-gray-500 dark:text-gray-400 mb-4">
        Actualice su contraseña proporcionando la actual y una nueva que cumpla la
        política de seguridad.
      </p>

      <div className="bg-white dark:bg-dark-surface rounded-lg shadow p-6 max-w-md">
        <form onSubmit={handleSubmit} noValidate className="space-y-5">
          {/* Contraseña actual */}
          <div>
            <label
              htmlFor="cambio-voluntario-actual"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Contraseña actual
            </label>
            <div className="relative">
              <input
                id="cambio-voluntario-actual"
                type={showCurrent ? 'text' : 'password'}
                autoComplete="current-password"
                value={currentPassword}
                onChange={e => {
                  const nuevoValor = e.target.value
                  setCurrentPassword(nuevoValor)
                  if (nuevoValor.length === 0) {
                    setShowCurrent(false)
                  }
                  setCurrentError('')
                  setSubmitError('')
                  setSuccess(false)
                }}
                aria-invalid={!!currentError}
                aria-describedby={currentError ? 'cambio-voluntario-actual-error' : undefined}
                className={`
                  w-full rounded-lg border px-3 py-2 pr-12 text-sm min-h-[44px]
                  bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                  focus:outline-none focus:ring-2 focus:ring-action-confirm
                  ${currentError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
                `}
                placeholder="••••••••"
              />
              {currentPassword.length > 0 && (
                <VisibilityToggle
                  visible={showCurrent}
                  onToggle={() => setShowCurrent(v => !v)}
                />
              )}
            </div>
            {currentError && (
              <p id="cambio-voluntario-actual-error" className="mt-1 text-xs text-action-danger" role="alert">
                {currentError}
              </p>
            )}
          </div>

          {/* Nueva contraseña */}
          <div>
            <label
              htmlFor="cambio-voluntario-nueva"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Nueva contraseña
            </label>
            <div className="relative">
              <input
                id="cambio-voluntario-nueva"
                type={showNew ? 'text' : 'password'}
                autoComplete="new-password"
                value={newPassword}
                onChange={e => {
                  const nuevoValor = e.target.value
                  setNewPassword(nuevoValor)
                  if (nuevoValor.length === 0) {
                    setShowNew(false)
                  }
                  setNewError('')
                  setSubmitError('')
                  setSuccess(false)
                }}
                aria-invalid={!!newError}
                aria-describedby={
                  newError ? 'cambio-voluntario-nueva-error' : 'cambio-voluntario-nueva-help'
                }
                className={`
                  w-full rounded-lg border px-3 py-2 pr-12 text-sm min-h-[44px]
                  bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                  focus:outline-none focus:ring-2 focus:ring-action-confirm
                  ${newError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
                `}
                placeholder="••••••••"
              />
              {newPassword.length > 0 && (
                <VisibilityToggle
                  visible={showNew}
                  onToggle={() => setShowNew(v => !v)}
                />
              )}
            </div>
            {/* Ayuda de política (visible cuando no hay error) para guiar al
                usuario y servir como descripción accesible del campo. */}
            {newError ? (
              <p id="cambio-voluntario-nueva-error" className="mt-1 text-xs text-action-danger" role="alert">
                {newError}
              </p>
            ) : (
              <p id="cambio-voluntario-nueva-help" className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                Mínimo 6 caracteres, con al menos una mayúscula, un número y uno de estos
                caracteres especiales: @, - o _.
              </p>
            )}
          </div>

          {/* Confirmar nueva contraseña */}
          <div>
            <label
              htmlFor="cambio-voluntario-confirmar"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Confirmar nueva contraseña
            </label>
            <div className="relative">
              <input
                id="cambio-voluntario-confirmar"
                type={showConfirm ? 'text' : 'password'}
                autoComplete="new-password"
                value={confirmPassword}
                onChange={e => {
                  const nuevoValor = e.target.value
                  setConfirmPassword(nuevoValor)
                  if (nuevoValor.length === 0) {
                    setShowConfirm(false)
                  }
                  setConfirmError('')
                  setSubmitError('')
                  setSuccess(false)
                }}
                aria-invalid={!!confirmError}
                aria-describedby={confirmError ? 'cambio-voluntario-confirmar-error' : undefined}
                className={`
                  w-full rounded-lg border px-3 py-2 pr-12 text-sm min-h-[44px]
                  bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                  focus:outline-none focus:ring-2 focus:ring-action-confirm
                  ${confirmError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
                `}
                placeholder="••••••••"
              />
              {confirmPassword.length > 0 && (
                <VisibilityToggle
                  visible={showConfirm}
                  onToggle={() => setShowConfirm(v => !v)}
                />
              )}
            </div>
            {confirmError && (
              <p id="cambio-voluntario-confirmar-error" className="mt-1 text-xs text-action-danger" role="alert">
                {confirmError}
              </p>
            )}
          </div>

          {/* Error general del envío (política del backend o error genérico) */}
          {submitError && (
            <div
              className="rounded-lg bg-action-danger/10 border border-action-danger/30 px-4 py-3 text-sm text-action-danger"
              role="alert"
            >
              {submitError}
            </div>
          )}

          {/* Confirmación de éxito (Req 17.6 en cliente: se limpia y confirma) */}
          {success && (
            <div
              className="rounded-lg bg-action-confirm/10 border border-action-confirm/30 px-4 py-3 text-sm text-action-confirm"
              role="status"
              aria-live="polite"
            >
              ✓ Su contraseña se actualizó correctamente.
            </div>
          )}

          <Button
            type="submit"
            variant="confirm"
            loading={loading}
            className="w-full min-h-[44px]"
          >
            Cambiar contraseña
          </Button>
        </form>
      </div>
    </section>
  )
}

/** Lee el JWT de localStorage de forma segura (localStorage puede no existir). */
function safeGetToken(): string | null {
  try {
    return localStorage.getItem(AUTH_TOKEN_KEY)
  } catch {
    return null
  }
}

/**
 * Botón para mostrar/ocultar la contraseña de un campo. `type="button"` evita
 * disparar el submit del formulario. Área táctil mínima de 44x44 px (WCAG) y
 * nombre accesible mediante texto `sr-only`.
 */
function VisibilityToggle({ visible, onToggle }: { visible: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      onClick={onToggle}
      aria-pressed={visible}
      className="absolute right-1 inset-y-0 my-auto flex items-center justify-center min-h-[44px] min-w-[44px] text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-action-confirm rounded"
    >
      <span className="sr-only">
        {visible ? 'Ocultar contraseña' : 'Mostrar contraseña'}
      </span>
      {visible ? (
        /* Ícono ojo-tachado: la contraseña está visible. */
        <svg
          className="w-5 h-5"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13.875 18.825A10.05 10.05 0 0112 19c-4.478 0-8.268-2.943-9.543-7a9.97 9.97 0 011.563-3.029m5.858.908a3 3 0 114.243 4.243M9.878 9.878l4.242 4.242M9.88 9.88l-3.29-3.29m7.532 7.532l3.29 3.29M3 3l3.59 3.59m0 0A9.953 9.953 0 0112 5c4.478 0 8.268 2.943 9.543 7a10.025 10.025 0 01-4.132 5.411m0 0L21 21" />
        </svg>
      ) : (
        /* Ícono ojo: la contraseña está oculta. */
        <svg
          className="w-5 h-5"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
        </svg>
      )}
    </button>
  )
}
