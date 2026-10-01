import { render, screen } from '@testing-library/react'
import { axe } from 'vitest-axe'
import { describe, it, expect } from 'vitest'
import { InstallmentSummary, calcularValorCuota } from './InstallmentSummary'

describe('calcularValorCuota', () => {
  // Req 1.5: Calcular valor cuota = Total / CuotasMeses redondeado a 2 decimales
  it('divide el total por el número de cuotas y redondea a 2 decimales', () => {
    expect(calcularValorCuota(100, 3)).toBe(33.33)
    expect(calcularValorCuota(1000, 6)).toBe(166.67)
    expect(calcularValorCuota(500, 12)).toBe(41.67)
  })

  it('retorna 0 cuando cuotas es 0', () => {
    expect(calcularValorCuota(100, 0)).toBe(0)
  })

  it('retorna 0 cuando cuotas es negativo', () => {
    expect(calcularValorCuota(100, -1)).toBe(0)
  })

  it('maneja montos exactamente divisibles', () => {
    expect(calcularValorCuota(120, 3)).toBe(40)
    expect(calcularValorCuota(1800, 18)).toBe(100)
  })
})

describe('InstallmentSummary', () => {
  // Req 2.3: No renderizar cuando cuotas = 0 (pago Corriente)
  it('no renderiza nada cuando cuotas es 0', () => {
    const { container } = render(<InstallmentSummary total={100} cuotas={0} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('no renderiza nada cuando cuotas es negativo', () => {
    const { container } = render(<InstallmentSummary total={100} cuotas={-1} />)
    expect(container).toBeEmptyDOMElement()
  })

  // Req 2.1: Mostrar resumen visual con total, cuotas y valor por cuota
  it('muestra el total de la venta formateado', () => {
    render(<InstallmentSummary total={1500.5} cuotas={3} />)
    expect(screen.getByText('$1500.50')).toBeInTheDocument()
  })

  it('muestra el número de cuotas con "meses"', () => {
    render(<InstallmentSummary total={1200} cuotas={6} />)
    expect(screen.getByText('6 meses')).toBeInTheDocument()
  })

  // Req 2.2: Mostrar valor cuota con formato moneda y "por mes"
  it('muestra el valor por cuota calculado con formato moneda', () => {
    render(<InstallmentSummary total={1000} cuotas={3} />)
    // 1000 / 3 = 333.33
    expect(screen.getByText('$333.33')).toBeInTheDocument()
  })

  it('muestra la etiqueta "por mes"', () => {
    render(<InstallmentSummary total={600} cuotas={6} />)
    expect(screen.getByText('por mes')).toBeInTheDocument()
  })

  // Req 2.3: Recalcular inmediatamente al cambiar cuotas (sin recarga)
  it('recalcula al cambiar las props de cuotas', () => {
    const { rerender } = render(<InstallmentSummary total={1200} cuotas={3} />)
    // 1200 / 3 = 400
    expect(screen.getByText('$400.00')).toBeInTheDocument()

    rerender(<InstallmentSummary total={1200} cuotas={6} />)
    // 1200 / 6 = 200
    expect(screen.getByText('$200.00')).toBeInTheDocument()
  })

  // Req 2.4: Nota informativa sobre procesamiento externo
  it('muestra la nota informativa sobre procesamiento externo', () => {
    render(<InstallmentSummary total={500} cuotas={3} />)
    expect(
      screen.getByText(/Este sistema solo registra la información de pago/)
    ).toBeInTheDocument()
  })

  // Accesibilidad
  describe('accesibilidad', () => {
    it('tiene aria-label en la sección', () => {
      render(<InstallmentSummary total={1000} cuotas={3} />)
      expect(screen.getByRole('region', { name: 'Resumen de cuotas' })).toBeInTheDocument()
    })

    it('tiene role="note" en la nota informativa', () => {
      render(<InstallmentSummary total={1000} cuotas={3} />)
      expect(screen.getByRole('note')).toBeInTheDocument()
    })

    it('no tiene violaciones de accesibilidad (WCAG 2.1 AA)', async () => {
      const { container } = render(<InstallmentSummary total={1500} cuotas={6} />)
      const results = await axe(container)
      expect(results).toHaveNoViolations()
    })
  })
})
