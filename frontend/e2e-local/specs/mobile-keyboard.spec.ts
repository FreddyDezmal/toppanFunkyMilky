import { devices, expect, test, type Locator, type Page } from '@playwright/test'
import { useTestCheckout } from '../support/providers'
import { expectNoSeriousAccessibilityProblems, PATRON_PASSWORD } from '../support/stack'

/**
 * M13 E2E-14 (FR-28): the patron journey on a phone-sized screen and by keyboard alone, with axe checks on every page.
 * Chromium project only (touch emulation is not supported in Firefox); the payment journey already runs on the
 * Mobile (Pixel 7) project as well.
 */
const CONFIRMED = 'Payment received. Your order is confirmed.'

test.beforeEach(async ({ page }) => {
  await useTestCheckout(page)
})

/** Fails if the page is wider than the screen (sideways scrolling on a phone). */
async function expectNoSidewaysScroll(page: Page, pageName: string): Promise<void> {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(overflow, `${pageName} scrolls sideways by ${overflow}px`).toBeLessThanOrEqual(0)
}

async function checkPage(page: Page, pageName: string): Promise<void> {
  await expectNoSidewaysScroll(page, pageName)
  await expectNoSeriousAccessibilityProblems(page, pageName)
}

/** Presses Tab until `target` has focus: proves it can be reached by keyboard. */
async function tabTo(page: Page, target: Locator, max = 80): Promise<void> {
  await target.waitFor()
  for (let i = 0; i < max; i++) {
    if (await target.evaluate((element) => element === document.activeElement)) {
      return
    }
    await page.keyboard.press('Tab')
  }
  throw new Error(`Could not reach ${target.toString()} with the Tab key in ${max} presses`)
}

async function typeInto(page: Page, field: Locator, text: string): Promise<void> {
  await tabTo(page, field)
  await page.keyboard.type(text)
}

/** Chooses the next option of a closed list with the arrow key (the first real option after the placeholder). */
async function chooseFirst(page: Page, select: Locator): Promise<void> {
  await tabTo(page, select)
  await page.keyboard.press('ArrowDown')
  await expect(select).not.toHaveValue('')
}

async function press(page: Page, control: Locator, key: 'Enter' | 'Space' = 'Enter'): Promise<void> {
  await tabTo(page, control)
  await page.keyboard.press(key)
}

test.describe('on a phone-sized screen', () => {
  const { viewport, userAgent, deviceScaleFactor, isMobile, hasTouch } = devices['Pixel 7']
  test.use({ viewport, userAgent, deviceScaleFactor, isMobile, hasTouch })

  test('E2E-14 a patron registers, orders, pays and finds the order in My orders, with no sideways scrolling and no serious accessibility problems', async ({
    page,
  }) => {
    test.setTimeout(180_000)
    const email = `e2e-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.test`
    await page.goto('/register')
    await checkPage(page, 'Register (phone)')
    await page.getByLabel('First name').fill('Thandi')
    await page.getByLabel('Email address').fill(email)
    await page.getByLabel('Mobile number').fill('+27 82 123 4567')
    await page.getByLabel('Password', { exact: true }).fill(PATRON_PASSWORD)
    await page.getByLabel('Confirm password').fill(PATRON_PASSWORD)
    await page.getByRole('button', { name: 'Create account' }).tap()
    await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
    await checkPage(page, 'My account (phone)')

    await page.goto('/order')
    await checkPage(page, 'Order – quantity (phone)')
    await page.getByLabel('Number of milkshakes').selectOption('1')
    await page.getByRole('button', { name: 'Continue' }).tap()
    const card = page.getByRole('listitem', { name: 'Milkshake 1', exact: true })
    for (const field of ['Flavour', 'Consistency', 'Topping']) {
      await card.getByLabel(field, { exact: true }).selectOption({ index: 1 })
    }
    await checkPage(page, 'Order – milkshakes (phone)')
    await card.getByRole('button', { name: 'Done with milkshake 1' }).tap()
    await expect(page.getByRole('region', { name: 'Estimated price' }).getByText('Estimated total', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Continue to pickup' }).tap()
    await page.getByLabel('Pickup restaurant').selectOption({ index: 1 })
    const time = page.getByLabel('Pickup date and time')
    await time.fill((await time.getAttribute('max')) ?? '')
    await checkPage(page, 'Order – pickup (phone)')
    await page.getByRole('button', { name: 'Review order' }).tap()
    await expect(page.getByRole('region', { name: 'Estimated price' }).getByText('Estimated total', { exact: true })).toBeVisible()
    await checkPage(page, 'Order – review (phone)')
    await page.getByRole('button', { name: 'Place order' }).tap()
    await page.getByRole('alertdialog').getByRole('button', { name: 'Place order' }).tap()
    await expect(page).toHaveURL(/\/orders\/MS-/)
    const reference = ((await page.getByRole('heading', { name: /^Order MS-/ }).textContent()) ?? '').replace(/^Order /, '').trim()
    await checkPage(page, 'Order page – not paid (phone)')

    await page.getByRole('button', { name: 'Pay now' }).tap()
    await page.getByRole('link', { name: 'Pay (test)' }).tap()
    await expect(page.getByText(CONFIRMED)).toBeVisible()
    await expect(page.getByText('Paid', { exact: true })).toBeVisible()
    await checkPage(page, 'Order page – paid (phone)')

    await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'My orders' }).tap()
    await expect(page.getByRole('link', { name: `Order ${reference}` })).toBeVisible()
    await checkPage(page, 'My orders (phone)')
  })
})

