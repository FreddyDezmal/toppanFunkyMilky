import { expect, test } from '@playwright/test'

test('home page reaches the API and the database', async ({ page }) => {
  await page.goto('/')

  const statusCard = page.getByRole('region', { name: 'System status' })
  await expect(statusCard).toBeVisible()

  // The first database check after API start-up can take a few seconds (connection + TLS).
  await expect(statusCard.getByText('Database connection succeeded.')).toBeVisible({ timeout: 30_000 })
  await expect(statusCard.getByText('API not reachable')).toHaveCount(0)
})