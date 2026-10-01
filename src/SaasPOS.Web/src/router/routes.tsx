import { Routes, Route, Navigate } from 'react-router-dom'
import { AppLayout } from '../components/layout/AppLayout'
import { ProtectedRoute } from './ProtectedRoute'
import { RequirePasswordChangeGuard } from './RequirePasswordChangeGuard'

// Pages
import { LoginPage } from '../pages/LoginPage'
import { RecuperarPasswordPage } from '../pages/RecuperarPasswordPage'
import { CambiarPasswordPage } from '../pages/CambiarPasswordPage'
import { DashboardPage } from '../pages/DashboardPage'
import { PosNormalPage } from '../pages/PosNormalPage'
import { PosBarPage } from '../pages/PosBarPage'
import { ProductosPage } from '../pages/ProductosPage'
import { CategoriasPage } from '../pages/CategoriasPage'
import { InventarioPage } from '../pages/InventarioPage'
import { ClientesPage } from '../pages/ClientesPage'
import { ReportesPage } from '../pages/ReportesPage'
import { NotificacionesPage } from '../pages/NotificacionesPage'
import { UsuariosPage } from '../pages/UsuariosPage'
import { SucursalesPage } from '../pages/SucursalesPage'
import { ConfiguracionPage } from '../pages/ConfiguracionPage'
import { ComprobantesConfigPage } from '../pages/ComprobantesConfigPage'
import { ChatIAPage } from '../pages/ChatIAPage'

/**
 * Application routes for the POS SPA.
 * Public routes: Login, Recuperar contraseña
 * Protected routes: All POS screens (wrapped in AppLayout with sidebar)
 */
export function AppRoutes() {
  return (
    <Routes>
      {/* Public routes */}
      <Route path="/login" element={<LoginPage />} />
      <Route path="/recuperar-password" element={<RecuperarPasswordPage />} />

      {/* Protected routes: requieren autenticación. */}
      <Route element={<ProtectedRoute />}>
        {/* Pantalla de cambio de contraseña obligatorio, a pantalla completa (sin
            sidebar) y fuera del guard para no provocar un bucle de redirección.
            El AuthContext libera la navegación al completar el cambio (Req 9.3). */}
        <Route path="/cambiar-password" element={<CambiarPasswordPage />} />

        {/* Guard de cambio obligatorio: si el Claim_Cambio está activo, fuerza
            /cambiar-password y bloquea el resto de rutas protegidas (Req 9.1, 9.2). */}
        <Route element={<RequirePasswordChangeGuard />}>
          <Route element={<AppLayout />}>
            <Route path="/dashboard" element={<DashboardPage />} />
          <Route
            path="/pos"
            element={<PosNormalPage />}
          />
          <Route
            path="/pos-bar"
            element={<PosBarPage />}
          />
          <Route
            path="/productos"
            element={<ProductosPage />}
          />
          <Route
            path="/categorias"
            element={<CategoriasPage />}
          />
          <Route
            path="/inventario"
            element={<InventarioPage />}
          />
          <Route
            path="/clientes"
            element={<ClientesPage />}
          />
          <Route
            path="/reportes"
            element={<ReportesPage />}
          />
          <Route
            path="/notificaciones"
            element={<NotificacionesPage />}
          />
          <Route
            path="/usuarios"
            element={<UsuariosPage />}
          />
          <Route
            path="/sucursales"
            element={<SucursalesPage />}
          />
          <Route
            path="/configuracion"
            element={<ConfiguracionPage />}
          />
          <Route
            path="/configuracion/comprobantes"
            element={<ComprobantesConfigPage />}
          />
            <Route
              path="/chat-ia"
              element={<ChatIAPage />}
            />
          </Route>
        </Route>
      </Route>

      {/* Default redirect */}
      <Route path="/" element={<Navigate to="/dashboard" replace />} />
      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}
