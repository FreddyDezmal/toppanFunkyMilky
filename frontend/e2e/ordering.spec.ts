import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Locator, type Page } from '@playwright/test'

/**
 * Patron ordering journey (M5) against the real stack (React → ASP.NET Core → Supabase).
 * Needs at least one active, priced flavour, consistency and topping and one active restaurant in the development
 * database (the seed data provides them). Each test registers a new patron, so no credentials are stored.
 * The draft journey orders nothing. The placement journey (M7) places ONE real, unpaid order per run for its own new
 * patron (decision O12); nothing is ever paid.
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

/**
 * Picks the first real card that no test created (UI/UX redesign: choice cards replaced the drop-downs). The manager
 * journey (run in parallel when manager credentials are set) adds an "E2E Flavour …" and deactivates it seconds later;
 * a draft holding it would correctly lose it.
 */
async function chooseFirstStableCard(group: Locator) {
  const labels = await group.locator('label').allTextContents()
  const index = labels.findIndex((label) => !label.trimStart().startsWith('E2E '))
  expect(index, 'Every option in this group was created by a test.').toBeGreaterThanOrEqual(0)
  await group.locator('label').nth(index).click()
  await expect(group.getByRole('radio').nth(index)).toBeChecked()
}

/** Picks a card whose fee differs from the chosen one (cards read "Name, + R 12,00"), so the total must change. */
async function chooseDifferentlyPricedCard(group: Locator) {
  // Everything after the hidden comma that follows the name (the fee itself contains a decimal comma).
  const fee = (label: string | null) => (label === null ? undefined : label.slice(label.indexOf(',') + 1).trim())
  const current = await group.locator('label:has(input:checked)').textContent()
  const labels = await group.locator('label').allTextContents()
  const other = labels.findIndex((label) => !label.trimStart().startsWith('E2E ') && fee(label) !== fee(current))
  test.skip(other < 0, 'Every option in this group has the same fee in this database.')
  await group.locator('label').nth(other).click()
  await expect(group.getByRole('radio').nth(other)).toBeChecked()
}

/** Sets the number of milkshakes (a number box with − and + buttons since the redesign). */
async function setQuantity(page: Page, quantity: number) {
  const field = page.getByLabel('Number of milkshakes')
  await field.fill(String(quantity))
  await expect(field).toHaveValue(String(quantity))
}

/** True when the page is wider than the window (content would need sideways scrolling). */
function overflowsHorizontally(page: Page): Promise<boolean> {
  return page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)
}

