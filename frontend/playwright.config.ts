import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end smoke tests against the real stack: React -> ASP.NET Core API -> Supabase.
 * Requires the API's user secrets (database connection) to be configured on this machine.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: 'list',
    // The dev database is remote (≈190 ms per query from here); registration and login chain many queries.
  expect: { timeout: 20_000 },
  use: {
    baseURL: 'http://localhost:5173',
    ignoreHTTPSErrors: true, // local ASP.NET Core development certificate
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: 'dotnet run --project ../backend/src/Milkshake.Api --launch-profile https',
      url: 'https://localhost:7298/api/health',
      ignoreHTTPSErrors: true,
      reuseExistingServer: !process.env.CI,
      timeout: 180_000,
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
  ],
})