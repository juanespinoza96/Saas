import { test, expect } from '@playwright/test'

test.describe('POS Bar Escolar Flow', () => {
  test.beforeEach(async ({ page }) => {
    // Set a fake auth token so we can access protected routes
    await page.goto('/login')
    await page.evaluate(() => {
      const fakeToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwiZW1haWwiOiJ0ZXN0QGV4YW1wbGUuY29tIiwicm9sZSI6IkFkbWluIiwiY29tZXJjaW9faWQiOiIxIiwiY29tZXJjaW9fbm9tYnJlIjoiVGVzdCIsImV4cCI6OTk5OTk5OTk5OX0.fake-signature'
      localStorage.setItem('saas-pos-token', fakeToken)
    })
  })

  test('navigates to /pos-bar, clicks a product card, and sees success toast', async ({ page }) => {
    // Mock API responses for bar escolar page
    await page.route('**/api/tenants/productos', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { id: 1, nombre: 'Empanada', tipoArticulo: 'Venta Directa', precioLista: 1.50, categoriaId: 1, categoriaNombre: 'Snacks' },
          { id: 2, nombre: 'Jugo Natural', tipoArticulo: 'Venta Directa', precioLista: 2.00, categoriaId: 2, categoriaNombre: 'Bebidas' },
        ]),
      })
    })

    await page.route('**/api/tenants/configuracion', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ esBarEscolar: true }),
      })
    })

    await page.route('**/api/tenants/sucursales', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([{ id: 1, nombre: 'Principal' }]),
      })
    })

    await page.route('**/api/tenants/ventas/bar-escolar', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ id: 1 }),
      })
    })

    await page.goto('/pos-bar')

    // Wait for products to load
    await expect(page.getByText('Empanada')).toBeVisible()

    // Click on a product card
    await page.getByRole('button', { name: /Vender Empanada/i }).click()

    // Verify success toast appears
    await expect(page.getByText(/✓ Empanada/)).toBeVisible()
  })

  test('shows not available message when EsBarEscolar is false', async ({ page }) => {
    await page.route('**/api/tenants/productos', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      })
    })

    await page.route('**/api/tenants/configuracion', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ esBarEscolar: false }),
      })
    })

    await page.route('**/api/tenants/sucursales', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([{ id: 1, nombre: 'Principal' }]),
      })
    })

    await page.goto('/pos-bar')

    await expect(page.getByText('Modo Bar Escolar no disponible')).toBeVisible()
  })
})
