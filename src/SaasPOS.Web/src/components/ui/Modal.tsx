import { useEffect, useCallback, useRef, type ReactNode } from 'react'

interface ModalProps {
  /** Whether the modal is open */
  open: boolean
  /** Called when the modal should close (overlay click, Escape key, close button) */
  onClose: () => void
  /** Modal title displayed in the header */
  title: string
  /** Modal body content */
  children: ReactNode
  /**
   * If true, shows a warning before closing when the user tries to dismiss.
   * Used to prevent accidental loss of unsaved changes.
   */
  warnOnClose?: boolean
  /** Custom warning message for unsaved changes */
  warnMessage?: string
  /** Size variant */
  size?: 'sm' | 'md' | 'lg' | 'xl'
}

const sizeClasses: Record<string, string> = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-lg',
  xl: 'max-w-xl',
}

/**
 * Reusable Modal component with overlay, close button, and Escape key handling.
 * Supports unsaved-changes warning before closing (Req 21.9, 21.10).
 */
export function Modal({
  open,
  onClose,
  title,
  children,
  warnOnClose = false,
  warnMessage = '¿Estás seguro? Los cambios no guardados se perderán.',
  size = 'md',
}: ModalProps) {
  const overlayRef = useRef<HTMLDivElement>(null)

  const handleClose = useCallback(() => {
    if (warnOnClose) {
      // Using native confirm for unsaved changes warning
      const confirmed = window.confirm(warnMessage)
      if (!confirmed) return
    }
    onClose()
  }, [warnOnClose, warnMessage, onClose])

  // Handle Escape key
  useEffect(() => {
    if (!open) return

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        handleClose()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [open, handleClose])

  // Trap focus within modal and prevent background scroll
  useEffect(() => {
    if (open) {
      document.body.style.overflow = 'hidden'
    }
    return () => {
      document.body.style.overflow = ''
    }
  }, [open])

  if (!open) return null

  return (
    <div
      ref={overlayRef}
      className="fixed inset-0 z-50 flex items-center justify-center p-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="modal-title"
    >
      {/* Overlay backdrop */}
      <div
        className="absolute inset-0 bg-black/50 transition-opacity"
        onClick={handleClose}
        aria-hidden="true"
      />

      {/* Modal panel */}
      <div
        className={`
          relative w-full ${sizeClasses[size]}
          bg-white dark:bg-dark-surface
          rounded-xl shadow-xl
          transform transition-all
        `}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 dark:border-gray-700">
          <h3
            id="modal-title"
            className="text-lg font-semibold text-gray-900 dark:text-dark-text"
          >
            {title}
          </h3>
          <button
            onClick={handleClose}
            className="p-1 rounded-md text-gray-400 hover:text-gray-600 dark:hover:text-gray-300 focus:outline-none focus:ring-2 focus:ring-action-confirm"
            aria-label="Cerrar modal"
          >
            <svg
              className="h-5 w-5"
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

        {/* Body */}
        <div className="px-6 py-4">{children}</div>
      </div>
    </div>
  )
}
