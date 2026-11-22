import { expect, test } from '@playwright/test'
import { countMailAfterQuietPeriod, mailMessage, paystack, useTestCheckout, waitForMail } from '../support/providers'
import { centsOf, expectNoSeriousAccessibilityProblems, mainNavLink, managerPage, placeOrder, registerPatron } from '../support/stack'

/**
 * M13 E2E-03, E2E-04, E2E-05 (MR-05, MR-06, MR-08, MR-09, MR-10; FR-06, FR-07) on the local E2E stack: real browser →
 * React → ASP.NET Core → PostgreSQL 17, with the Paystack simulator answering the server's calls and the test checkout
 * page served in this browser only. Critical invariant: an order is never confirmed because the browser says so.
 */
const CONFIRMED = 'Payment received. Your order is confirmed.'
const FAILED = 'Your payment did not go through. You have not been charged for this order. You can try again.'
const NEEDS_REFUND = 'We received a payment that could not be applied to this order, so it needs to be refunded. Please contact the restaurant.'
const receiptSubject = (reference: string) => `Order ${reference} confirmed – your receipt`

test.beforeEach(async ({ page }) => {
  await useTestCheckout(page)
})

test('@all-browsers a paid order is confirmed only after server verification, with one receipt, and shows as Paid to the patron and the manager', async ({
  page,
  browser,
}) => {
  const email = await registerPatron(page)
  const order = await placeOrder(page)
  await expect(page.getByText('Not paid', { exact: true })).toBeVisible()

  // The browser coming "back from checkout" without paying confirms nothing.
  await page.goto(`/orders/${order.reference}?payment=return`)
  await expect(page.getByText('Checking your payment…')).toHaveCount(0)
  await expect(page.getByText('Not paid', { exact: true })).toBeVisible()
  await expect(page.getByText(CONFIRMED)).toHaveCount(0)

  // Pay: the server, not the browser, chose the amount, the currency and where Paystack returns the patron.
  await page.goto(`/orders/${order.reference}`)
  await page.getByRole('button', { name: 'Pay now' }).click()
  await expect(page.getByRole('heading', { name: 'Test checkout (Paystack simulator)' })).toBeVisible()
  const tx = await paystack.latestFor(email)
  expect(tx.amount).toBe(centsOf(order.totalText))
  expect(tx.currency).toBe('ZAR')
  expect(tx.callbackUrl).toBe(`https://localhost:5173/orders/${order.reference}?payment=return`)
  await page.getByRole('link', { name: 'Pay (test)' }).click()

  // Back on the site: confirmed after the server verified the payment with "Paystack".
  await expect(page.getByText(CONFIRMED)).toBeVisible()
  await expect(page.getByText('Paid', { exact: true })).toBeVisible()
  await expect(page.getByText('Confirmed', { exact: true })).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Paid order')

  // Paystack retries its notification twice: accepted, and still one logical receipt.
  expect((await paystack.webhook(tx.reference, 2)).statuses).toEqual([200, 200])
  const receipt = await waitForMail(email, receiptSubject(order.reference))
  expect(await countMailAfterQuietPeriod(page, email, receiptSubject(order.reference))).toBe(1)
  const content = await mailMessage(receipt.ID)
  const amount = (tx.amount / 100).toFixed(2)
  expect(content.Text).toContain(order.reference)
  expect(content.Text).toMatch(new RegExp(amount.replace('.', '[.,]')))
  expect(content.HTML).toContain(`https://localhost:5173/orders/${order.reference}`)
  expect(content.HTML + content.Text).not.toMatch(/sk_test|sk_live|card|cvv/i)

  // Patron history.
  await (await mainNavLink(page, 'My orders')).click()
  const row = page.getByRole('link', { name: `Order ${order.reference}` }).locator('xpath=ancestor::li[1]')
  await expect(row).toContainText('Confirmed · Paid')

  // Manager report for today, found by reference.
  const manager = await managerPage(browser)
  await manager.goto(`/manager/reports?q=${order.reference}`)
  const reportRow = manager.getByRole('row').filter({ has: manager.getByRole('link', { name: order.reference }) })
  await expect(reportRow).toContainText('Paid')
  await manager.context().close()
})

test('a declined payment leaves the order unpaid and payable, with no receipt; paying again confirms it', async ({ page }) => {
  const email = await registerPatron(page)
  const order = await placeOrder(page)

  await page.getByRole('button', { name: 'Pay now' }).click()
  await page.getByRole('link', { name: 'Decline (test)' }).click()
  await expect(page.getByText(FAILED)).toBeVisible()
  await expect(page.getByText('Not paid', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Pay now' })).toBeEnabled()
  expect(await countMailAfterQuietPeriod(page, email, receiptSubject(order.reference))).toBe(0)

  await page.getByRole('button', { name: 'Pay now' }).click()
  await page.getByRole('link', { name: 'Pay (test)' }).click()
  await expect(page.getByText(CONFIRMED)).toBeVisible()
  await waitForMail(email, receiptSubject(order.reference))
  expect(await countMailAfterQuietPeriod(page, email, receiptSubject(order.reference))).toBe(1)
})

test('the payment notification can arrive before the patron returns from checkout', async ({ page }) => {
  const email = await registerPatron(page)
  const order = await placeOrder(page)

  await page.getByRole('button', { name: 'Pay now' }).click()
  await page.getByRole('link', { name: 'Pay, webhook first (test)' }).click()

  await expect(page.getByText(CONFIRMED)).toBeVisible()
  await waitForMail(email, receiptSubject(order.reference))
  expect(await countMailAfterQuietPeriod(page, email, receiptSubject(order.reference))).toBe(1)
})

test('a payment of the wrong amount does not confirm the order and is held for a refund', async ({ page }) => {
  const email = await registerPatron(page)
  const order = await placeOrder(page)

  await page.getByRole('button', { name: 'Pay now' }).click()
  // The checkout page appears only once the server has created the checkout at "Paystack".
  await expect(page.getByRole('heading', { name: 'Test checkout (Paystack simulator)' })).toBeVisible()
  const tx = await paystack.latestFor(email)
  await paystack.set(tx.reference, { paidAmount: tx.amount - 100 })
  await page.getByRole('link', { name: 'Pay (test)' }).click()

  await expect(page.getByText(NEEDS_REFUND)).toBeVisible()
  await expect(page.getByText('Not paid', { exact: true })).toBeVisible()
  expect(await countMailAfterQuietPeriod(page, email, receiptSubject(order.reference))).toBe(0)
})
