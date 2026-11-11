// e2e/fixtures/test-users.ts
//
// Usuarios de prueba mapeados a los datos REALES existentes en la base local
// "saaspos" (comercio "Comercio Demo", Id 1, plan Empresarial). Todos los
// usuarios de "Comercio Demo" comparten la contraseña `Admin123!` (mismo hash
// BCrypt verificado en la base). No se siembran usuarios nuevos: los tests se
// adaptan a los datos ya presentes.

export interface TestUser {
  email: string;
  password: string;
  role: string;
  comercio: string;
  plan: string;
}

// Contraseña común de los usuarios de desarrollo de "Comercio Demo".
const DEMO_PASSWORD = 'Admin123!';

export const TEST_USERS = {
  // Plan Empresarial — Comercio Demo (usuarios reales de la base local)
  gerenteEmpresarial: {
    email: 'gerente@demo.com',
    password: DEMO_PASSWORD,
    role: 'Gerente',
    comercio: 'Comercio Demo',
    plan: 'Empresarial',
  },
  cajeroEmpresarial: {
    email: 'cajero@demo.com',
    password: DEMO_PASSWORD,
    role: 'Cajero',
    comercio: 'Comercio Demo',
    plan: 'Empresarial',
  },
  dueno: {
    email: 'dueno@demo.com',
    password: DEMO_PASSWORD,
    role: 'Dueño',
    comercio: 'Comercio Demo',
    plan: 'Empresarial',
  },
  supervisor: {
    email: 'supervisor@demo.com',
    password: DEMO_PASSWORD,
    role: 'Supervisor',
    comercio: 'Comercio Demo',
    plan: 'Empresarial',
  },
  bodeguero: {
    email: 'bodeguero@demo.com',
    password: DEMO_PASSWORD,
    role: 'Bodeguero',
    comercio: 'Comercio Demo',
    plan: 'Empresarial',
  },

  // Plan Básico — Moda Express S.A. (comercio 2). El plan Básico solo contempla dos
  // usuarios: el Dueño y un Cajero; NO existe Gerente en este plan. Los specs que
  // validan restricciones del plan Básico usan al Dueño (rol de mayor jerarquía).
  duenoBasico: {
    email: 'duena@modaexpress.com',
    password: DEMO_PASSWORD,
    role: 'Dueño',
    comercio: 'Moda Express S.A.',
    plan: 'Básico',
  },
  cajeroBasico: {
    email: 'cajero@modaexpress.com',
    password: DEMO_PASSWORD,
    role: 'Cajero',
    comercio: 'Moda Express S.A.',
    plan: 'Básico',
  },

  // Plan Intermedio — Sabores del Valle Cía. Ltda. (comercio 3)
  gerenteIntermedio: {
    email: 'gerente@sabores.com',
    password: DEMO_PASSWORD,
    role: 'Gerente',
    comercio: 'Sabores del Valle Cía. Ltda.',
    plan: 'Intermedio',
  },

  // SuperAdmin (usuario real de la base local)
  superadmin: {
    email: 'admin@saaspos.com',
    password: DEMO_PASSWORD,
    role: 'SuperAdmin',
    comercio: 'Platform',
    plan: 'N/A',
  },
} as const satisfies Record<string, TestUser>;

// Aliases de conveniencia usados por auth.setup.ts
export const GERENTE = TEST_USERS.gerenteEmpresarial;
export const CAJERO = TEST_USERS.cajeroEmpresarial;
export const SUPERVISOR = TEST_USERS.supervisor;
export const BODEGUERO = TEST_USERS.bodeguero;
export const DUENO = TEST_USERS.dueno;
export const SUPERADMIN = TEST_USERS.superadmin;
