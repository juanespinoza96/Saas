import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { api, type ApiError } from '../lib/api'
import { validatePassword } from '../lib/passwordPolicy'
import { Button } from '../components/ui/Button'

/**
 * Cuerpo de la respuesta de error del backend para change-password.
 * El controlador retorna `{ message }` tanto en 400 (política) como en otros errores.
 */
interface ChangePasswordErrorDetails {
  message?: string
}

/**
 * Pantalla de cambio de contraseña obligatorio (`/cambiar-password`).
 *
 * Se muestra cuando el usuario inició sesión con una Contraseña_Temporal y su
 * Claim_Cambio (`must_change_password`) está activo. El flujo:
 * - Valida la nueva contraseña contra la Politica_Password en el cliente usando
 *   el espejo `validatePassword` (Req 11.1–11.6). El backend sigue siendo la
 *   autoridad final.
 * - Envía la nueva contraseña a `POST /api/tenants/auth/change-password`
 *   (Authorize; el userId sale del claim del JWT, no del cuerpo) (Req 10.1).
 * - Al éxito, apaga el flag de cambio en el AuthContext para liberar la
 *   navegación (Req 9.3) y redirige al dashboard.
 * - Muestra mensajes de error descriptivos para política incumplida (400) y
 *   para errores de red/genéricos.
 *
 * Accesibilidad (WCAG): áreas táctiles mínimas de 44x44 px en los controles
 * interactivos, asociación label/input, `aria-invalid`/`aria-describedby` en los
 * campos y `role="alert"` en los mensajes de error.
 */
