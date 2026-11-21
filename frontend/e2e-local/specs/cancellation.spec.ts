import { expect, test, type Browser, type Page } from '@playwright/test'
import { countMailAfterQuietPeriod, mailMessage, payOrder, paystack, useTestCheckout, waitForMail, type SimulatedTransaction } from '../support/providers'
import { centsOf, expectNoSeriousAccessibilityProblems, managerPage, placeOrder, registerPatron, type PlacedOrder } from '../support/stack'

/**
 * M13 E2E-06 and E2E-07 (MR-05, MR-06, MR-09, MR-10) on the local E2E stack. A paid order is cancelled by the patron
 * (fee kept) or, at the restaurant's request, with the patron's agreement (full refund). Refunds go to the Paystack
 * simulator and count as complete only when "Paystack" confirms them (signed refund webhook, re-verified by the API).
 */
const CANCELLED = 'Your order has been cancelled.'
const DECLINED = 'You declined the request. Your order stays as it is.'
const MONEY = String.raw`R\s?[\d\s.,]*\d`

const subjects = {
  cancelled: (reference: string) => `Order ${reference} has been cancelled`,
  refunded: (reference: string) => `Refund for order ${reference} completed`,
  refundFailed: (reference: string) => `Refund for order ${reference}: please contact us`,
  requested: (reference: string) => `Order ${reference}: the restaurant has asked to cancel`,
}

test.beforeEach(async ({ page }) => {
  await useTestCheckout(page)
})

/** Registers a patron, places one order and pays it through the test checkout. */
async function paidOrder(page: Page): Promise<{ email: string; order: PlacedOrder; tx: SimulatedTransaction }> {
  const email = await registerPatron(page)
  const order = await placeOrder(page)
  const tx = await payOrder(page, email)
  return { email, order, tx }
}

/** The patron's My orders row for this order. */
async function historyRow(page: Page, reference: string) {
  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'My orders' }).click()
  return page.getByRole('link', { name: `Order ${reference}` }).locator('xpath=ancestor::li[1]')
}

/** The manager's Orders page opened on this order, in its own signed-in context. */
async function managerOrder(browser: Browser, reference: string): Promise<Page> {
  const manager = await managerPage(browser)
  await manager.goto(`/manager/orders?reference=${reference}`)
  await expect(manager.getByText('Order status', { exact: true })).toBeVisible()
  return manager
}

async function askToCancel(browser: Browser, reference: string, reason: string): Promise<void> {
  const manager = await managerOrder(browser, reference)
  await manager.getByLabel('Reason').fill(reason)
  await manager.getByRole('button', { name: 'Ask the patron to cancel' }).click()
  await expect(manager.getByText(/^Request sent\./)).toBeVisible()
  await expect(manager.getByText(`– Waiting for the patron`, { exact: false })).toBeVisible()
  await manager.context().close()
}

