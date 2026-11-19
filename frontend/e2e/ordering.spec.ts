import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

/**
 * Patron ordering journey (M5) against the real stack (React → ASP.NET Core → Supabase).
 * Needs at least one active, priced flavour, consistency and topping and one active restaurant in the development
 * database (the seed data provides them). Each test registers a new patron, so no credentials are stored.
 * Nothing is ordered or paid: M5 only builds and validates a draft, and the draft never reaches the database.
 */
const PASSWORD = 'Milkshake1Test'
const DRAFT_KEY_PREFIX = 'milkshake.orderDraft.v1:'
const managerEmail = process.env.E2E_MANAGER_EMAIL ?? ''
const managerPassword = process.env.E2E_MANAGER_PASSWORD ?? ''

async function registerPatron(page: Page): Promise<string> {
  const email = `e2e-order-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.test`
  await page.goto('/register')
  await page.getByLabel('First name').fill('Playwright')
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Mobile number').fill('+27 82 123 4567')
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD)
  await page.getByLabel('Confirm password').fill(PASSWORD)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
  return email
}

/** Fails on serious or critical WCAG 2 A/AA problems in the page's main content, listing each one. */
async function expectNoSeriousAccessibilityProblems(page: Page, pageName: string) {
  const results = await new AxeBuilder({ page }).include('main').withTags(['wcag2a', 'wcag2aa']).analyze()
  const serious = results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${pageName}: ${violation.id}: ${violation.help} (${violation.nodes.length} element(s))`)
  expect(serious).toEqual([])
}

/** The values of every stored order draft in this tab. */
function storedDrafts(page: Page): Promise<string[]> {
  return page.evaluate(
    (prefix) =>
      Object.keys(sessionStorage)
        .filter((key) => key.startsWith(prefix))
        .map((key) => sessionStorage.getItem(key) ?? ''),
    DRAFT_KEY_PREFIX,
  )
}

/** True when the page is wider than the window (content would need sideways scrolling). */
function overflowsHorizontally(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
}

test('a patron builds a draft: quantity, milkshakes, pickup and a server-checked review', async ({ page }) => {
  test.setTimeout(120_000)
  const email = await registerPatron(page)

  await page.goto('/')
  await page.getByRole('link', { name: 'Start an order' }).click()

  // Quantity: within the configured limits.
  await expect(page.getByRole('heading', { name: 'How many milkshakes?' })).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Quantity')
  await page.getByLabel('Number of milkshakes').selectOption('2')
  await page.getByRole('button', { name: 'Continue' }).click()

  // Milkshakes: one flavour, consistency and topping each (the first real option in every list).
  await expect(page.getByRole('heading', { name: 'Choose your milkshakes' })).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Milkshakes')
  for (const number of [1, 2]) {
    const card = page.getByRole('listitem', { name: `Milkshake ${number}`, exact: true })
    for (const label of ['Flavour', 'Consistency', 'Topping']) {
      await card.getByLabel(label, { exact: true }).selectOption({ index: 1 })
    }
    await card.getByRole('button', { name: `Done with milkshake ${number}` }).click()
    await expect(card.getByRole('button', { name: `Edit milkshake ${number}` })).toBeFocused()
  }
  await page.getByRole('button', { name: 'Continue to pickup' }).click()

  // Pickup: an active restaurant and the latest allowed time (always inside the window, whatever the time now).
  await expect(page.getByRole('heading', { name: 'Where and when?' })).toBeVisible()
  await expect(page.getByText(/Times are in .*Africa\/Johannesburg/)).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Pickup')
  await page.getByLabel('Pickup restaurant').selectOption({ index: 1 })
  const time = page.getByLabel('Pickup date and time')
  const latest = await time.getAttribute('max')
  expect(latest).toMatch(/^\d{4}-\d{2}-\d{2}T23:59$/)
  await time.fill(latest ?? '')
  await page.getByRole('button', { name: 'Review order' }).click()

  // Review: what the server confirmed. Fees only, no total, nothing ordered or paid.
  await expect(page.getByRole('heading', { name: 'Review your order' })).toBeVisible()
  await expect(page.getByText(/^Draft – not yet ordered or paid/)).toBeVisible()
  await expect(page.getByRole('heading', { name: '2 milkshakes' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Continue to payment (coming soon)' })).toBeDisabled()
  await expect(page.getByText(/total/i)).toHaveCount(0)
  await expectNoSeriousAccessibilityProblems(page, 'Review')

  // A refresh keeps the draft; storage holds identifiers and the pickup time only.
  await page.reload()
  await expect(page.getByRole('heading', { name: '2 milkshakes' })).toBeVisible()
  const stored = await storedDrafts(page)
  expect(stored).toHaveLength(1)
  expect(stored[0]).not.toMatch(/price|fee|amount|name|total/i)

  // Signing out clears the draft; signing back in starts empty.
  await page.getByRole('button', { name: 'Log out' }).click()
  await expect(page.getByRole('heading', { name: 'Log in to your account' })).toBeVisible()
  expect(await storedDrafts(page)).toEqual([])

  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password').fill(PASSWORD)
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()
  await page.goto('/order/review')
  await expect(page.getByRole('heading', { name: 'Your order is empty' })).toBeVisible()
})

test('forged prices and tampered drafts are refused, and the screens fit a phone', async ({ page }) => {
  await registerPatron(page)

  // Straight to the API from the patron's own session: a price in the body is rejected, not ignored.
  const forged = await page.evaluate(async () => {
    const { token } = (await (await fetch('/api/auth/csrf', { credentials: 'include' })).json()) as { token: string }
    const body = JSON.stringify({
      restaurantId: 1,
      pickupLocal: null,
      drinks: [{ flavourId: 1, consistencyId: 1, toppingId: 1, price: 0.01 }],
    })
    const headers = { 'Content-Type': 'application/json' }
    const withToken = await fetch('/api/ordering/draft/validate', {
      method: 'POST',
      credentials: 'include',
      headers: { ...headers, 'X-XSRF-TOKEN': token },
      body,
    })
    const withoutToken = await fetch('/api/ordering/draft/validate', { method: 'POST', credentials: 'include', headers, body })
    return { withToken: withToken.status, withoutToken: withoutToken.status }
  })
  expect(forged).toEqual({ withToken: 400, withoutToken: 400 })

  // A draft edited in sessionStorage is rebuilt on load: unknown ids are cleared with a notice, extras are dropped.
  const userId = await page.evaluate(async () => ((await (await fetch('/api/auth/me')).json()) as { id: string }).id)
  await page.evaluate(
    ([key]) =>
      sessionStorage.setItem(
        key ?? '',
        JSON.stringify({
          restaurantId: 999_999_999,
          pickupLocal: '2000-01-01T00:00',
          price: 0.01,
          drinks: [{ flavourId: 999_999_999, consistencyId: 'x', toppingId: null, fee: 0 }],
        }),
      ),
    [`${DRAFT_KEY_PREFIX}${userId}`],
  )
  await page.goto('/order/drinks')

  await expect(page.getByText('The flavour chosen for milkshake 1 is no longer available. Please choose another.')).toBeVisible()
  await expect(page.getByText('The selected restaurant is no longer available. Please choose another.')).toBeVisible()
  await expect(page.getByLabel('Flavour', { exact: true })).toHaveValue('')
  const stored = await storedDrafts(page)
  expect(stored.join('')).not.toMatch(/price|fee|999999999/)

  // The same patron on a phone-sized screen: nothing needs sideways scrolling, and axe stays clean.
  // (Kept in this test so the suite registers fewer patrons: registration is rate-limited per IP.)
  await page.setViewportSize({ width: 375, height: 812 })
  await page.goto('/order')
  await expect(page.getByRole('heading', { name: 'How many milkshakes?' })).toBeVisible()
  expect(await overflowsHorizontally(page)).toBe(false)
  await page.getByLabel('Number of milkshakes').selectOption('1')
  await page.getByRole('button', { name: 'Continue' }).click()
  await expect(page.getByRole('heading', { name: 'Choose your milkshakes' })).toBeVisible()
  expect(await overflowsHorizontally(page)).toBe(false)
  await expectNoSeriousAccessibilityProblems(page, 'Milkshakes (phone)')
})

test('anonymous visitors are sent to log in', async ({ page }) => {
  await page.goto('/order')
  await expect(page.getByRole('heading', { name: 'Log in to your account' })).toBeVisible()

  const status = await page.evaluate(async () => (await fetch('/api/ordering/options')).status)
  expect(status).toBe(401)
})

test.describe('manager', () => {
  test.skip(!managerEmail || !managerPassword, 'Set E2E_MANAGER_EMAIL and E2E_MANAGER_PASSWORD to run the manager check.')

  test('cannot open the ordering screens (patron-only, open question Q-19)', async ({ page }) => {
    await page.goto('/login')
    await page.getByLabel('Email address').fill(managerEmail)
    await page.getByLabel('Password').fill(managerPassword)
    await page.getByRole('button', { name: 'Log in' }).click()
    await expect(page.getByRole('button', { name: 'Log out' })).toBeVisible()

    await expect(page.getByRole('link', { name: 'Order', exact: true })).toHaveCount(0)
    await page.goto('/order')
    await expect(page.getByRole('heading', { name: 'Access denied' })).toBeVisible()
  })
})