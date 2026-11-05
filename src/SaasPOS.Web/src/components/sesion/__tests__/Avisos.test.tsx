// Feature: sesion-inactividad-y-expiracion-por-rol
//
// Tests de componente y accesibilidad (vitest-axe) de los tres avisos de
// expiración de sesión. Cubre la Tarea 9.1:
// - Modal/Bloqueo exponen `role="alertdialog"`;
// - foco inicial en "Seguir conectado" (focus trap del Modal);
// - `aria-live` polite (Modal) y assertive (Bloqueo);
// - "Seguir conectado" renueva (invoca el callback);
// - BloqueoTotal a 0 dispara logout (+ redirección delegada en onExpirado);
// - sin violaciones de accesibilidad (axe) en Modal y Bloqueo.
//
// _Requirements: 5.4, 5.6, 6.1, 6.2, 6.3_

import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { AvisoTemprano } from '../AvisoTemprano'
import { ModalConfirmacion } from '../ModalConfirmacion'
import { BloqueoTotal } from '../BloqueoTotal'

describe('AvisoTemprano', () => {
  it('es no intrusivo: usa role="status" y aria-live="polite", sin alertdialog', () => {
    render(<AvisoTemprano remainingMs={5 * 60_000} />)
    const status = screen.getByRole('status')
    expect(status).toHaveAttribute('aria-live', 'polite')
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('no tiene violaciones de accesibilidad', async () => {
    const { container } = render(<AvisoTemprano remainingMs={5 * 60_000} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('ModalConfirmacion', () => {
  it('expone role="alertdialog" con aria-live="polite"', () => {
    render(<ModalConfirmacion remainingMs={60_000} onSeguirConectado={vi.fn()} />)
    const dialog = screen.getByRole('alertdialog')
    expect(dialog).toHaveAttribute('aria-live', 'polite')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
  })

  it('coloca el foco inicial en el botón "Seguir conectado" (focus trap)', () => {
    render(<ModalConfirmacion remainingMs={60_000} onSeguirConectado={vi.fn()} />)
    const boton = screen.getByRole('button', { name: /seguir conectado/i })
    expect(boton).toHaveFocus()
  })

  it('al pulsar "Seguir conectado" invoca la renovación', async () => {
    const onSeguir = vi.fn()
    const user = userEvent.setup()
    render(<ModalConfirmacion remainingMs={60_000} onSeguirConectado={onSeguir} />)

    await user.click(screen.getByRole('button', { name: /seguir conectado/i }))
    expect(onSeguir).toHaveBeenCalledTimes(1)
  })

  it('muestra la cuenta regresiva en segundos', () => {
    render(<ModalConfirmacion remainingMs={45_000} onSeguirConectado={vi.fn()} />)
    expect(screen.getByText(/45/)).toBeInTheDocument()
  })

  it('no tiene violaciones de accesibilidad', async () => {
    const { container } = render(
      <ModalConfirmacion remainingMs={60_000} onSeguirConectado={vi.fn()} />,
    )
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('BloqueoTotal', () => {
  it('expone role="alertdialog" con aria-live="assertive"', () => {
    render(<BloqueoTotal remainingMs={10_000} onExpirado={vi.fn()} />)
    const dialog = screen.getByRole('alertdialog')
    expect(dialog).toHaveAttribute('aria-live', 'assertive')
    expect(dialog).toHaveAttribute('aria-modal', 'true')
  })

  it('coloca el foco en el contenedor (focus trap sin controles accionables)', () => {
    render(<BloqueoTotal remainingMs={10_000} onExpirado={vi.fn()} />)
    const dialog = screen.getByRole('alertdialog')
    expect(dialog).toHaveFocus()
  })

  it('al llegar a 0 ms invoca onExpirado una sola vez (logout + redirección)', () => {
    const onExpirado = vi.fn()
    render(<BloqueoTotal remainingMs={0} onExpirado={onExpirado} />)
    expect(onExpirado).toHaveBeenCalledTimes(1)
  })

  it('con tiempo restante > 0 NO invoca onExpirado todavía', () => {
    const onExpirado = vi.fn()
    render(<BloqueoTotal remainingMs={5_000} onExpirado={onExpirado} />)
    expect(onExpirado).not.toHaveBeenCalled()
  })

  it('no tiene violaciones de accesibilidad', async () => {
    const { container } = render(<BloqueoTotal remainingMs={10_000} onExpirado={vi.fn()} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
