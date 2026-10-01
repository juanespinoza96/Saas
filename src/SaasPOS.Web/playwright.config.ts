import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: process.env.CI ? 4 : 2,
  reporter: process.env.CI
    ? [['html'], ['junit', { outputFile: 'test-results/e2e-results.xml' }]]
    : [['html']],

  use: {
    baseURL: 'http://localhost:3000',
    screenshot: 'only-on-failure',
    trace: 'on-first-retry',
    video: 'retain-on-failure',
  },

  // Setup project to generate auth states
  projects: [
    {
      name: 'setup',
      testMatch: /.*\.setup\.ts/,
    },
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        storageState: '.auth/gerente.json',
      },
      dependencies: ['setup'],
    },
  ],

  globalSetup: './e2e/fixtures/seed-data.ts',

  webServer: [
    {
      command: 'npm run dev',
      url: 'http://localhost:3000',
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:3001',
      cwd: '../SaasPOS.Admin',
      reuseExistingServer: !process.env.CI,
      timeout: 30_000,
    },
  ],
});
