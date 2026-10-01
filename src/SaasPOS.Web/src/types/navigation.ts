import { UserRole } from './auth'

export interface NavItem {
  id: string
  label: string
  path: string
  icon: string
  /** Roles that can see this item. Empty array = visible to all authenticated users */
  allowedRoles: UserRole[]
}
