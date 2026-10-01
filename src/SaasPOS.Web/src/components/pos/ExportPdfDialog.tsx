import { useState, useEffect } from 'react'
import { Modal } from '../ui/Modal'
import { Button } from '../ui/Button'

/** Opciones de personalización de componentes PDF */
export interface PdfExportOptions {
  incluirGraficoBarras: boolean
  incluirGraficoPastel: boolean
  incluirTabla: boolean
}

interface ExportPdfDialogProps {
  /** Si el diálogo está abierto */
  isOpen: boolean
  /** Callback al confirmar la exportación con opciones seleccionadas */
  onConfirm: (options: PdfExportOptions) => void
  /** Callback al cancelar/cerrar el diálogo */
  onCancel: () => void
}

/**
 * Diálogo de personalización para exportación PDF (Plan Empresarial).
 * Permite seleccionar qué componentes visuales incluir en el PDF.
 * Todos los checkboxes se pre-seleccionan al abrir (Req 7.2).
 * El botón de exportar se deshabilita si todos están desmarcados (Req 7.3).
 */
export function ExportPdfDialog({
  isOpen,
  onConfirm,
  onCancel,
}: ExportPdfDialogProps) {
  const [options, setOptions] = useState<PdfExportOptions>({
    incluirGraficoBarras: true,
    incluirGraficoPastel: true,
    incluirTabla: true,
  })

  // Reiniciar opciones cada vez que se abre el diálogo (Req 7.2)
  useEffect(() => {
    if (isOpen) {
      setOptions({
        incluirGraficoBarras: true,
        incluirGraficoPastel: true,
        incluirTabla: true,
      })
    }
  }, [isOpen])

  const allUnchecked =
    !options.incluirGraficoBarras &&
    !options.incluirGraficoPastel &&
    !options.incluirTabla

  const handleToggle = (key: keyof PdfExportOptions) => {
    setOptions(prev => ({ ...prev, [key]: !prev[key] }))
  }

  return (
    <Modal open={isOpen} onClose={onCancel} title="Personalizar Exportación PDF" size="sm">
      <div className="space-y-4">
        <p className="text-sm text-gray-600 dark:text-gray-400">
          Seleccione los componentes que desea incluir en el documento PDF:
        </p>

        <div className="space-y-3">
          <label className="flex items-center gap-3 cursor-pointer">
            <input
              type="checkbox"
              checked={options.incluirGraficoBarras}
              onChange={() => handleToggle('incluirGraficoBarras')}
              className="h-4 w-4 rounded border-gray-300 dark:border-gray-600 text-action-confirm focus:ring-action-confirm"
            />
            <span className="text-sm text-gray-700 dark:text-gray-300">
              Gráfico de Barras
            </span>
          </label>

          <label className="flex items-center gap-3 cursor-pointer">
            <input
              type="checkbox"
              checked={options.incluirGraficoPastel}
              onChange={() => handleToggle('incluirGraficoPastel')}
              className="h-4 w-4 rounded border-gray-300 dark:border-gray-600 text-action-confirm focus:ring-action-confirm"
            />
            <span className="text-sm text-gray-700 dark:text-gray-300">
              Gráfico de Pastel
            </span>
          </label>

          <label className="flex items-center gap-3 cursor-pointer">
            <input
              type="checkbox"
              checked={options.incluirTabla}
              onChange={() => handleToggle('incluirTabla')}
              className="h-4 w-4 rounded border-gray-300 dark:border-gray-600 text-action-confirm focus:ring-action-confirm"
            />
            <span className="text-sm text-gray-700 dark:text-gray-300">
              Tabla de Datos
            </span>
          </label>
        </div>

        {/* Mensaje de validación cuando todos están desmarcados (Req 7.3) */}
        {allUnchecked && (
          <p className="text-sm text-action-danger" role="alert">
            Debe seleccionar al menos un componente para exportar.
          </p>
        )}

        <div className="flex justify-end gap-3 pt-2">
          <Button variant="neutral" onClick={onCancel}>
            Cancelar
          </Button>
          <Button
            variant="confirm"
            onClick={() => onConfirm(options)}
            disabled={allUnchecked}
          >
            Exportar PDF
          </Button>
        </div>
      </div>
    </Modal>
  )
}
