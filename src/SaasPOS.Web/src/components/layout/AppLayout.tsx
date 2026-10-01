import { useState, useCallback } from 'react'
import { Outlet } from 'react-router-dom'
import { Sidebar } from './Sidebar'
import { Header } from './Header'

/**
 * Layout principal de la aplicación con sidebar fijo en desktop y menú hamburguesa en móvil.
 * - >= 768px: sidebar fijo visible + contenido con margen izquierdo
 * - < 768px: sidebar oculto, botón hamburguesa en header, sidebar como drawer/overlay
 * Req 5.5: menú hamburguesa para pantallas < 768px
 */
export function AppLayout() {
  // Estado para controlar la visibilidad del menú móvil (Req 5.5)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)

  const openMobileMenu = useCallback(() => setMobileMenuOpen(true), [])
  const closeMobileMenu = useCallback(() => setMobileMenuOpen(false), [])

  return (
    <div className="min-h-screen bg-light-bg dark:bg-dark-bg">
      {/* Sidebar fijo visible solo en >= 768px (md:block) */}
      <div className="hidden md:block fixed left-0 top-0 h-full z-30">
        <Sidebar />
      </div>

      {/* Overlay/backdrop del menú móvil (solo visible cuando está abierto en < 768px) */}
      {mobileMenuOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/50 md:hidden"
          onClick={closeMobileMenu}
          aria-hidden="true"
        />
      )}

      {/* Drawer del sidebar para móvil (< 768px) */}
      <div
        className={`fixed inset-y-0 left-0 z-50 w-64 transform transition-transform duration-300 ease-in-out md:hidden ${
          mobileMenuOpen ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <Sidebar />
        {/* Botón de cerrar dentro del drawer móvil */}
        <button
          onClick={closeMobileMenu}
          className="absolute top-4 right-4 p-2 rounded-lg text-gray-500 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-action-confirm"
          aria-label="Cerrar menú de navegación"
        >
          <svg
            className="w-5 h-5"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            aria-hidden="true"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M6 18L18 6M6 6l12 12"
            />
          </svg>
        </button>
      </div>

      {/* Contenido principal: margen izquierdo solo en >= 768px */}
      <div className="md:ml-64 flex flex-col min-h-screen">
        {/* Header con botón hamburguesa en móvil */}
        <header
          className="h-16 bg-white dark:bg-dark-surface border-b border-gray-200 dark:border-gray-700 flex items-center px-4 md:px-6"
          role="banner"
        >
          {/* Botón hamburguesa visible solo en < 768px (Req 5.5) */}
          <button
            onClick={openMobileMenu}
            className="p-2 mr-3 rounded-lg text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-action-confirm md:hidden"
            aria-label="Abrir menú de navegación"
          >
            <svg
              className="w-6 h-6"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              aria-hidden="true"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M4 6h16M4 12h16M4 18h16"
              />
            </svg>
          </button>

          {/* Contenido del header (delegado al componente Header) */}
          <div className="flex-1">
            <Header />
          </div>
        </header>

        <main
          className="flex-1 p-4 md:p-6"
          role="main"
          aria-label="Contenido principal"
        >
          <Outlet />
        </main>
      </div>
    </div>
  )
}
