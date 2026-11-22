import AxeBuilder from '@axe-core/playwright'
import { expect, test, type APIRequestContext, type Browser, type Locator, type Page } from '@playwright/test'

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
  await page.getByLabel('Confirm password', { exact: true }).fill(PATRON_PASSWORD)
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

/**
 * A link in the main navigation. On phone-sized screens the navigation is folded behind the "Menu" button (UI/UX
 * redesign), so the menu is opened first when that button is showing.
 */
export async function mainNavLink(page: Page, name: string): Promise<Locator> {
  const menu = page.getByRole('button', { name: 'Menu' })
  if ((await menu.isVisible()) && (await menu.getAttribute('aria-expanded')) !== 'true') {
    await menu.click()
  }
  return page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name, exact: true })
}

/** Escapes text for use inside a regular expression. */
function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

/**
 * Chooses a card in a group of choice cards (UI/UX redesign: radio cards replaced the drop-downs) by clicking its
 * card, as a patron would. `option` is the start of the option's name, or its position in the group (0 = first).
 * Each radio's accessible name is the option's name, a comma and its fee (or the restaurant's address); browsers may
 * put a space before the comma, because the name and the fee are separate blocks on the card.
 */
export async function chooseCard(scope: Page | Locator, group: string, option: string | number = 0): Promise<void> {
  const fieldset = scope.getByRole('group', { name: group, exact: true })
  const radio =
    typeof option === 'number'
      ? fieldset.getByRole('radio').nth(option)
      : fieldset.getByRole('radio', { name: new RegExp(`^${escapeRegExp(option)}\\s*,`) })
  await radio.locator('xpath=ancestor::label[1]').click()
  await expect(radio).toBeChecked()
}

/** Sets the number of milkshakes on the quantity step (a number box with − and + buttons since the redesign). */
export async function setQuantity(page: Page, quantity: number): Promise<void> {
  const field = page.getByLabel('Number of milkshakes')
  await field.fill(String(quantity))
  await expect(field).toHaveValue(String(quantity))
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
 * group is used, or the named flavour (matched by the start of its card's name, which also carries the fee).
 */
export async function reviewOrder(page: Page, options: { flavour?: string } = {}): Promise<string> {
  await page.goto('/order')
  await setQuantity(page, 1)
  await page.getByRole('button', { name: 'Continue' }).click()
  const card = page.getByRole('listitem', { name: 'Milkshake 1', exact: true })
  await chooseCard(card, 'Flavour', options.flavour ?? 0)
  await chooseCard(card, 'Consistency')
  await chooseCard(card, 'Topping')
  await card.getByRole('button', { name: 'Done with milkshake 1' }).click()
  // Wait for the server's estimate as a patron would read it before continuing (the space is reserved since DEF-04).
  await expect(page.getByRole('region', { name: 'Estimated price' }).getByRole('status')).toHaveText(/^Estimated total .+\.$/)
  await page.getByRole('button', { name: 'Continue to pickup' }).click()
  await chooseCard(page, 'Pickup restaurant')
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
    .map((violation) => {
      // Name each element and axe's reason (e.g. the contrast ratio), so a failure can be fixed without a re-run.
      const elements = violation.nodes.map((node) => `${node.target.join(' ')} – ${(node.failureSummary ?? '').split('\n').slice(1).join(' ').trim()}`)
      return `${pageName}: ${violation.id}: ${violation.help} (${violation.nodes.length} element(s)): ${elements.join('; ')}`
    })
  expect(serious).toEqual([])
}

/** GET JSON from a stack service (simulator or Mailpit), failing with the body on an error status. */
export async function getJson<T>(request: APIRequestContext, url: string): Promise<T> {
  const response = await request.get(url)
  expect(response.ok(), `GET ${url}: ${response.status()} ${await response.text()}`).toBeTruthy()
  return (await response.json()) as T
}
