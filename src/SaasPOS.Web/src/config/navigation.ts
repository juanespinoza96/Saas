import { NavItem } from '../types/navigation'

/**
 * POS sidebar navigation items with role-based access control.
 * Items with empty allowedRoles are visible to all authenticated users.
 * 
 * Role permissions:
 * - Cajero: sell, manage clients
 * - Gerente: manage products, categories, users, sucursales, config, view reports
 * - Dueño (Plan Empresarial): same as Gerente + all
 * - Supervisor (Plan Empresarial): view reports, monitor sales (read-only)
 * - Bodeguero (Plan Empresarial): manage stock
 */
export const navItems: NavItem[] = [
  {
    id: 'dashboard',
    label: 'Dashboard',
    path: '/dashboard',
    icon: 'home',
    allowedRoles: ['Dueño', 'Gerente', 'Supervisor', 'Cajero', 'Bodeguero'],
  },
  {
    id: 'pos',
    label: 'Punto de Venta',
    path: '/pos',
    icon: 'shopping-cart',
    allowedRoles: ['Dueño', 'Gerente', 'Cajero'],
  },
  {
    id: 'pos-bar',
    label: 'Punto de Venta (Bar)',
    path: '/pos-bar',
    icon: 'coffee',
    allowedRoles: ['Dueño', 'Gerente', 'Cajero'],
  },
  {
    id: 'productos',
    label: 'Productos',
    path: '/productos',
    icon: 'package',
    allowedRoles: ['Dueño', 'Gerente'],
  },
  {
    id: 'categorias',
    label: 'Categorías y Atributos',
    path: '/categorias',
    icon: 'tag',
    allowedRoles: ['Dueño', 'Gerente'],
  },
  {
    id: 'inventario',
    label: 'Inventario / Stock',
    path: '/inventario',
    icon: 'archive',
    allowedRoles: ['Dueño', 'Gerente', 'Bodeguero'],
  },
  {
    id: 'clientes',
    label: 'Clientes',
    path: '/clientes',
    icon: 'users',
    allowedRoles: ['Dueño', 'Gerente', 'Cajero'],
  },
  {
    id: 'reportes',
    label: 'Reportes',
    path: '/reportes',
    icon: 'bar-chart',
    allowedRoles: ['Dueño', 'Gerente', 'Supervisor'],
  },
  {
    id: 'notificaciones',
    label: 'Notificaciones',
    path: '/notificaciones',
    icon: 'bell',
    allowedRoles: ['Dueño', 'Gerente', 'Supervisor', 'Cajero', 'Bodeguero'],
  },
  {
    id: 'usuarios',
    label: 'Usuarios',
    path: '/usuarios',
    icon: 'user-plus',
    allowedRoles: ['Dueño', 'Gerente'],
  },
  {
    id: 'sucursales',
    label: 'Sucursales',
    path: '/sucursales',
    icon: 'map-pin',
    allowedRoles: ['Dueño', 'Gerente'],
  },
  {
    id: 'configuracion',
    label: 'Configuración',
    path: '/configuracion',
    icon: 'settings',
    allowedRoles: ['Dueño', 'Gerente'],
  },
]
