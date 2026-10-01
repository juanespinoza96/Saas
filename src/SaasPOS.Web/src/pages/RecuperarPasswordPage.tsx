import { useState, useCallback, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useApiRequest } from '../hooks/useApiRequest'
import { api } from '../lib/api'
import { Button } from '../components/ui/Button'

/**
 * Password recovery page with silent flow.
 * - Always shows the same success message regardless of response (Req 3.7)
 * - Recovery requires Gerente approval on the backend (Req 3.6)
 * - Inline validation (Req 21.13)
 * - Button disabled during request (Req 21.14)
 */
export function RecuperarPasswordPage() {
  const [email, setEmail] = useState('')
  const [emailError, setEmailError] = useState('')
  const [submitted, setSubmitted] = useState(false)

  const { loading, execute } = useApiRequest<void>(
    useCallback(
      () => api.post<void>('/api/tenants/auth/password-recovery/request', { email }),
      [email],
    ),
  )

  function validateForm(): boolean {
    setEmailError('')

    if (!email.trim()) {
      setEmailError('El correo es obligatorio')
      return false
    }

    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
      setEmailError('Ingrese un correo válido')
      return false
    }

    return true
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()

    if (!validateForm()) return

    // Fire and forget — always show success message (Req 3.7)
    await execute()
    setSubmitted(true)
  }

  // After submission, always show the same message regardless of whether the email exists
  if (submitted) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-light-bg dark:bg-dark-bg px-4">
        <div className="bg-white dark:bg-dark-surface rounded-xl shadow-lg p-8 w-full max-w-md text-center">
          <div className="mx-auto mb-4 w-12 h-12 rounded-full bg-action-confirm/10 flex items-center justify-center">
            <svg
              className="w-6 h-6 text-action-confirm"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M5 13l4 4L19 7"
              />
            </svg>
          </div>
          <h1 className="text-xl font-bold text-gray-900 dark:text-dark-text mb-2">
            Solicitud Enviada
          </h1>
          <p className="text-sm text-gray-600 dark:text-gray-400 mb-6">
            Si el correo está registrado, se enviará la solicitud de recuperación.
            Un gerente revisará y aprobará el restablecimiento de contraseña.
          </p>
          <Link
            to="/login"
            className="text-sm text-action-confirm hover:underline"
          >
            Volver al inicio de sesión
          </Link>
        </div>
      </div>
    )
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-light-bg dark:bg-dark-bg px-4">
      <div className="bg-white dark:bg-dark-surface rounded-xl shadow-lg p-8 w-full max-w-md">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text mb-2 text-center">
          Recuperar Contraseña
        </h1>
        <p className="text-sm text-gray-500 dark:text-gray-400 text-center mb-6">
          Ingresa tu correo electrónico y enviaremos una solicitud de recuperación.
        </p>

        <form onSubmit={handleSubmit} noValidate className="space-y-5">
          {/* Email field */}
          <div>
            <label
              htmlFor="recovery-email"
              className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
            >
              Correo electrónico
            </label>
            <input
              id="recovery-email"
              type="email"
              autoComplete="email"
              value={email}
              onChange={e => {
                setEmail(e.target.value)
                setEmailError('')
              }}
              aria-invalid={!!emailError}
              aria-describedby={emailError ? 'recovery-email-error' : undefined}
              className={`
                w-full rounded-lg border px-3 py-2 text-sm
                bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                focus:outline-none focus:ring-2 focus:ring-action-confirm
                ${emailError ? 'border-action-danger' : 'border-gray-300 dark:border-gray-600'}
              `}
              placeholder="correo@ejemplo.com"
            />
            {emailError && (
              <p id="recovery-email-error" className="mt-1 text-xs text-action-danger" role="alert">
                {emailError}
              </p>
            )}
          </div>

          {/* Submit button (Req 21.14) */}
          <Button
            type="submit"
            variant="confirm"
            loading={loading}
            className="w-full"
          >
            Enviar Solicitud
          </Button>
        </form>

        <div className="mt-4 text-center">
          <Link
            to="/login"
            className="text-sm text-action-confirm hover:underline"
          >
            Volver al inicio de sesión
          </Link>
        </div>
      </div>
    </div>
  )
}