test('E2E-14 a patron registers, orders and pays using only the keyboard', async ({ page }) => {
  test.setTimeout(180_000)
  const email = `e2e-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}@example.test`

  await page.goto('/register')
  await typeInto(page, page.getByLabel('First name'), 'Thandi')
  await typeInto(page, page.getByLabel('Email address'), email)
  await typeInto(page, page.getByLabel('Mobile number'), '+27 82 123 4567')
  await typeInto(page, page.getByLabel('Password', { exact: true }), PATRON_PASSWORD)
  await typeInto(page, page.getByLabel('Confirm password'), PATRON_PASSWORD)
  await press(page, page.getByRole('button', { name: 'Create account' }))
  await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()

  // From the header to the order screens by keyboard.
  await press(page, page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Order', exact: true }))
  await expect(page).toHaveURL(/\/order$/)
  await chooseFirst(page, page.getByLabel('Number of milkshakes'))
  await press(page, page.getByRole('button', { name: 'Continue' }))

  const card = page.getByRole('listitem', { name: 'Milkshake 1', exact: true })
  for (const field of ['Flavour', 'Consistency', 'Topping']) {
    await chooseFirst(page, card.getByLabel(field, { exact: true }))
  }
  await press(page, card.getByRole('button', { name: 'Done with milkshake 1' }))
  await expect(page.getByRole('region', { name: 'Estimated price' }).getByText('Estimated total', { exact: true })).toBeVisible()
  await press(page, page.getByRole('button', { name: 'Continue to pickup' }))

  await chooseFirst(page, page.getByLabel('Pickup restaurant'))
  // The pickup time is reached by Tab; its value is set directly because the native date picker's keyboard entry
  // differs by operating system and locale, which would test the browser rather than the application.
  const time = page.getByLabel('Pickup date and time')
  await tabTo(page, time)
  await time.fill((await time.getAttribute('max')) ?? '')
  await press(page, page.getByRole('button', { name: 'Review order' }))

  await press(page, page.getByRole('button', { name: 'Place order' }))
  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toBeVisible()
  // Focus starts inside the dialog, on the safe choice, and Tab moves to Place order without leaving the dialog.
  expect(await dialog.evaluate((element) => element.contains(document.activeElement))).toBe(true)
  await press(page, dialog.getByRole('button', { name: 'Place order' }))
  await expect(page).toHaveURL(/\/orders\/MS-/)

  await press(page, page.getByRole('button', { name: 'Pay now' }))
  await press(page, page.getByRole('link', { name: 'Pay (test)' }))
  await expect(page.getByText(CONFIRMED)).toBeVisible()
  await expect(page.getByText('Paid', { exact: true })).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Order page – paid (keyboard)')
})
