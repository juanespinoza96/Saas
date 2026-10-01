// e2e/fixtures/auth.setup.ts
import { test as setup } from '@playwright/test';
import { TEST_USERS } from './test-users';

const POS_URL = 'http://localhost:3000';
const ADMIN_URL = 'http://localhost:3001';

const authStates = [
  { user: TEST_USERS.gerenteEmpresarial, file: '.auth/gerente.json', baseURL: POS_URL },
  { user: TEST_USERS.cajeroEmpresarial, file: '.auth/cajero.json', baseURL: POS_URL },
  { user: TEST_USERS.supervisor, file: '.auth/supervisor.json', baseURL: POS_URL },
  { user: TEST_USERS.bodeguero, file: '.auth/bodeguero.json', baseURL: POS_URL },
  { user: TEST_USERS.dueno, file: '.auth/dueno.json', baseURL: POS_URL },
  { user: TEST_USERS.superadmin, file: '.auth/superadmin.json', baseURL: ADMIN_URL },
] as const;

for (const { user, file, baseURL } of authStates) {
  setup(`authenticate as ${user.role}`, async ({ page }) => {
    await page.goto(`${baseURL}/login`);
    // Los selectores coinciden con LoginPage.tsx del frontend POS:
    // label "Correo electrónico", label "Contraseña" y botón "Ingresar".
    await page.getByLabel('Correo electrónico').fill(user.email);
    await page.getByLabel('Contraseña').fill(user.password);
    await page.getByRole('button', { name: /ingresar/i }).click();
    await page.waitForURL('**/dashboard');
    await page.context().storageState({ path: file });
  });
}
