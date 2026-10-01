import { test, expect } from '@playwright/test'

test.describe('Login Flow', () => {
  test('navigates to /login and shows login form', async ({ page }) => {
    await page.goto('/login')

    await expect(page.getByRole('heading', { name: /iniciar sesión/i })).toBeVisible()
    await expect(page.getByLabel(/correo electrónico/i)).toBeVisible()
    await expect(page.getByLabel(/contraseña/i)).toBeVisible()
    await expect(page.getByRole('button', { name: /ingresar/i })).toBeVisible()
  })

  test('fills email and password, submits, and redirects to /dashboard on success', async ({ page }) => {
    await page.goto('/login')

    // Intercept the login API call and return a valid JWT
    await page.route('**/api/tenants/auth/login', async (route) => {
      // Fake JWT with expected claims structure
      const fakeToken = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwiZW1haWwiOiJ0ZXN0QGV4YW1wbGUuY29tIiwicm9sZSI6IkFkbWluIiwiY29tZXJjaW9faWQiOiIxIiwiY29tZXJjaW9fbm9tYnJlIjoiVGVzdCIsImV4cCI6OTk5OTk5OTk5OX0.fake-signature'
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ token: fakeToken }),
      })
    })

    await page.getByLabel(/correo electrónico/i).fill('admin@test.com')
    await page.getByLabel(/contraseña/i).fill('Password123!')
    await page.getByRole('button', { name: /ingresar/i }).click()

    // Should redirect to dashboard
    await expect(page).toHaveURL(/\/dashboard/)
  })

  test('shows generic error message on authentication failure', async ({ page }) => {
    await page.goto('/login')

    // Intercept and return 401
    await page.route('**/api/tenants/auth/login', async (route) => {
      await route.fulfill({
        status: 401,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Invalid credentials' }),
      })
    })

    await page.getByLabel(/correo electrónico/i).fill('wrong@test.com')
    await page.getByLabel(/contraseña/i).fill('wrongpassword')
    await page.getByRole('button', { name: /ingresar/i }).click()

    // Should show generic error message
    await expect(page.getByText('Credenciales inválidas')).toBeVisible()
  })

  test('shows inline validation for empty fields', async ({ page }) => {
    await page.goto('/login')

    // Submit without filling fields
    await page.getByRole('button', { name: /ingresar/i }).click()

    await expect(page.getByText('El correo es obligatorio')).toBeVisible()
    await expect(page.getByText('La contraseña es obligatoria')).toBeVisible()
  })
})
