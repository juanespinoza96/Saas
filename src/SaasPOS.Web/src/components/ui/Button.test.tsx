import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe } from 'vitest-axe'
import { describe, it, expect, vi } from 'vitest'
import { Button } from './Button'

describe('Button', () => {
  // Req 21.1: Semantic colors for buttons
  describe('variant classes (Req 21.1)', () => {
    it('renders confirm variant with green classes', () => {
      render(<Button variant="confirm">Guardar</Button>)
      const button = screen.getByRole('button', { name: 'Guardar' })
      expect(button.className).toContain('bg-action-confirm')
    })

    it('renders edit variant with yellow classes', () => {
      render(<Button variant="edit">Editar</Button>)
      const button = screen.getByRole('button', { name: 'Editar' })
      expect(button.className).toContain('bg-action-edit')
    })

    it('renders danger variant with red classes', () => {
      render(<Button variant="danger">Eliminar</Button>)
      const button = screen.getByRole('button', { name: 'Eliminar' })
      expect(button.className).toContain('bg-action-danger')
    })
  })

  // Req 21.14: Disable buttons during API requests with loading indicator
  describe('loading state (Req 21.14)', () => {
    it('shows spinner and is disabled when loading=true', () => {
      render(<Button variant="confirm" loading>Guardando</Button>)
      const button = screen.getByRole('button', { name: 'Guardando' })

      expect(button).toBeDisabled()
      expect(button).toHaveAttribute('aria-busy', 'true')
      // Spinner SVG should be present
      const spinner = button.querySelector('svg.animate-spin')
      expect(spinner).toBeInTheDocument()
    })

    it('does not show spinner when loading=false', () => {
      render(<Button variant="confirm">Guardar</Button>)
      const button = screen.getByRole('button', { name: 'Guardar' })

      expect(button).not.toBeDisabled()
      expect(button).toHaveAttribute('aria-busy', 'false')
      const spinner = button.querySelector('svg.animate-spin')
      expect(spinner).not.toBeInTheDocument()
    })
  })

  describe('click behavior', () => {
    it('calls onClick when not disabled', async () => {
      const user = userEvent.setup()
      const onClick = vi.fn()
      render(<Button variant="confirm" onClick={onClick}>Confirmar</Button>)

      await user.click(screen.getByRole('button', { name: 'Confirmar' }))
      expect(onClick).toHaveBeenCalledTimes(1)
    })

    it('does not call onClick when disabled', async () => {
      const user = userEvent.setup()
      const onClick = vi.fn()
      render(<Button variant="confirm" disabled onClick={onClick}>Confirmar</Button>)

      await user.click(screen.getByRole('button', { name: 'Confirmar' }))
      expect(onClick).not.toHaveBeenCalled()
    })

    it('does not call onClick when loading', async () => {
      const user = userEvent.setup()
      const onClick = vi.fn()
      render(<Button variant="confirm" loading onClick={onClick}>Confirmar</Button>)

      await user.click(screen.getByRole('button', { name: 'Confirmar' }))
      expect(onClick).not.toHaveBeenCalled()
    })
  })

  // Accessibility with axe-core (WCAG 2.1 AA)
  describe('accessibility (WCAG 2.1 AA)', () => {
    it('has no accessibility violations', async () => {
      const { container } = render(
        <div>
          <Button variant="confirm">Confirmar</Button>
          <Button variant="edit">Editar</Button>
          <Button variant="danger">Eliminar</Button>
          <Button variant="neutral">Cancelar</Button>
        </div>
      )
      const results = await axe(container)
      expect(results).toHaveNoViolations()
    })

    it('has no accessibility violations when loading', async () => {
      const { container } = render(
        <Button variant="confirm" loading>Cargando</Button>
      )
      const results = await axe(container)
      expect(results).toHaveNoViolations()
    })
  })
})
