import { type ButtonHTMLAttributes, type ReactNode } from 'react'

type ButtonVariant = 'confirm' | 'edit' | 'danger' | 'neutral'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  loading?: boolean
  children: ReactNode
}

const variantClasses: Record<ButtonVariant, string> = {
  // Green (#4CAF7D) - confirmación, guardado, creación
  confirm:
    'bg-action-confirm hover:bg-action-confirm/90 text-white focus:ring-action-confirm',
  // Yellow (#F5A623) - edición, modificación
  edit:
    'bg-action-edit hover:bg-action-edit/90 text-white focus:ring-action-edit',
  // Red (#E57373) - eliminación, cancelación
  danger:
    'bg-action-danger hover:bg-action-danger/90 text-white focus:ring-action-danger',
  // Neutral for non-action buttons
  neutral:
    'bg-gray-200 hover:bg-gray-300 text-gray-800 dark:bg-dark-surface dark:hover:bg-dark-surface/80 dark:text-dark-text focus:ring-gray-400',
}

/**
 * Reusable Button component with semantic colors and loading state.
 * - Green: confirmación, guardado, creación
 * - Yellow: edición, modificación
 * - Red: eliminación, cancelación
 * 
 * Disables during loading to prevent duplicate submissions (Req 21.14).
 */
export function Button({
  variant = 'neutral',
  loading = false,
  disabled,
  children,
  className = '',
  ...props
}: ButtonProps) {
  const isDisabled = disabled || loading

  return (
    <button
      className={`
        inline-flex items-center justify-center gap-2 px-4 py-2 rounded-lg
        text-sm font-medium transition-all duration-150
        focus:outline-none focus:ring-2 focus:ring-offset-2
        disabled:opacity-50 disabled:cursor-not-allowed
        ${variantClasses[variant]}
        ${className}
      `}
      disabled={isDisabled}
      aria-busy={loading}
      aria-disabled={isDisabled}
      {...props}
    >
      {loading && (
        <svg
          className="animate-spin h-4 w-4"
          xmlns="http://www.w3.org/2000/svg"
          fill="none"
          viewBox="0 0 24 24"
          aria-hidden="true"
        >
          <circle
            className="opacity-25"
            cx="12"
            cy="12"
            r="10"
            stroke="currentColor"
            strokeWidth="4"
          />
          <path
            className="opacity-75"
            fill="currentColor"
            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"
          />
        </svg>
      )}
      {children}
    </button>
  )
}
