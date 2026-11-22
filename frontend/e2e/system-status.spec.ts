import { expect, test } from '@playwright/test'

/**
 * The website reaches the API, and the API reaches the database.
 * Since the UI/UX redesign (D-R7) the System status card is on the manager overview instead of the public home page,
 * so the card is checked there when manager credentials are set; the health endpoint is checked on every run.
 */
const managerEmail = process.env.E2E_MANAGER_EMAIL ?? ''
const managerPassword = process.env.E2E_MANAGER_PASSWORD ?? ''

test('the API reports the database connection as healthy', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { level: 1, name: 'Your Milkshake. Your Rules.' })).toBeVisible()

  // The first database check after API start-up can take a few seconds (connection + TLS).
  await expect(async () => {
    const response = await page.request.get('/api/health')
    expect(response.ok()).toBe(true)
    const body = (await response.json()) as { checks: { description: string | null }[] }
    expect(body.checks.map((check) => check.description)).toContain('Database connection succeeded.')
  }).toPass({ timeout: 30_000 })
})

test('the manager overview shows the system status', async ({ page }) => {
  test.skip(!managerEmail || !managerPassword, 'Set E2E_MANAGER_EMAIL and E2E_MANAGER_PASSWORD to check the manager overview.')

  await page.goto('/login')
  await page.getByLabel('Email address').fill(managerEmail)
  await page.getByLabel('Password', { exact: true }).fill(managerPassword)
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
  await page.goto('/manager')

  const statusCard = page.getByRole('region', { name: 'System status' })
  await expect(statusCard).toBeVisible()
  await expect(statusCard.getByText('Database connection succeeded.')).toBeVisible({ timeout: 30_000 })
  await expect(statusCard.getByText('API not reachable')).toHaveCount(0)
})
