import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { randomUUID } from 'node:crypto'
import { payOrder, paystack, useTestCheckout } from '../support/providers'
import { managerPage, placeOrder, registerPatron } from '../support/stack'

/**
 * M13 E2E-12 (MR-07): cross-patron isolation and role boundaries over real HTTP on the local E2E stack. Every call is
 * made from a real browser context (its session cookie), with a valid CSRF token on writes, so a refusal comes from
 * authorisation or ownership, not from a missing token.
 */

/** Headers for a state-changing call from this page's session: JSON plus its CSRF token. */
async function writeHeaders(page: Page): Promise<Record<string, string>> {
  const response = await page.request.get('/api/auth/csrf')
  expect(response.ok()).toBeTruthy()
  const { token, headerName } = (await response.json()) as { token: string; headerName: string }
  return { [headerName]: token, 'Content-Type': 'application/json' }
}

async function statusOf(request: APIRequestContext, method: 'GET' | 'POST' | 'PUT', url: string, headers?: Record<string, string>, data?: unknown) {
  const response = await request.fetch(url, { method, headers, data: data === undefined ? undefined : JSON.stringify(data) })
  return response.status()
}

test.beforeEach(async ({ page }) => {
  await useTestCheckout(page)
})

test("E2E-12 a patron can neither see nor act on another patron's order", async ({ page, browser }) => {
  // Patron A pays for an order.
  const emailA = await registerPatron(page)
  const order = await placeOrder(page)
  const txA = await payOrder(page, emailA)

  // Patron B, in a separate browser context, knows A's order reference.
  const contextB = await browser.newContext({ ignoreHTTPSErrors: true })
  const b = await contextB.newPage()
  await registerPatron(b)
  await b.goto(`/orders/${order.reference}`)
  await expect(b.getByText('We could not find this order in your account.')).toBeVisible()
  await expect(b.getByText(order.totalText)).toHaveCount(0)

  const headers = await writeHeaders(b)
  const base = `/api/ordering/orders/${order.reference}`
  const attempts: [string, 'GET' | 'POST', string, Record<string, string> | undefined, unknown][] = [
    ['read the order', 'GET', base, undefined, undefined],
    ['read the order (lower-case reference)', 'GET', `/api/ordering/orders/${order.reference.toLowerCase()}`, undefined, undefined],
    ['read its cancellation', 'GET', `${base}/cancellation`, undefined, undefined],
    ['start a payment', 'POST', `${base}/payments`, { ...headers, 'Idempotency-Key': randomUUID() }, {}],
    ['refresh a payment', 'POST', `${base}/payments/refresh`, headers, {}],
    ['cancel it', 'POST', `${base}/cancel`, headers, { expectedRefund: 0 }],
    ['accept a cancellation request', 'POST', `${base}/cancellation-request/accept`, headers, { expectedRefund: 0 }],
    ['decline a cancellation request', 'POST', `${base}/cancellation-request/decline`, headers, {}],
  ]
  for (const [what, method, url, h, data] of attempts) {
    expect(await statusOf(b.request, method, url, h, data), `patron B tries to ${what}`).toBe(404)
  }

  // B's own history does not contain A's order.
  const history = await b.request.get('/api/ordering/orders')
  expect(history.status()).toBe(200)
  expect(await history.text()).not.toContain(order.reference)
  await contextB.close()

  // Nothing happened to A's order: still paid, no new checkout, nothing refunded.
  await page.reload()
  await expect(page.getByText('Paid', { exact: true })).toBeVisible()
  await expect(page.getByText('Confirmed', { exact: true })).toBeVisible()
  expect((await paystack.latestFor(emailA)).reference).toBe(txA.reference)
  await expect(paystack.settleRefunds(txA.reference, 'processed')).rejects.toThrow(/404/)
})

test('E2E-12 role boundaries: patrons are refused manager functions, the manager is refused patron functions, and visitors must sign in', async ({
  page,
  browser,
}) => {
  await registerPatron(page)
  const order = await placeOrder(page)

  const managerOnly = [
    '/api/manager/ping',
    '/api/manager/reports/orders',
    '/api/manager/reports/trends',
    '/api/manager/reports/orders/export',
    '/api/manager/reports/audit/changes/export',
    '/api/manager/audit/changes',
    '/api/manager/audit/security',
    `/api/manager/orders/${order.reference}`,
    '/api/manager/refunds/attention',
    '/api/manager/catalog/flavours/1',
    '/api/manager/restaurants/1',
  ]
  const patronOnly = ['/api/ordering/options', '/api/ordering/orders', `/api/ordering/orders/${order.reference}`, '/api/ordering/cancellation-requests']

  // A patron: 403 on every manager read and write; the screens say Access denied.
  for (const url of managerOnly) {
    expect(await statusOf(page.request, 'GET', url), `patron GET ${url}`).toBe(403)
  }
  const patronHeaders = await writeHeaders(page)
  expect(await statusOf(page.request, 'POST', `/api/manager/orders/${order.reference}/cancellation-requests`, patronHeaders, { reason: 'Not allowed' })).toBe(403)
  expect(await statusOf(page.request, 'PUT', '/api/manager/settings/Refund.FeeRate', patronHeaders, { value: '0', version: 1 })).toBe(403)
  for (const path of ['/manager/reports', '/manager/orders', '/manager/settings']) {
    await page.goto(path)
    await expect(page.getByRole('heading', { name: 'Access denied' })).toBeVisible()
  }

  // The manager: 403 on patron functions; the ordering screens say Access denied.
  const manager = await managerPage(browser)
  for (const url of patronOnly) {
    expect(await statusOf(manager.request, 'GET', url), `manager GET ${url}`).toBe(403)
  }
  const managerHeaders = await writeHeaders(manager)
  expect(await statusOf(manager.request, 'POST', `/api/ordering/orders/${order.reference}/cancel`, managerHeaders, { expectedRefund: 0 })).toBe(403)
  await manager.goto('/order')
  await expect(manager.getByRole('heading', { name: 'Access denied' })).toBeVisible()
  await manager.context().close()

  // A visitor who is not signed in: 401 everywhere, and the screens send them to sign in.
  const visitorContext = await browser.newContext({ ignoreHTTPSErrors: true })
  const visitor = await visitorContext.newPage()
  for (const url of [...managerOnly, ...patronOnly]) {
    expect(await statusOf(visitor.request, 'GET', url), `visitor GET ${url}`).toBe(401)
  }
  for (const path of [`/orders/${order.reference}`, '/manager/reports']) {
    await visitor.goto(path)
    await expect(visitor).toHaveURL(/\/login$/)
  }
  await visitorContext.close()

  // The patron's order was not touched by any of this.
  await page.goto(`/orders/${order.reference}`)
  await expect(page.getByText('Not paid', { exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'The restaurant has asked to cancel this order' })).toHaveCount(0)
})
