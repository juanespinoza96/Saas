import { useState } from 'react'
import { useTheme } from '../../contexts/ThemeContext'
import { useAuth } from '../../contexts/AuthContext'
import { ConfirmDialog } from '../ui/ConfirmDialog'

export function Header() {
  const { theme, toggleTheme } = useTheme()
  const { logout } = useAuth()
  const [showLogoutConfirm, setShowLogoutConfirm] = useState(false)

  return (
    <header className="h-16 border-b border-gray-200 dark:border-dark-surface flex items-center justify-between px-6 bg-white dark:bg-dark-bg">
      <h1 className="text-lg font-semibold text-gray-900 dark:text-dark-text">
        SaaS POS Admin
      </h1>

      <div className="flex items-center gap-4">
        {/* Theme toggle (Requirement 21.6) */}
        <button
          onClick={toggleTheme}
          className="p-2 rounded-lg hover:bg-gray-100 dark:hover:bg-dark-surface transition-colors"
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

        {/* Logout with confirmation */}
        <button
          onClick={() => setShowLogoutConfirm(true)}
          className="px-3 py-1.5 text-sm font-medium text-gray-600 dark:text-dark-text hover:text-action-danger transition-colors"
        >
          Cerrar Sesión
        </button>
      </div>

      <ConfirmDialog
        isOpen={showLogoutConfirm}
        onClose={() => setShowLogoutConfirm(false)}
        onConfirm={logout}
        title="Cerrar Sesión"
        message="¿Estás seguro de que deseas cerrar la sesión? Tendrás que volver a iniciar sesión para acceder al panel."
        confirmLabel="Cerrar Sesión"
        cancelLabel="Cancelar"
        variant="danger"
      />
    </header>
  )
}
