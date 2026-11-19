import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

/**
 * Manager journey through the M10 screens against the real stack (React → ASP.NET Core → Supabase).
 * Credentials come only from environment variables set in your terminal; they are never stored in the repository.
 * The flavour created here is deactivated at the end (records are never deleted); no setting is changed.
 */
const managerEmail = process.env.E2E_MANAGER_EMAIL ?? ''
const managerPassword = process.env.E2E_MANAGER_PASSWORD ?? ''

/** A datetime-local value for tomorrow at 09:00 in this machine's time zone (the browser's too). */
function tomorrowAtNine(): string {
  const date = new Date()
  date.setDate(date.getDate() + 1)
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T09:00`
}

/** Fails on serious or critical WCAG 2 A/AA problems in the page's main content, listing each one. */
async function expectNoSeriousAccessibilityProblems(page: Page, pageName: string) {
  const results = await new AxeBuilder({ page }).include('main').withTags(['wcag2a', 'wcag2aa']).analyze()
  const serious = results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${pageName}: ${violation.id}: ${violation.help} (${violation.nodes.length} element(s))`)
  expect(serious).toEqual([])
}

function section(page: Page, name: string) {
  return page.getByRole('navigation', { name: 'Management sections' }).getByRole('link', { name, exact: true })
}

test('a patron cannot open the management screens', async ({ page }) => {
  const password = 'Milkshake1Test'
  await page.goto('/register')
  await page.getByLabel('First name').fill('Playwright')
  await page.getByLabel('Email address').fill(`e2e-patron-${Date.now()}@example.test`)
  await page.getByLabel('Mobile number').fill('+27 82 123 4567')
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByLabel('Confirm password').fill(password)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()

  for (const path of ['/manager/catalog/flavours', '/manager/settings', '/manager/discount-tiers']) {
    await page.goto(path)
    await expect(page.getByRole('heading', { name: 'Access denied' })).toBeVisible()
  }
})

test.describe('manager journey', () => {
  test.skip(!managerEmail || !managerPassword, 'Set E2E_MANAGER_EMAIL and E2E_MANAGER_PASSWORD to run the manager journey.')

  test('manages a flavour and its prices, meets a settings limit and sees discounts blocked', async ({ page }) => {
    const name = `E2E Flavour ${Date.now()}`

    // Sign in and open the flavours list.
    await page.goto('/login')
    await page.getByLabel('Email address').fill(managerEmail)
    await page.getByLabel('Password').fill(managerPassword)
    await page.getByRole('button', { name: 'Log in' }).click()
    await page.getByRole('link', { name: 'Management', exact: true }).click()
    await section(page, 'Flavours').click()
    await expect(page.getByRole('heading', { name: 'Flavours' })).toBeVisible()
    await expect(page.getByRole('table')).toBeVisible()
    await expectNoSeriousAccessibilityProblems(page, 'Flavours')

    // Add a flavour with its starting price (comma decimal separator).
    await page.getByRole('button', { name: 'Add flavour' }).click()
    await page.getByLabel('Name', { exact: true }).fill(name)
    await page.getByLabel('Price', { exact: true }).fill('12,50')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText(`Added “${name}”.`)).toBeVisible()
    await expect(page.getByRole('row', { name: new RegExp(name) })).toBeVisible()

    // Schedule a price for tomorrow, then cancel it.
    await page.getByRole('link', { name: `Prices for ${name}` }).click()
    await expect(page.getByRole('heading', { name: `Prices: ${name}` })).toBeVisible()
    await page.getByLabel('New price').fill('13')
    await page.getByLabel('At a later date and time').check()
    await page.getByLabel('Starts at').fill(tomorrowAtNine())
    await page.getByRole('button', { name: 'Schedule price' }).click()
    await expect(page.getByText(/will apply from/)).toBeVisible()
    await expect(page.getByText('Scheduled', { exact: true })).toBeVisible()
    await expectNoSeriousAccessibilityProblems(page, 'Prices')

    await page.getByRole('button', { name: /Cancel the scheduled price/ }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel scheduled price' }).click()
    await expect(page.getByText(/was cancelled/)).toBeVisible()
    await expect(page.getByText('Scheduled', { exact: true })).toHaveCount(0)

    // Deactivate the test flavour (records are never deleted).
    await page.getByRole('link', { name: /Back to flavours/ }).click()
    await page.getByRole('button', { name: `Deactivate ${name}` }).click()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Deactivate' }).click()
    await expect(page.getByText(`“${name}” is hidden from new orders. Its history is kept.`)).toBeVisible()

    // Settings: an out-of-range value is refused by the server; nothing is changed.
    await section(page, 'Settings').click()
    await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible()
    await expect(page.getByText('Read-only', { exact: true })).toBeVisible()
    await expectNoSeriousAccessibilityProblems(page, 'Settings')
    await page.getByRole('button', { name: 'Edit Maximum drinks per order' }).click()
    await page.getByLabel('Maximum drinks per order', { exact: true }).fill('51')
    await page.getByRole('button', { name: 'Save' }).click()
    await expect(page.getByText('Maximum drinks per order cannot be more than 50.')).toBeVisible()
    await page.getByRole('button', { name: 'Cancel', exact: true }).click()

    // Discount tiers: editable, but activation is switched off until Q-01 to Q-04 are answered.
    await section(page, 'Discount tiers').click()
    await expect(page.getByText('Discounts are switched off for now')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Activate tier 1' })).toBeDisabled()
    await expectNoSeriousAccessibilityProblems(page, 'Discount tiers')
  })
})