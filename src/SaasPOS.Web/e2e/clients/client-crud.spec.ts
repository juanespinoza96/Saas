import { test } from '@playwright/test';
import { ClientesPage } from '../page-objects/ClientesPage';

test.describe('Client CRUD', () => {
  test.use({ storageState: '.auth/gerente.json' });

  test('Create client with valid data appears in list', async ({ page }) => {
    const clientesPage = new ClientesPage(page);
    await clientesPage.goto();
    await clientesPage.expectLoaded();

    const uniqueId = `09${Date.now().toString().slice(-8)}`;
    await clientesPage.createClient({
      identificacion: uniqueId,
      nombre: 'Cliente Test E2E',
      correo: 'test-e2e@test.com',
      direccion: 'Calle E2E 123',
      telefono: '0991234567',
    });

    // Verify client appears in list after creation
    await clientesPage.expectClientInList('Cliente Test E2E');
  });

  test('Duplicate identification shows error', async ({ page }) => {
    const clientesPage = new ClientesPage(page);
    await clientesPage.goto();
    await clientesPage.expectLoaded();

    // Use the seeded client identification (from globalSetup)
    await clientesPage.createClient({
      identificacion: '1712345678',
      nombre: 'Duplicate Test',
      correo: 'dup@test.com',
      direccion: 'Calle Dup',
      telefono: '0991111111',
    });

    // Should show error about duplicate
    await clientesPage.expectDuplicateError();
  });

  test('ConsumidorFinal TRUE makes Correo Direccion Telefono optional', async ({ page }) => {
    const clientesPage = new ClientesPage(page);
    await clientesPage.goto();
    await clientesPage.expectLoaded();

    const uniqueId = `09${Date.now().toString().slice(-8)}`;

    // Create client with esConsumidorFinal=true and NO correo/direccion/telefono
    await clientesPage.createClient({
      identificacion: uniqueId,
      nombre: 'Consumidor Final Test',
      esConsumidorFinal: true,
      // No correo, direccion, telefono — should succeed
    });

    // Should succeed — client appears in list
    await clientesPage.expectClientInList('Consumidor Final Test');
  });
});
