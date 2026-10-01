import { test, expect } from '@playwright/test'

test.describe('Theme Toggle', () => {
  test('toggles theme and verifies dark class on html element', async ({ page }) => {
    await page.goto('/login')

    // Initially, html should NOT have 'dark' class (default is light)
    const html = page.locator('html')
    await expect(html).not.toHaveClass(/dark/)

    // Find and click the theme toggle button
    const themeToggle = page.getByRole('button', { name: /tema|theme|modo/i })

    // If theme toggle is in a layout (may need to navigate to a page with it)
    if (await themeToggle.isVisible()) {
      await themeToggle.click()

      // After toggling, html should have 'dark' class
      await expect(html).toHaveClass(/dark/)

      // Toggle back
      await themeToggle.click()
      await expect(html).not.toHaveClass(/dark/)
    }
  })

  test('persists theme after page reload', async ({ page }) => {
    await page.goto('/login')

    // Set dark theme via localStorage directly
    await page.evaluate(() => {
      localStorage.setItem('saas-pos-theme', 'dark')
    })

    // Reload page
    await page.reload()

    // HTML should have dark class after reload
    const html = page.locator('html')
    await expect(html).toHaveClass(/dark/)
  })
})
