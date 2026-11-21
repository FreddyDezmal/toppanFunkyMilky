import { defineConfig, devices } from '@playwright/test'

/**
 * M13 end-to-end journeys on the LOCAL E2E stack (decisions D1 to D3, D6): React → ASP.NET Core ("E2E" environment) →
 * disposable PostgreSQL 17, with the Paystack simulator and a disposable Mailpit. Start it ONLY through
 * `npm run test:e2e:local`, which builds the stack, passes the addresses and the E2E manager's credentials in memory,
 * and removes everything afterwards. The Supabase smoke tests keep their own config (playwright.config.ts).
 */
if (process.env.E2E_LOCAL !== '1') {
  throw new Error('Run these tests with `npm run test:e2e:local`: they need the disposable local E2E stack.')
}

export default defineConfig({
  testDir: './e2e-local/specs',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0, // a retry would hide intermittent failures; M13 investigates them instead
  workers: 3,
  reporter: [['list'], ['junit', { outputFile: `${process.env.E2E_LOG_DIR ?? 'test-results'}/playwright-junit.xml` }]],
  outputDir: `${process.env.E2E_LOG_DIR ?? 'test-results'}/playwright`,
  timeout: 120_000,
  expect: { timeout: 20_000 },
  use: {
    baseURL: 'https://localhost:5173',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] }, testIgnore: /reports\.spec\.ts/ },
    { name: 'firefox', use: { ...devices['Desktop Firefox'] }, grep: /@all-browsers/ },
    { name: 'webkit', use: { ...devices['Desktop Safari'] }, grep: /@all-browsers/ },
    { name: 'mobile', use: { ...devices['Pixel 7'] }, grep: /@all-browsers/ },
    // Report totals are compared with exports and with each other, so they run once every other test has finished
    // placing orders (M13 E2E-11). If an earlier project fails, Playwright skips this one: fix that failure first.
    {
      name: 'reports',
      use: { ...devices['Desktop Chrome'] },
      testMatch: /reports\.spec\.ts/,
      dependencies: ['chromium', 'firefox', 'webkit', 'mobile'],
    },
  ],
})
