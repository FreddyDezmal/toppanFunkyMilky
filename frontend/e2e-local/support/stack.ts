import AxeBuilder from '@axe-core/playwright'
import { expect, test, type APIRequestContext, type Browser, type Page } from '@playwright/test'

/** Addresses and credentials passed in memory by e2e-local/run.mjs. */
export const stack = {
  simulator: required('E2E_SIMULATOR_URL'),
  mailpit: required('E2E_MAILPIT_URL'),
  managerEmail: required('E2E_LOCAL_MANAGER_EMAIL'),
  managerPassword: required('E2E_LOCAL_MANAGER_PASSWORD'),
}

function required(name: string): string {
  const value = process.env[name]
  if (!value) {
    throw new Error(`${name} is not set: run these tests with \`npm run test:e2e:local\`.`)
  }
  return value
}

export const PATRON_PASSWORD = 'Milkshake1Test'

/** Registers a new patron through the UI and returns their email address (unique per call). */
export async function registerPatron(page: Page): Promise<string> {
  const email = `e2e-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.test`
  await page.goto('/register')
  await page.getByLabel('First name').fill('Thandi')
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Mobile number').fill('+27 82 123 4567')
  await page.getByLabel('Password', { exact: true }).fill(PATRON_PASSWORD)
  await page.getByLabel('Confirm password').fill(PATRON_PASSWORD)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
  return email
}

/** Signs in through the UI (a fresh browser context per role keeps the sessions apart). */
export async function signIn(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Log in' }).click()
  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
}

/** A page signed in as the E2E manager, in its own browser context. */
export async function managerPage(browser: Browser): Promise<Page> {
  const context = await browser.newContext({ ignoreHTTPSErrors: true })
  const page = await context.newPage()
  await signIn(page, stack.managerEmail, stack.managerPassword)
  return page
}

export interface PlacedOrder {
  reference: string
  /** The total exactly as shown, e.g. "R 29,90". */
  totalText: string
}

/**
 * Builds a one-milkshake order through the UI with the first option in each list and the latest pickup time, confirms
 * the server's total and places it. The local database holds only seed data plus what the tests add.
 */
/**
 * Builds a one-milkshake order up to the review step and returns the estimated total shown there. The first option of
 * each list is used, or the named flavour (matched by the start of its option text, which also shows the fee).
 */
export async function reviewOrder(page: Page, options: { flavour?: string } = {}): Promise<string> {
  await page.goto('/order')
  await page.getByLabel('Number of milkshakes').selectOption('1')
  await page.getByRole('button', { name: 'Continue' }).click()
  const card = page.getByRole('listitem', { name: 'Milkshake 1', exact: true })
  for (const field of ['Flavour', 'Consistency', 'Topping']) {
    const select = card.getByLabel(field, { exact: true })
    if (field === 'Flavour' && options.flavour) {
      const value = await select.locator('option').filter({ hasText: `${options.flavour} (` }).getAttribute('value')
      await select.selectOption(value ?? '')
    } else {
      await select.selectOption({ index: 1 })
    }
  }
  await card.getByRole('button', { name: 'Done with milkshake 1' }).click()
  // The estimate appears above the button once the server has priced the milkshake; wait for it as a patron would,
  // so the click is not moved by that layout change (M13 DEF-04).
  await expect(page.getByRole('region', { name: 'Estimated price' }).getByText('Estimated total', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Continue to pickup' }).click()
  await page.getByLabel('Pickup restaurant').selectOption({ index: 1 })
  const time = page.getByLabel('Pickup date and time')
  await time.fill((await time.getAttribute('max')) ?? '')
  await page.getByRole('button', { name: 'Review order' }).click()

  const reviewPrice = page.getByRole('region', { name: 'Estimated price' })
  await expect(reviewPrice.getByText('Estimated total', { exact: true })).toBeVisible()
  return ((await reviewPrice.locator('dd').last().textContent()) ?? '').trim()
}

export async function placeOrder(page: Page, options: { flavour?: string } = {}): Promise<PlacedOrder> {
  const totalText = await reviewOrder(page, options)
  await page.getByRole('button', { name: 'Place order' }).click()
  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toContainText(totalText)
  await dialog.getByRole('button', { name: 'Place order' }).click()

  await expect(page).toHaveURL(/\/orders\/MS-[0-9A-Z]{4}-[0-9A-Z]{4}$/)
  const heading = page.getByRole('heading', { name: /^Order MS-/ })
  const reference = ((await heading.textContent()) ?? '').replace(/^Order /, '').trim()
  return { reference, totalText }
}

/** "R 29,90" (any locale's grouping and decimal mark) → 2990 cents. */
export function centsOf(totalText: string): number {
  const digits = totalText.replace(/[^\d,.]/g, '')
  const [whole, fraction = '00'] = digits.split(/[,.](?=\d{2}$)/)
  return Number(whole.replace(/[,.]/g, '')) * 100 + Number(fraction.padEnd(2, '0'))
}

/**
 * Fails on serious or critical WCAG 2 A/AA problems in the page's main content, listing each one.
 * Not run in Firefox (M13 DEF-02): Playwright's Firefox build crashes the page at random points while axe runs, on any
 * rule. axe inspects the DOM, so its findings do not depend on the engine; Chromium, WebKit and Mobile scan every page.
 * Firefox still runs every functional check, and the skip is recorded on the test.
 */
export async function expectNoSeriousAccessibilityProblems(page: Page, pageName: string): Promise<void> {
  if (page.context().browser()?.browserType().name() === 'firefox') {
    test.info().annotations.push({ type: 'accessibility scan skipped', description: `${pageName}: Firefox, M13 DEF-02` })
    return
  }
  const results = await new AxeBuilder({ page }).include('main').withTags(['wcag2a', 'wcag2aa']).analyze()
  const serious = results.violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => `${pageName}: ${violation.id}: ${violation.help} (${violation.nodes.length} element(s))`)
  expect(serious).toEqual([])
}

/** GET JSON from a stack service (simulator or Mailpit), failing with the body on an error status. */
export async function getJson<T>(request: APIRequestContext, url: string): Promise<T> {
  const response = await request.get(url)
  expect(response.ok(), `GET ${url}: ${response.status()} ${await response.text()}`).toBeTruthy()
  return (await response.json()) as T
}
