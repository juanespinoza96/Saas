import { useState, type FormEvent } from 'react'
import { Navigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import { useTheme } from '../contexts/ThemeContext'
import { Button } from '../components/ui/Button'

export function LoginPage() {
  const { isAuthenticated, login } = useAuth()
  const { theme, toggleTheme } = useTheme()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  // Estado de visibilidad de la contraseña. Controla solo el atributo `type`
  // del input (password ↔ text); el valor sigue en el estado `password` y
  // nunca se modifica al alternar. Reemplaza la dependencia del control nativo
  // del navegador (p. ej. `::-ms-reveal`), ausente en la mayoría de móviles.
  const [showPassword, setShowPassword] = useState(false)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  if (isAuthenticated) {
    return <Navigate to="/" replace />
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)

    try {
      const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5291'
      const response = await fetch(`${apiBaseUrl}/api/tenants/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })

      if (!response.ok) {
        throw new Error('Credenciales inválidas')
      }

      const data = await response.json() as { token: string }
      login(data.token)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Error al iniciar sesión')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-light-bg dark:bg-dark-bg px-4">
      {/* Theme toggle */}
      <button
        onClick={toggleTheme}
        className="absolute top-4 right-4 p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-dark-surface transition-colors"
        aria-label={theme === 'light' ? 'Activar modo oscuro' : 'Activar modo claro'}
      >
        {theme === 'light' ? (
          <svg className="w-5 h-5 text-gray-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
          </svg>
        ) : (
          <svg className="w-5 h-5 text-dark-text" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
          </svg>
        )}
      </button>

      <div className="w-full max-w-md">
        <div className="bg-white dark:bg-dark-surface rounded-xl shadow-lg p-8">
          <div className="text-center mb-8">
            <h1 className="text-2xl font-bold text-gray-900 dark:text-dark-text">
              SaaS POS Admin
            </h1>
            <p className="mt-2 text-sm text-gray-500 dark:text-dark-text/70">
              Panel de SuperAdministración
            </p>
          </div>

          <form onSubmit={handleSubmit} className="space-y-4">
            {error && (
              <div className="p-3 rounded-lg bg-action-danger/10 text-action-danger text-sm">
                {error}
              </div>
            )}

            <div>
              <label htmlFor="email" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
                Correo electrónico
              </label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                className="w-full px-3 py-2 border border-gray-300 dark:border-dark-surface rounded-lg
                           bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                           focus:ring-2 focus:ring-action-confirm focus:border-transparent outline-none"
                placeholder="admin@saas.com"
              />
            </div>

            <div>
              <label htmlFor="password" className="block text-sm font-medium text-gray-700 dark:text-dark-text/80 mb-1">
                Contraseña
              </label>
              {/* Contenedor relative: aloja el input y el botón de toggle
                  posicionado en el borde derecho. El input reserva padding
                  derecho (pr-10) para que el texto no quede bajo el botón. */}
              <div className="relative">
                <input
                  id="password"
                  /* El toggle solo cambia el atributo `type`, nunca el valor. */
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={(e) => {
                    const nuevoValor = e.target.value
                    setPassword(nuevoValor)
                    // Si el campo queda sin caracteres, forzar el estado a
                    // oculto. Así el botón (que solo se muestra con contenido)
                    // no deja un estado "visible" colgado al volver a escribir.
                    if (nuevoValor.length === 0) {
                      setShowPassword(false)
                    }
                  }}
                  required
                  className="w-full px-3 py-2 pr-10 border border-gray-300 dark:border-dark-surface rounded-lg
                             bg-white dark:bg-dark-bg text-gray-900 dark:text-dark-text
                             focus:ring-2 focus:ring-action-confirm focus:border-transparent outline-none"
                  placeholder="••••••••"
                />
                {/* Toggle propio de mostrar/ocultar contraseña. `type="button"`
                    evita que dispare el submit del formulario. Es un <button>
                    nativo, por lo que ya es enfocable y activable con
                    Enter/Espacio. `aria-label` dinámico y `aria-pressed`
                    reflejan el estado para lectores de pantalla.
                    Solo se renderiza cuando el campo tiene al menos un carácter:
                    así el usuario no puede activar la visibilidad sobre un campo
                    vacío (lo que dejaría un estado incoherente con el ícono). */}
                {password.length > 0 && (
                <button
                  type="button"
                  onClick={() => setShowPassword((v) => !v)}
                  aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'}
                  aria-pressed={showPassword}
                  className="absolute right-2 inset-y-0 flex items-center text-gray-500 hover:text-gray-700 dark:text-dark-text/70 dark:hover:text-dark-text focus:outline-none focus:ring-2 focus:ring-action-confirm rounded"
                >
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
            </div>

            <Button
              type="submit"
              variant="confirm"
              disabled={loading}
              className="w-full"
            >
              {loading ? 'Ingresando...' : 'Ingresar'}
            </Button>
          </form>
        </div>
      </div>
    </div>
  )
}
