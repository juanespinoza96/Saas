import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { ExportPdfDialog } from './ExportPdfDialog'

describe('ExportPdfDialog', () => {
  const defaultProps = {
    isOpen: true,
    onConfirm: vi.fn(),
    onCancel: vi.fn(),
  }

  // Req 7.2: Todos los checkboxes pre-seleccionados por defecto al abrir
  it('renderiza con todos los checkboxes pre-seleccionados al abrir', () => {
    render(<ExportPdfDialog {...defaultProps} />)

    const checkboxes = screen.getAllByRole('checkbox')
    expect(checkboxes).toHaveLength(3)
    checkboxes.forEach(checkbox => {
      expect(checkbox).toBeChecked()
    })

    // Verificar que las etiquetas de los 3 componentes están presentes (Req 7.1)
    expect(screen.getByText('Gráfico de Barras')).toBeInTheDocument()
    expect(screen.getByText('Gráfico de Pastel')).toBeInTheDocument()
    expect(screen.getByText('Tabla de Datos')).toBeInTheDocument()
  })

  // Req 7.3: Botón deshabilitado si todos desmarcados + mensaje de validación
  it('deshabilita el botón de exportar y muestra mensaje si todos los checkboxes están desmarcados', async () => {
    const user = userEvent.setup()
    render(<ExportPdfDialog {...defaultProps} />)

    // Desmarcar los 3 checkboxes
    const checkboxes = screen.getAllByRole('checkbox')
    for (const checkbox of checkboxes) {
      await user.click(checkbox)
    }

    // Verificar botón deshabilitado
    const exportBtn = screen.getByRole('button', { name: /exportar pdf/i })
    expect(exportBtn).toBeDisabled()

    // Verificar mensaje de validación
    expect(
      screen.getByRole('alert')
    ).toHaveTextContent('Debe seleccionar al menos un componente para exportar.')
  })

  // Req 7.4: No se muestra cuando isOpen=false (simula Plan Intermedio que nunca abre el diálogo)
  it('no renderiza nada cuando isOpen es false', () => {
    const { container } = render(
      <ExportPdfDialog {...defaultProps} isOpen={false} />
    )
    // El Modal retorna null cuando open=false
    expect(container).toBeEmptyDOMElement()
  })

  // onConfirm emite las opciones seleccionadas correctamente
  it('llama a onConfirm con las opciones correctas al hacer click en Exportar PDF', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(<ExportPdfDialog {...defaultProps} onConfirm={onConfirm} />)

    // Desmarcar "Gráfico de Pastel" (segundo checkbox)
    const checkboxes = screen.getAllByRole('checkbox')
    await user.click(checkboxes[1]!)

    // Click en botón exportar
    const exportBtn = screen.getByRole('button', { name: /exportar pdf/i })
    await user.click(exportBtn)

    // Verificar que onConfirm fue llamado con opciones correctas
    expect(onConfirm).toHaveBeenCalledOnce()
    expect(onConfirm).toHaveBeenCalledWith({
      incluirGraficoBarras: true,
      incluirGraficoPastel: false,
      incluirTabla: true,
    })
  })
})