test('a patron builds a draft: quantity, milkshakes, pickup and a server-checked review', async ({ page }) => {
  test.setTimeout(120_000)
  const email = await registerPatron(page)

  await page.goto('/')
  await page.getByRole('link', { name: 'Build Your Milkshake' }).click()

  // Quantity: within the configured limits.
  await expect(page.getByRole('heading', { name: 'How many milkshakes?' })).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Quantity')
  await setQuantity(page, 2)
  await page.getByRole('button', { name: 'Continue' }).click()

  // Milkshakes: one flavour, consistency and topping each (the first real option in every list that no test created).
  await expect(page.getByRole('heading', { name: 'Choose your milkshakes' })).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Milkshakes')
  for (const number of [1, 2]) {
    const card = page.getByRole('listitem', { name: `Milkshake ${number}`, exact: true })
    for (const label of ['Flavour', 'Consistency', 'Topping']) {
      await chooseFirstStableCard(card.getByRole('group', { name: label, exact: true }))
    }
    await card.getByRole('button', { name: `Done with milkshake ${number}` }).click()
    await expect(card.getByRole('button', { name: `Edit milkshake ${number}` })).toBeFocused()
    await expect(card.getByText(/^Price:/)).toBeVisible()
  }

  // Live prices (M6): the server's estimate appears once every milkshake is complete, and follows a change.
  const estimate = page.getByRole('region', { name: 'Estimated price' }).getByRole('status')
  await expect(estimate).toHaveText(/^Estimated total .+\.$/)
  const firstTotal = await estimate.textContent()
  const second = page.getByRole('listitem', { name: 'Milkshake 2', exact: true })
  await second.getByRole('button', { name: 'Edit milkshake 2' }).click()
  await chooseDifferentlyPricedCard(second.getByRole('group', { name: 'Topping', exact: true }))
  await second.getByRole('button', { name: 'Done with milkshake 2' }).click()
  await expect(async () => {
    const text = await estimate.textContent()
    expect(text).toMatch(/^Estimated total .+\.$/)
    expect(text).not.toBe(firstTotal)
  }).toPass()
  const estimatedTotal = (await estimate.textContent())?.replace(/^Estimated total /, '').replace(/\.$/, '') ?? ''
  await page.getByRole('button', { name: 'Continue to pickup' }).click()

  // Pickup: an active restaurant and the latest allowed time (always inside the window, whatever the time now).
  await expect(page.getByRole('heading', { name: 'Where and when?' })).toBeVisible()
  await expect(page.getByText(/Times are in .*Africa\/Johannesburg/)).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Pickup')
  await chooseFirstStableCard(page.getByRole('group', { name: 'Pickup restaurant', exact: true }))
  const time = page.getByLabel('Pickup date and time')
  const latest = await time.getAttribute('max')
  expect(latest).toMatch(/^\d{4}-\d{2}-\d{2}T23:59$/)
  await time.fill(latest ?? '')
  await page.getByRole('button', { name: 'Review order' }).click()

  // Review: what the server confirmed. Fees only, no total, nothing ordered or paid.
  await expect(page.getByRole('heading', { name: 'Review your order' })).toBeVisible()
  await expect(page.getByText(/^Draft – not yet ordered or paid/)).toBeVisible()
  await expect(page.getByRole('heading', { name: '2 milkshakes' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Place order' })).toBeEnabled()
  // Server-calculated estimate: discount shown as none (deferred), VAT with its rate, and the same total as before.
  const reviewPrice = page.getByRole('region', { name: 'Estimated price' })
  await expect(reviewPrice.getByText('Estimated total', { exact: true })).toBeVisible()
  await expect(reviewPrice.getByText(/\(none\)$/)).toBeVisible()
  await expect(reviewPrice.getByText(/^VAT \(/)).toBeVisible()
  await expect(reviewPrice.getByText(estimatedTotal, { exact: true }).last()).toBeVisible()
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
    // A quote with a VAT rate or a discount of the browser's choosing is refused too.
    const forgedQuote = await fetch('/api/ordering/quote', {
      method: 'POST',
      credentials: 'include',
      headers: { ...headers, 'X-XSRF-TOKEN': token },
      body: JSON.stringify({ drinks: [], vatRate: 0, discountRate: 1 }),
    })
    return { withToken: withToken.status, withoutToken: withoutToken.status, forgedQuote: forgedQuote.status }
  })
  expect(forged).toEqual({ withToken: 400, withoutToken: 400, forgedQuote: 400 })

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
  await expect(page.getByRole('group', { name: 'Flavour', exact: true }).getByRole('radio', { checked: true })).toHaveCount(0)
  const stored = await storedDrafts(page)
  expect(stored.join('')).not.toMatch(/price|fee|999999999/)

  // The same patron on a phone-sized screen: nothing needs sideways scrolling, and axe stays clean.
  // (Kept in this test so the suite registers fewer patrons: registration is rate-limited per IP.)
  await page.setViewportSize({ width: 375, height: 812 })
  await page.goto('/order')
  await expect(page.getByRole('heading', { name: 'How many milkshakes?' })).toBeVisible()
  expect(await overflowsHorizontally(page)).toBe(false)
  await setQuantity(page, 1)
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
test('a patron places an order: confirmed total, stored unpaid, kept after a refresh, draft cleared', async ({ page }) => {
  test.setTimeout(120_000)
  await registerPatron(page)

  // A one-milkshake draft with the first real option in every list that no test created.
  await page.goto('/order')
  await setQuantity(page, 1)
  await page.getByRole('button', { name: 'Continue' }).click()
  const card = page.getByRole('listitem', { name: 'Milkshake 1', exact: true })
  for (const field of ['Flavour', 'Consistency', 'Topping']) {
    await chooseFirstStableCard(card.getByRole('group', { name: field, exact: true }))
  }
  await card.getByRole('button', { name: 'Done with milkshake 1' }).click()
  await page.getByRole('button', { name: 'Continue to pickup' }).click()
  await chooseFirstStableCard(page.getByRole('group', { name: 'Pickup restaurant', exact: true }))
  const time = page.getByLabel('Pickup date and time')
  await time.fill((await time.getAttribute('max')) ?? '')
  await page.getByRole('button', { name: 'Review order' }).click()

  // Review, then confirm the server's total.
  const reviewPrice = page.getByRole('region', { name: 'Estimated price' })
  await expect(reviewPrice.getByText('Estimated total', { exact: true })).toBeVisible()
  const total = (await reviewPrice.locator('dd').last().textContent()) ?? ''
  await page.getByRole('button', { name: 'Place order' }).click()
  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toContainText(total)
  await dialog.getByRole('button', { name: 'Place order' }).click()

  // The stored order: reference, unpaid, the same total; the draft and its submission key are gone.
  await expect(page).toHaveURL(/\/orders\/MS-[0-9A-Z]{4}-[0-9A-Z]{4}$/)
  const heading = page.getByRole('heading', { name: /^Order MS-/ })
  await expect(heading).toBeVisible()
  const reference = ((await heading.textContent()) ?? '').replace(/^Order /, '')
  await expect(page.getByText('Awaiting payment', { exact: true })).toBeVisible()
  await expect(page.getByText('Not paid', { exact: true })).toBeVisible()
  await expect(page.getByRole('region', { name: 'Price' }).locator('dd').last()).toHaveText(total)
  // Payment is offered but not started here: a real checkout needs the Paystack sandbox (manual check, M8 Step 8).
  await expect(page.getByRole('region', { name: 'Payment' }).getByRole('button', { name: 'Pay now' })).toBeEnabled()
  expect(await page.evaluate(() => Object.keys(sessionStorage).filter((key) => key.startsWith('milkshake.')))).toEqual([])
  await expectNoSeriousAccessibilityProblems(page, 'Order')

  // Still there after a refresh, read back from the server.
  await page.reload()
  await expect(page.getByRole('heading', { name: `Order ${reference}` })).toBeVisible()
  await expect(page.getByText('Not paid', { exact: true })).toBeVisible()

  // Listed under My orders, linking back to it.
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'My orders' }).click()
  await expect(page.getByRole('link', { name: `Order ${reference}` })).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'My orders')

  // Only the patron's own order is found; any other reference is not (same 404 as a missing order).
  const lookups = await page.evaluate(async (own) => {
    const status = async (ref: string) => (await fetch(`/api/ordering/orders/${ref}`, { credentials: 'include' })).status
    return { own: await status(own), other: await status('MS-0000-0000') }
  }, reference)
  expect(lookups).toEqual({ own: 200, other: 404 })

  await page.goto('/order/review')
  await expect(page.getByRole('heading', { name: 'Your order is empty' })).toBeVisible()
})
