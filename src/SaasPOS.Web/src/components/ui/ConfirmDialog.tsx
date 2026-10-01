import { type ReactNode } from 'react'
import { Modal } from './Modal'
import { Button } from './Button'

interface ConfirmDialogProps {
  /** Whether the dialog is open */
  open: boolean
  /** Called when the user cancels or closes */
  onClose: () => void
  /** Called when the user confirms the action */
  onConfirm: () => void
  /** Dialog title */
  title: string
  /** Description of what will happen */
  children: ReactNode
  /** Text for the confirm button (default: "Confirmar") */
  confirmLabel?: string
  /** Text for the cancel button (default: "Cancelar") */
  cancelLabel?: string
  /** Button variant for confirm action (default: "danger" for destructive actions) */
  confirmVariant?: 'confirm' | 'edit' | 'danger'
  /** Whether the confirm action is in progress */
  loading?: boolean
}

/**
 * Confirmation dialog for irreversible/destructive actions (Req 21.15).
 * Uses Modal internally with semantic danger styling by default.
 */
export function ConfirmDialog({
  open,
  onClose,
  onConfirm,
  title,
  children,
  confirmLabel = 'Confirmar',
  cancelLabel = 'Cancelar',
  confirmVariant = 'danger',
  loading = false,
}: ConfirmDialogProps) {
  return (
    <Modal open={open} onClose={onClose} title={title} size="sm">
      <div className="space-y-4">
        <div className="text-sm text-gray-600 dark:text-gray-400">
          {children}
        </div>
        <div className="flex justify-end gap-3">
          <Button
            variant="neutral"
            onClick={onClose}
            disabled={loading}
          >
            {cancelLabel}
          </Button>
          <Button
            variant={confirmVariant}
            onClick={onConfirm}
            loading={loading}
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </Modal>
  )
}