export function CambiarPasswordPage() {
  const navigate = useNavigate()
  const { clearMustChangePassword } = useAuth()

  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  // Controla solo el atributo `type` del input (password ↔ text); el valor
  // permanece intacto en el estado al alternar la visibilidad.
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirm, setShowConfirm] = useState(false)

  const [passwordError, setPasswordError] = useState('')
  const [confirmError, setConfirmError] = useState('')
  // Error general del envío (política reportada por el backend, red o genérico).
  const [submitError, setSubmitError] = useState('')
  const [loading, setLoading] = useState(false)

  /**
   * Valida el formulario en cliente antes de enviar:
   * - La nueva contraseña debe cumplir la Politica_Password (Req 11).
   * - La confirmación debe coincidir con la nueva contraseña.
   */
  function validateForm(): boolean {
    let valid = true
    setPasswordError('')
    setConfirmError('')

    const result = validatePassword(password)
    if (!result.isValid) {
      // Mostrar el primer error de política para una guía inmediata al usuario.
      setPasswordError(result.firstError)
      valid = false
    }

    if (!confirmPassword) {
      setConfirmError('Confirme la nueva contraseña')
      valid = false
    } else if (confirmPassword !== password) {
      setConfirmError('Las contraseñas no coinciden')
      valid = false
    }

    return valid
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setSubmitError('')

    if (!validateForm()) return

    setLoading(true)

    try {
      // El backend toma el userId del claim `sub`; solo se envía la nueva contraseña.
      await api.post<{ message: string }>('/api/tenants/auth/change-password', {
        NewPassword: password,
      })

      // Éxito: apagar el flag de cambio en memoria para que el Guard libere la
      // navegación (Req 9.3) y dirigir al usuario al dashboard.
      clearMustChangePassword()
      navigate('/dashboard', { replace: true })
    } catch (err: unknown) {
      const apiError = err as ApiError
      const details = apiError?.details as ChangePasswordErrorDetails | undefined

      if (apiError?.status === 400) {
        // La nueva contraseña incumple la política según el backend (Req 11.1).
        setSubmitError(
          details?.message ?? 'La nueva contraseña no cumple la política de seguridad.',
        )
      } else if (apiError?.status === 401) {
        // El token expiró o fue invalidado; el interceptor global cerrará la sesión.
        setSubmitError('Su sesión expiró. Vuelva a iniciar sesión para continuar.')
      } else {
        // Error de red u otro error genérico.
        setSubmitError(
          details?.message ??
            'No se pudo cambiar la contraseña. Verifique su conexión e inténtelo nuevamente.',
        )
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-light-bg dark:bg-dark-bg px-4">
      <div className="bg-white dark:bg-dark-surface rounded-xl shadow-lg p-8 w-full max-w-md">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-2 text-center">
          Cambiar Contraseña
        </h1>
        <p className="text-sm text-gray-500 dark:text-gray-400 text-center mb-6">
          Debe establecer una nueva contraseña para continuar. Elija una que cumpla
          la política de seguridad.
        </p>

        <form onSubmit={handleSubmit} noValidate className="space-y-5">
          {/* Campo nueva contraseña */}
          <div>
            <label
              htmlFor="cambiar-password"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Nueva contraseña
            </label>
            {/* Contenedor relative: aloja el input y el botón de mostrar/ocultar
                posicionado en el borde derecho. El input reserva padding derecho
                (pr-12) para que el texto no quede bajo el botón. */}
            <div className="relative">
              <input
                id="cambiar-password"
                type={showPassword ? 'text' : 'password'}
                autoComplete="new-password"
                value={password}
                onChange={e => {
                  const nuevoValor = e.target.value
                  setPassword(nuevoValor)
                  // Al vaciar el campo, forzar el estado a oculto para no dejar
                  // un estado "visible" colgado al volver a escribir.
                  if (nuevoValor.length === 0) {
                    setShowPassword(false)
                  }
                  setPasswordError('')
                  setSubmitError('')
                }}
                aria-invalid={!!passwordError}
                aria-describedby={
                  passwordError ? 'cambiar-password-error' : 'cambiar-password-help'
                }
                className={`
                  w-full rounded-lg border px-3 py-2 pr-12 text-sm min-h-[44px]
                  bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                  focus:outline-none focus:ring-2 focus:ring-action-confirm
                  ${passwordError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
                `}
                placeholder="••••••••"
              />
              {/* Toggle mostrar/ocultar. `type="button"` evita disparar el submit.
                  Área táctil mínima de 44x44 px (min-h/min-w) para cumplir WCAG.
                  El nombre accesible se provee con texto `sr-only`. */}
              {password.length > 0 && (
                <button
                  type="button"
                  onClick={() => setShowPassword(v => !v)}
                  aria-pressed={showPassword}
                  className="absolute right-1 inset-y-0 my-auto flex items-center justify-center min-h-[44px] min-w-[44px] text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-action-confirm rounded"
                >
                  <span className="sr-only">
                    {showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'}
                  </span>
                  {showPassword ? (
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
              )}
            </div>
            {/* Ayuda de política (siempre visible cuando no hay error) para guiar
                al usuario y servir como descripción accesible del campo. */}
            {passwordError ? (
              <p id="cambiar-password-error" className="mt-1 text-xs text-action-danger" role="alert">
                {passwordError}
              </p>
            ) : (
              <p id="cambiar-password-help" className="mt-1 text-xs text-gray-500 dark:text-gray-400">
                Mínimo 6 caracteres, con al menos una mayúscula, un número y uno de estos
                caracteres especiales: @, - o _.
              </p>
            )}
          </div>

          {/* Campo confirmar contraseña */}
          <div>
            <label
              htmlFor="cambiar-password-confirm"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Confirmar contraseña
            </label>
            <div className="relative">
              <input
                id="cambiar-password-confirm"
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
                }}
                aria-invalid={!!confirmError}
                aria-describedby={confirmError ? 'cambiar-password-confirm-error' : undefined}
                className={`
                  w-full rounded-lg border px-3 py-2 pr-12 text-sm min-h-[44px]
                  bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                  focus:outline-none focus:ring-2 focus:ring-action-confirm
                  ${confirmError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
                `}
                placeholder="••••••••"
              />
              {confirmPassword.length > 0 && (
                <button
                  type="button"
                  onClick={() => setShowConfirm(v => !v)}
                  aria-pressed={showConfirm}
                  className="absolute right-1 inset-y-0 my-auto flex items-center justify-center min-h-[44px] min-w-[44px] text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-action-confirm rounded"
                >
                  <span className="sr-only">
                    {showConfirm ? 'Ocultar contraseña' : 'Mostrar contraseña'}
                  </span>
                  {showConfirm ? (
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
              )}
            </div>
            {confirmError && (
              <p id="cambiar-password-confirm-error" className="mt-1 text-xs text-action-danger" role="alert">
                {confirmError}
              </p>
            )}
          </div>

          {/* Error general del envío (política del backend, red o genérico) */}
          {submitError && (
            <div
              className="rounded-lg bg-action-danger/10 border border-action-danger/30 px-4 py-3 text-sm text-action-danger"
              role="alert"
            >
              {submitError}
            </div>
          )}

          {/* Botón de submit con estado de carga. El componente Button ya expone
              padding suficiente; se refuerza la altura táctil mínima (44px). */}
          <Button
            type="submit"
            variant="confirm"
            loading={loading}
            className="w-full min-h-[44px]"
          >
            Guardar contraseña
          </Button>
        </form>
      </div>
    </div>
  )
}
