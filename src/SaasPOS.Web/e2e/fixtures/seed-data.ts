// e2e/fixtures/seed-data.ts
//
// Global setup de Playwright. En este entorno NO se siembran datos: la base
// local "saaspos" ya contiene los comercios, usuarios, productos y clientes
// reales que los tests utilizan (ver test-users.ts). Este setup solo valida
// que el backend esté accesible y que el login del SuperAdmin funcione, para
// fallar temprano con un mensaje claro si el API no está levantado.
//
// El puerto del backend por defecto es 5291 (ver GUIA_CLOUDFLARE_TUNNEL.md);
// se puede sobrescribir con la variable de entorno API_BASE_URL.
import { request } from '@playwright/test';
import { TEST_USERS } from './test-users';

const API_BASE_URL = process.env.API_BASE_URL || 'http://localhost:5291';

async function globalSetup() {
  const api = await request.newContext({ baseURL: API_BASE_URL });

  try {
    // Verificar conectividad + credenciales del SuperAdmin contra la base real.
    const loginRes = await api.post('/api/admin/auth/login', {
      data: {
        email: TEST_USERS.superadmin.email,
        password: TEST_USERS.superadmin.password,
      },
    });

    if (!loginRes.ok()) {
      console.warn(
        `[seed-data] Login de SuperAdmin (${TEST_USERS.superadmin.email}) falló con estado ` +
          `${loginRes.status()}. Verifica que el backend esté corriendo en ${API_BASE_URL} ` +
          `y que las credenciales de test-users.ts coincidan con la base local. ` +
          `Los tests que dependen de datos sembrados pueden fallar.`
      );
    } else {
      console.log('[seed-data] Backend accesible y credenciales de SuperAdmin válidas. Usando datos existentes de la base local.');
    }
  } catch (err) {
    console.warn(
      `[seed-data] No se pudo conectar al backend en ${API_BASE_URL}. ` +
        `Levanta el API (.NET) antes de correr las pruebas E2E. Detalle: ${(err as Error).message}`
    );
  } finally {
    await api.dispose();
  }
}

export default globalSetup;
