import { type ButtonHTMLAttributes, type ReactNode } from 'react'

type ButtonVariant = 'confirm' | 'edit' | 'danger' | 'neutral'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  children: ReactNode
}

const variantClasses: Record<ButtonVariant, string> = {
  confirm: 'bg-action-confirm hover:bg-action-confirm/90 text-white',
  edit: 'bg-action-edit hover:bg-action-edit/90 text-white',
  danger: 'bg-action-danger hover:bg-action-danger/90 text-white',
  neutral: 'bg-gray-200 hover:bg-gray-300 text-gray-800 dark:bg-dark-surface dark:hover:bg-dark-surface/80 dark:text-dark-text',
}

export function Button({ variant = 'neutral', children, className = '', ...props }: ButtonProps) {
  return (
    <button
      className={`px-4 py-2 rounded-lg font-medium text-sm transition-colors duration-150 
        focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-action-confirm
        disabled:opacity-50 disabled:cursor-not-allowed
        ${variantClasses[variant]} ${className}`}
      {...props}
    >
      {children}
    </button>
  )
}
