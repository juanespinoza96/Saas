import { useState, useEffect, useRef, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { jwtDecode } from 'jwt-decode'
import { useAuth } from '../contexts/AuthContext'
import { useLoginLockout } from '../hooks'
import { api, type ApiError } from '../lib/api'
import { Button } from '../components/ui/Button'
import type { JwtClaims } from '../types/auth'

interface LoginResponse {
  token: string
}

/** Forma del body cuando el backend retorna 401 con info de intentos */
interface LoginFailedDetails {
  message: string
  remainingAttempts: number
  lockoutSeconds: number
}

/** Forma del body cuando el backend retorna 429 por rate limiting en login */
interface RateLimitDetails {
  error: string
  code: string
  retryAfterSeconds: number
}

/**
 * Página de login con email/contraseña.
 * - Mensaje genérico "Credenciales inválidas" en fallos (Req 3.2)
 * - Validación inline por campo (Req 21.13)
 * - Botón deshabilitado con indicador de carga durante request (Req 21.14)
 * - Integración con bloqueo por rate limiting (Req 9.1–9.6)
 */
export function LoginPage() {
  const navigate = useNavigate()
  const { login } = useAuth()
  const { isLocked, remainingTime, setLockout } = useLoginLockout()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  // Estado de visibilidad de la contraseña. Controla solo el atributo `type`
  // del input (password ↔ text); el valor sigue en el estado `password` y
  // nunca se modifica al alternar. Reemplaza la dependencia del control nativo
  // del navegador (p. ej. `::-ms-reveal`), ausente en la mayoría de móviles.
  const [showPassword, setShowPassword] = useState(false)
  const [emailError, setEmailError] = useState('')
  const [passwordError, setPasswordError] = useState('')
  const [authError, setAuthError] = useState('')
  const [loading, setLoading] = useState(false)

  // Estado para intentos restantes (Req 9.1)
  const [remainingAttempts, setRemainingAttempts] = useState<number | null>(null)

  // Mensaje mostrado cuando el lockout expira (Req 9.5)
  const [unlockMessage, setUnlockMessage] = useState('')

  // Referencia al estado previo de isLocked para detectar la transición a desbloqueado
  const wasLockedRef = useRef(false)

  // Detectar cuando el lockout expira para mostrar mensaje de desbloqueo (Req 9.5)
  useEffect(() => {
    if (wasLockedRef.current && !isLocked) {
      // El timer llegó a 00:00: mostrar mensaje de desbloqueo
      setUnlockMessage('Ya puede intentar ingresar nuevamente.')
      setRemainingAttempts(null)
      setAuthError('')
    }
    wasLockedRef.current = isLocked
  }, [isLocked])

  function validateForm(): boolean {
    let valid = true
    setEmailError('')
    setPasswordError('')

    if (!email.trim()) {
      setEmailError('El correo es obligatorio')
      valid = false
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      setEmailError('Ingrese un correo válido')
      valid = false
    }

    if (!password) {
      setPasswordError('La contraseña es obligatoria')
      valid = false
    }

    return valid
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setAuthError('')
    setUnlockMessage('')
    setRemainingAttempts(null)

    // Protección contra manipulación DOM: bloquear submit si isLocked (Req 9.6)
    if (isLocked) return

    if (!validateForm()) return

    setLoading(true)

    try {
      const result = await api.post<LoginResponse>('/api/tenants/auth/login', { email, password })
      if (result?.token) {
        login(result.token)
        // Bifurcar la navegación según el claim de cambio de contraseña (Req 8.3, 9.1).
        // `login(token)` actualiza el contexto de forma asíncrona (setState), por lo que
        // `mustChangePassword` del hook aún no reflejaría el nuevo token dentro de este
        // handler. Por eso decodificamos el claim directamente del token recién recibido.
        let debeCambiarPassword = false
        try {
          const claims = jwtDecode<JwtClaims>(result.token)
          debeCambiarPassword = claims.must_change_password === true
        } catch {
          // Si la decodificación falla, se usa el destino por defecto (/dashboard)
          debeCambiarPassword = false
        }
        navigate(debeCambiarPassword ? '/cambiar-password' : '/dashboard', { replace: true })
      }
    } catch (err: unknown) {
      const apiError = err as ApiError

      if (apiError?.status === 429) {
        // Rate limit excedido: activar bloqueo (Req 9.2, 9.3)
        const details = apiError.details as RateLimitDetails | undefined
        const retryAfterSeconds = details?.retryAfterSeconds
        if (retryAfterSeconds && retryAfterSeconds > 0) {
          setLockout(retryAfterSeconds)
        }
        setRemainingAttempts(null)
        setAuthError('')
      } else if (apiError?.status === 403) {
        // Usuario inactivo: mostrar mensaje específico
        const details = apiError.details as { message?: string; code?: string } | undefined
        if (details?.code === 'USER_INACTIVE') {
          setAuthError(details.message ?? 'Su cuenta se encuentra inactiva. Contacte al administrador de su comercio.')
          setRemainingAttempts(null)
        } else {
          setAuthError('Credenciales inválidas')
        }
      } else if (apiError?.status === 401) {
        // Credenciales inválidas con información de intentos restantes (Req 9.1)
        const details = apiError.details as LoginFailedDetails | undefined
        if (details?.remainingAttempts !== undefined && details.remainingAttempts !== null) {
          setRemainingAttempts(details.remainingAttempts)
        }
        setAuthError('Credenciales inválidas')
      } else {
        // Error genérico (Req 3.2)
        setAuthError('Credenciales inválidas')
      }
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-light-bg dark:bg-dark-bg px-4">
      <div className="bg-white dark:bg-dark-surface rounded-xl shadow-lg p-8 w-full max-w-md">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-6 text-center">
          Iniciar Sesión
        </h1>

        {/* Estado de bloqueo: mostrar temporizador y ocultar formulario (Req 9.2) */}
        {isLocked && (
          <div
            className="rounded-lg bg-action-danger/10 border border-action-danger/30 px-4 py-5 text-center mb-4"
            role="alert"
            aria-live="assertive"
          >
            <p className="text-sm font-medium text-action-danger mb-2">
              Cuenta bloqueada temporalmente
            </p>
            <p className="text-2xl font-mono font-bold text-action-danger" aria-label={`Tiempo restante: ${remainingTime}`}>
              {remainingTime}
            </p>
            <p className="text-xs text-gray-600 dark:text-gray-400 mt-2">
              Demasiados intentos fallidos. Espere a que termine el temporizador.
            </p>
          </div>
        )}

        {/* Mensaje cuando el lockout expira (Req 9.5) */}
        {!isLocked && unlockMessage && (
          <div
            className="rounded-lg bg-green-50 dark:bg-green-900/20 border border-green-300 dark:border-green-700 px-4 py-3 text-sm text-green-700 dark:text-green-400 mb-4"
            role="status"
            aria-live="polite"
          >
            {unlockMessage}
          </div>
        )}

        {/* Formulario oculto cuando está bloqueado (Req 9.2) */}
        {!isLocked && (
          <form onSubmit={handleSubmit} noValidate className="space-y-5">
            {/* Campo email */}
            <div>
              <label
                htmlFor="login-email"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Correo electrónico
              </label>
              <input
                id="login-email"
                type="email"
                autoComplete="email"
                value={email}
                onChange={e => {
                  setEmail(e.target.value)
                  setEmailError('')
                  setAuthError('')
                  setUnlockMessage('')
                }}
                aria-invalid={!!emailError}
                aria-describedby={emailError ? 'login-email-error' : undefined}
                className={`
                  w-full rounded-lg border px-3 py-2 text-sm
                  bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                  focus:outline-none focus:ring-2 focus:ring-action-confirm
                  ${emailError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
                `}
                placeholder="correo@ejemplo.com"
              />
              {emailError && (
                <p id="login-email-error" className="mt-1 text-xs text-action-danger" role="alert">
                  {emailError}
                </p>
              )}
            </div>

            {/* Campo contraseña */}
            <div>
              <label
                htmlFor="login-password"
                className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
              >
                Contraseña
              </label>
              {/* Contenedor relative: aloja el input y el botón de toggle
                  posicionado en el borde derecho. El input reserva padding
                  derecho (pr-10) para que el texto no quede bajo el botón. */}
              <div className="relative">
                <input
                  id="login-password"
                  /* El toggle solo cambia el atributo `type`, nunca el valor. */
                  type={showPassword ? 'text' : 'password'}
                  autoComplete="current-password"
                  value={password}
                  onChange={e => {
                    const nuevoValor = e.target.value
                    setPassword(nuevoValor)
                    // Si el campo queda sin caracteres, forzar el estado a
                    // oculto. Así el botón (que solo se muestra con contenido)
                    // no deja un estado "visible" colgado al volver a escribir.
                    if (nuevoValor.length === 0) {
                      setShowPassword(false)
                    }
                    setPasswordError('')
                    setAuthError('')
                    setUnlockMessage('')
                  }}
                  aria-invalid={!!passwordError}
                  aria-describedby={passwordError ? 'login-password-error' : undefined}
                  className={`
                    w-full rounded-lg border px-3 py-2 pr-10 text-sm
                    bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                    focus:outline-none focus:ring-2 focus:ring-action-confirm
                    ${passwordError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
                  `}
                  placeholder="••••••••"
                />
                {/* Toggle propio de mostrar/ocultar contraseña. `type="button"`
                    evita que dispare el submit del formulario. Es un <button>
                    nativo, por lo que ya es enfocable y activable con
                    Enter/Espacio. El nombre accesible se provee mediante un
                    texto oculto visualmente (`sr-only`) en lugar de `aria-label`;
                    así el control queda descrito para lectores de pantalla sin
                    que su nombre colisione con el del campo de contraseña.
                    `aria-pressed` refleja el estado (visible u oculto).
                    Solo se renderiza cuando el campo tiene al menos un carácter:
                    así el usuario no puede activar la visibilidad sobre un campo
                    vacío (lo que dejaría un estado incoherente con el ícono). */}
                {password.length > 0 && (
                <button
                  type="button"
                  onClick={() => setShowPassword(v => !v)}
                  aria-pressed={showPassword}
                  className="absolute right-2 inset-y-0 flex items-center text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 focus:outline-none focus:ring-2 focus:ring-action-confirm rounded"
                >
                  {/* Nombre accesible dinámico del botón (oculto visualmente). */}
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
              {passwordError && (
                <p id="login-password-error" className="mt-1 text-xs text-action-danger" role="alert">
                  {passwordError}
                </p>
              )}
            </div>

            {/* Error de autenticación — mensaje genérico (Req 3.2) */}
            {authError && (
              <div
                className="rounded-lg bg-action-danger/10 border border-action-danger/30 px-4 py-3 text-sm text-action-danger"
                role="alert"
              >
                {authError}
              </div>
            )}

            {/* Mensaje de intentos restantes en texto rojo (Req 9.1) */}
            {remainingAttempts !== null && (
              <div
                className="text-sm text-action-danger font-medium"
                role="alert"
                aria-live="assertive"
              >
                Le quedan {remainingAttempts} intento(s). Luego deberá esperar 30 minutos para volver a ingresar.
              </div>
            )}

            {/* Botón de submit con loading (Req 21.14), deshabilitado si bloqueado (Req 9.6) */}
            <Button
              type="submit"
              variant="confirm"
              loading={loading}
              disabled={isLocked}
              className="w-full"
            >
              Ingresar
            </Button>
          </form>
        )}

        {/* Link a recuperación de contraseña */}
        <div className="mt-4 text-center">
          <Link
            to="/recuperar-password"
            className="text-sm text-action-confirm hover:underline"
          >
            ¿Olvidaste tu contraseña?
          </Link>
        </div>
      </div>
    </div>
  )
}
