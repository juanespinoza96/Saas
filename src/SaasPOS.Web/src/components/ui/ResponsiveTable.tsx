import { ReactNode } from 'react'

/**
 * Props para el componente ResponsiveTable.
 * Permite configurar qué columnas son sticky en móvil.
 */
interface ResponsiveTableProps {
  /** Contenido de la tabla (elemento <table>) */
  children: ReactNode
  /** Clases CSS adicionales para el contenedor exterior */
  className?: string
  /** Número de columnas iniciales que serán sticky en móvil (por defecto 1) */
  stickyColumns?: number
}

/**
 * Componente wrapper que proporciona scroll horizontal responsive en tablas.
 * En pantallas <768px activa el scroll horizontal y fija las columnas de identificación.
 * En desktop no altera el comportamiento existente.
 *
 * Uso:
 * ```tsx
 * <ResponsiveTable stickyColumns={1}>
 *   <table>...</table>
 * </ResponsiveTable>
 * ```
 */
export function ResponsiveTable({
  children,
  className = '',
  stickyColumns = 1,
}: ResponsiveTableProps) {
  return (
    <div
      className={`table-responsive ${className}`}
      data-sticky-columns={stickyColumns}
    >
      {children}
    </div>
  )
}