test('E2E-06 a patron cancels a paid order: the fee is kept, the rest is refunded once Paystack confirms, with emails and history', async ({
  page,
  browser,
}) => {
  const { email, order, tx } = await paidOrder(page)

  // The dialog shows the server's figures: paid = fee + refund, and a fee is kept.
  await page.getByRole('button', { name: 'Cancel order' }).click()
  const dialog = page.getByRole('alertdialog')
  const message = (await dialog.textContent()) ?? ''
  const figures = new RegExp(`You paid (${MONEY})\\. A cancellation fee of (${MONEY}) .*you will get back (${MONEY})\\.`).exec(message)
  expect(figures, message).not.toBeNull()
  const [paid, fee, refund] = figures!.slice(1).map(centsOf)
  expect(paid).toBe(tx.amount)
  expect(fee).toBeGreaterThan(0)
  expect(fee + refund).toBe(paid)
  await dialog.getByRole('button', { name: 'Cancel order' }).click()

  await expect(page.getByText(CANCELLED)).toBeVisible()
  await expect(page.getByText(/– on its way\./)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Cancel order' })).toHaveCount(0)

  // "Paystack" received exactly one refund, for the refund amount; it confirms it by a signed webhook.
  const settled = await paystack.settleRefunds(tx.reference, 'processed')
  expect(settled.refunds.map((r) => r.amount)).toEqual([refund])
  expect(settled.statuses).toEqual([200])

  await page.reload()
  await expect(page.getByText(/– completed( on .+)?\. A cancellation fee of .+ was kept\./)).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Cancelled order')

  // One cancellation email and one refund email.
  const cancelledMail = await waitForMail(email, subjects.cancelled(order.reference))
  await waitForMail(email, subjects.refunded(order.reference))
  expect(await countMailAfterQuietPeriod(page, email, subjects.cancelled(order.reference))).toBe(1)
  expect(await countMailAfterQuietPeriod(page, email, subjects.refunded(order.reference))).toBe(1)
  expect((await mailMessage(cancelledMail.ID)).Text).toContain(order.reference)

  // History and the manager's view of the order.
  await expect(await historyRow(page, order.reference)).toContainText('Cancelled · Refunded less fee')
  const manager = await managerOrder(browser, order.reference)
  await expect(manager.getByText(/Patron cancelled \(fee kept\) – Completed/)).toBeVisible()
  await manager.goto(`/manager/reports?q=${order.reference}`)
  const reportRow = manager.getByRole('row').filter({ has: manager.getByRole('link', { name: order.reference }) })
  await expect(reportRow).toContainText('Cancelled')
  await manager.context().close()
})

test('E2E-07 the restaurant asks to cancel a paid order and the patron accepts: full refund, no fee', async ({ page, browser }) => {
  const { email, order, tx } = await paidOrder(page)
  await askToCancel(browser, order.reference, 'Out of strawberry syrup')

  // The patron is told by email and on Home, and answers on the order page.
  await waitForMail(email, subjects.requested(order.reference))
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'The restaurant has asked to cancel an order' })).toBeVisible()
  await page.goto(`/orders/${order.reference}`)
  await expect(page.getByRole('heading', { name: 'The restaurant has asked to cancel this order' })).toBeVisible()
  await expect(page.getByText('Out of strawberry syrup')).toBeVisible()
  await expectNoSeriousAccessibilityProblems(page, 'Order with a cancellation request')

  await page.getByRole('button', { name: 'Accept cancellation' }).click()
  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toContainText('you will get back everything you paid')
  await dialog.getByRole('button', { name: 'Accept cancellation' }).click()
  await expect(page.getByText(CANCELLED)).toBeVisible()

  // Everything is refunded: no fee.
  const settled = await paystack.settleRefunds(tx.reference, 'processed')
  expect(settled.refunds.map((r) => r.amount)).toEqual([tx.amount])
  expect(settled.statuses).toEqual([200])
  await page.reload()
  await expect(page.getByText(/– completed( on .+)?\.$/)).toBeVisible()
  await expect(page.getByText(/cancellation fee .* was kept/)).toHaveCount(0)

  await waitForMail(email, subjects.cancelled(order.reference))
  await waitForMail(email, subjects.refunded(order.reference))
  await expect(await historyRow(page, order.reference)).toContainText('Cancelled · Refunded')
  await expect(await historyRow(page, order.reference)).not.toContainText('less fee')

  const manager = await managerOrder(browser, order.reference)
  await expect(manager.getByText(/Accepted – order cancelled/)).toBeVisible()
  await expect(manager.getByText(/Restaurant request \(full refund\) – Completed/)).toBeVisible()
  await manager.context().close()
})

test('E2E-07 the patron declines the restaurant\'s request: the order stays paid and nothing is refunded', async ({ page, browser }) => {
  const { email, order, tx } = await paidOrder(page)
  await askToCancel(browser, order.reference, 'Machine being serviced')

  await page.goto(`/orders/${order.reference}`)
  await page.getByRole('button', { name: 'Keep my order' }).click()
  await expect(page.getByText(DECLINED)).toBeVisible()
  await expect(page.getByText('Paid', { exact: true })).toBeVisible()
  await expect(page.getByText('Confirmed', { exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'The restaurant has asked to cancel this order' })).toHaveCount(0)

  const manager = await managerOrder(browser, order.reference)
  await expect(manager.getByText(/Declined by the patron/)).toBeVisible()
  await manager.context().close()

  // Nothing went to "Paystack" and no cancellation email was sent.
  await expect(paystack.settleRefunds(tx.reference, 'processed')).rejects.toThrow(/404/)
  expect(await countMailAfterQuietPeriod(page, email, subjects.cancelled(order.reference))).toBe(0)
  await expect(await historyRow(page, order.reference)).toContainText('Confirmed · Paid')
})

test('E2E-07 a refund that Paystack fails: the patron is emailed to contact the restaurant and the manager sees it', async ({ page, browser }) => {
  const { email, order, tx } = await paidOrder(page)

  await page.getByRole('button', { name: 'Cancel order' }).click()
  await page.getByRole('alertdialog').getByRole('button', { name: 'Cancel order' }).click()
  await expect(page.getByText(CANCELLED)).toBeVisible()

  expect((await paystack.settleRefunds(tx.reference, 'failed')).statuses).toEqual([200])
  await waitForMail(email, subjects.refundFailed(order.reference))
  expect(await countMailAfterQuietPeriod(page, email, subjects.refunded(order.reference))).toBe(0)

  await page.reload()
  await expect(page.getByText(/could not be completed automatically\. Please contact the restaurant about this refund\./)).toBeVisible()
  await expect(await historyRow(page, order.reference)).toContainText('Cancelled · Refund failed')

  const manager = await managerOrder(browser, order.reference)
  await expect(manager.getByText(/Patron cancelled \(fee kept\) – Failed – refund by hand at Paystack/)).toBeVisible()
  await expect(manager.getByRole('region', { name: 'Refunds needing attention' }).getByText('No refunds need attention.')).toHaveCount(0)
  await manager.context().close()
})
