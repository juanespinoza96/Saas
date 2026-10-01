import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, beforeEach } from 'vitest'
import { ThemeProvider, useTheme } from './ThemeContext'

// Helper component to expose theme context for testing
function ThemeTestConsumer() {
  const { theme, toggleTheme } = useTheme()
  return (
    <div>
      <span data-testid="current-theme">{theme}</span>
      <button onClick={toggleTheme}>Toggle</button>
    </div>
  )
}

describe('ThemeContext', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.classList.remove('dark')
  })

  it('defaults to light theme', () => {
    render(
      <ThemeProvider>
        <ThemeTestConsumer />
      </ThemeProvider>
    )

    expect(screen.getByTestId('current-theme')).toHaveTextContent('light')
  })

  it('toggles between light and dark mode', async () => {
    const user = userEvent.setup()

    render(
      <ThemeProvider>
        <ThemeTestConsumer />
      </ThemeProvider>
    )

    expect(screen.getByTestId('current-theme')).toHaveTextContent('light')

    // Toggle to dark
    await user.click(screen.getByRole('button', { name: 'Toggle' }))
    expect(screen.getByTestId('current-theme')).toHaveTextContent('dark')

    // Toggle back to light
    await user.click(screen.getByRole('button', { name: 'Toggle' }))
    expect(screen.getByTestId('current-theme')).toHaveTextContent('light')
  })

  it('persists theme preference in localStorage', async () => {
    const user = userEvent.setup()

    render(
      <ThemeProvider>
        <ThemeTestConsumer />
      </ThemeProvider>
    )

    // Toggle to dark
    await user.click(screen.getByRole('button', { name: 'Toggle' }))

    // localStorage should have 'dark'
    expect(localStorage.getItem('saas-pos-theme')).toBe('dark')

    // Toggle back to light
    await user.click(screen.getByRole('button', { name: 'Toggle' }))
    expect(localStorage.getItem('saas-pos-theme')).toBe('light')
  })

  it('applies dark class to document.documentElement in dark mode', async () => {
    const user = userEvent.setup()

    render(
      <ThemeProvider>
        <ThemeTestConsumer />
      </ThemeProvider>
    )

    // Toggle to dark
    await user.click(screen.getByRole('button', { name: 'Toggle' }))
    expect(document.documentElement.classList.contains('dark')).toBe(true)

    // Toggle back to light
    await user.click(screen.getByRole('button', { name: 'Toggle' }))
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('reads persisted theme from localStorage on mount', () => {
    localStorage.setItem('saas-pos-theme', 'dark')

    render(
      <ThemeProvider>
        <ThemeTestConsumer />
      </ThemeProvider>
    )

    expect(screen.getByTestId('current-theme')).toHaveTextContent('dark')
  })
})
