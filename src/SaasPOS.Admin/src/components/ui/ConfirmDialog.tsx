import { Modal } from './Modal'
import { Button } from './Button'

interface ConfirmDialogProps {
  isOpen: boolean
  onClose?: () => void
  onCancel?: () => void
  onConfirm: () => void
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'danger' | 'confirm'
  loading?: boolean
  isLoading?: boolean
}

export function ConfirmDialog({
  isOpen,
  onClose,
  onCancel,
  onConfirm,
  title,
  message,
  confirmLabel = 'Confirmar',
  cancelLabel = 'Cancelar',
  variant = 'danger',
  loading,
  isLoading,
}: ConfirmDialogProps) {
  const isProcessing = loading ?? isLoading ?? false
  const handleClose = onClose ?? onCancel ?? (() => {})

  return (
    <Modal isOpen={isOpen} onClose={handleClose} title={title}>
      <p className="text-gray-600 dark:text-dark-text/70 mb-6">{message}</p>
      <div className="flex justify-end gap-3">
        <Button variant="neutral" onClick={handleClose} disabled={isProcessing}>
          {cancelLabel}
        </Button>
        <Button variant={variant} onClick={onConfirm} disabled={isProcessing}>
          {isProcessing ? 'Procesando...' : confirmLabel}
        </Button>
      </div>
    </Modal>
  )
}
