import { expect, test } from '@playwright/test'

test('a patron can register, is kept out of manager pages, signs out and signs back in', async ({ page }) => {
  const email = `e2e-${Date.now()}@example.test`
  const password = 'Milkshake1Test'

  await page.goto('/register')
  await page.getByLabel('First name').fill('Playwright')
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Mobile number').fill('+27 82 123 4567')
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Confirm password').fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()

  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()

  await page.goto('/manager')
  await expect(page.getByRole('heading', { name: 'Access denied' })).toBeVisible()

  await page.getByRole('button', { name: 'Log out' }).click()
  await expect(page.getByRole('heading', { name: 'Log in to your account' })).toBeVisible()

  await page.goto('/account')
  await expect(page.getByRole('heading', { name: 'Log in to your account' })).toBeVisible()

  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
})