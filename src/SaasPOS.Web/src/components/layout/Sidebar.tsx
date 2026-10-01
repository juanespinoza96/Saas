import { useState } from 'react'
import { NavLink } from 'react-router-dom'
import { useAuth } from '../../contexts/AuthContext'
import { useTheme } from '../../contexts/ThemeContext'
import { navItems } from '../../config/navigation'
import { SidebarIcon } from './SidebarIcon'
import { ConfirmDialog } from '../ui/ConfirmDialog'

/**
 * Vertical fixed sidebar with role-based navigation filtering (Req 21.7, 21.8).
 * Only shows sections the current user's role is allowed to access.
 */
export function Sidebar() {
  const { claims, logout } = useAuth()
  const { theme, toggleTheme } = useTheme()
  const [showLogoutConfirm, setShowLogoutConfirm] = useState(false)

  const userRole = claims?.role

  // Filter nav items based on user role (Req 21.8)
  const visibleItems = navItems.filter(item => {
    if (item.allowedRoles.length === 0) return true
    if (!userRole) return false
    return item.allowedRoles.includes(userRole)
  })

  return (
    <aside
      className="h-full w-64 bg-white dark:bg-dark-surface border-r border-gray-200 dark:border-gray-700 flex flex-col"
      aria-label="Navegación principal"
      role="navigation"
    >
      {/* Logo / Brand area */}
      <div className="p-4 border-b border-gray-200 dark:border-gray-700">
        <h1 className="text-lg font-bold text-gray-900 dark:text-dark-text">
          SaaS POS
        </h1>
      </div>

      {/* Navigation items */}
      <nav className="flex-1 overflow-y-auto py-4 px-3" aria-label="Menú del POS">
        <ul className="space-y-1" role="list">
          {visibleItems.map(item => (
            <li key={item.id}>
              <NavLink
                to={item.path}
                className={({ isActive }) =>
                  `sidebar-link ${isActive ? 'active' : 'text-gray-700 dark:text-gray-300'}`
                }
                aria-label={item.label}
              >
                <SidebarIcon name={item.icon} />
                <span>{item.label}</span>
              </NavLink>
            </li>
          ))}
        </ul>
      </nav>

      {/* Bottom actions: theme toggle + logout */}
      <div className="p-3 border-t border-gray-200 dark:border-gray-700 space-y-1">
        <button
          onClick={toggleTheme}
          className="sidebar-link w-full text-gray-700 dark:text-gray-300"
          aria-label={theme === 'light' ? 'Cambiar a modo oscuro' : 'Cambiar a modo claro'}
        >
          <SidebarIcon name={theme === 'light' ? 'moon' : 'sun'} />
          <span>{theme === 'light' ? 'Modo Oscuro' : 'Modo Claro'}</span>
        </button>
        <button
          onClick={() => setShowLogoutConfirm(true)}
          className="sidebar-link w-full text-gray-700 dark:text-gray-300"
          aria-label="Cerrar sesión"
        >
          <SidebarIcon name="log-out" />
          <span>Cerrar Sesión</span>
        </button>
      </div>

      <ConfirmDialog
        open={showLogoutConfirm}
        onClose={() => setShowLogoutConfirm(false)}
        onConfirm={logout}
        title="Cerrar Sesión"
        confirmLabel="Cerrar Sesión"
        cancelLabel="Cancelar"
        confirmVariant="danger"
      >
        ¿Estás seguro de que deseas cerrar la sesión? Tendrás que volver a iniciar sesión para acceder al sistema.
      </ConfirmDialog>
    </aside>
  )
}
