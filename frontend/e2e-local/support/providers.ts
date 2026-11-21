import { expect, type Page } from '@playwright/test'
import { stack } from './stack'

// ---- Paystack simulator ------------------------------------------------------------------------------------------

export interface SimulatedTransaction {
  id: number
  reference: string
  email: string
  amount: number
  currency: string
  callbackUrl: string
  outcome: 'open' | 'paid' | 'failed' | 'abandoned'
  paidAmount?: number
}

/** A refund the API created at "Paystack" (amount in cents). */
export interface SimulatedRefund {
  id: number
  reference: string
  transaction: string
  amount: number
  currency: string
  status: string
}

async function simulator<T>(method: 'GET' | 'POST', path: string, body?: unknown): Promise<T> {
  const response = await fetch(`${stack.simulator}/__control${path}`, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!response.ok) {
    throw new Error(`Simulator ${method} ${path}: ${response.status} ${await response.text()}`)
  }
  return (await response.json()) as T
}

export const paystack = {
  /** The newest checkout the server created for this patron (what the API sent Paystack). */
  latestFor: (email: string) => simulator<SimulatedTransaction>('GET', `/transactions?email=${encodeURIComponent(email)}`),
  transaction: (reference: string) => simulator<SimulatedTransaction>('GET', `/transactions/${encodeURIComponent(reference)}`),
  /** What Paystack will say when the server verifies this transaction. */
  set: (reference: string, change: Partial<Pick<SimulatedTransaction, 'outcome' | 'paidAmount'>> & { delayMs?: number; unavailable?: boolean }) =>
    simulator<SimulatedTransaction>('POST', `/transactions/${encodeURIComponent(reference)}`, change),
  /** Sends a signed charge.success webhook to the API `times` times; returns the API's HTTP statuses. */
  webhook: (reference: string, times = 1) => simulator<{ statuses: number[] }>('POST', `/transactions/${encodeURIComponent(reference)}/webhook`, { times }),
  /** Settles the refunds of a transaction at "Paystack", optionally telling the API by a signed refund webhook. */
  settleRefunds: (transactionReference: string, status: 'processed' | 'failed', webhook = true) =>
    simulator<{ refunds: SimulatedRefund[]; statuses: number[] }>('POST', `/refunds/by-transaction/${encodeURIComponent(transactionReference)}`, { status, webhook }),
}

/**
 * Serves a test checkout page for checkout.paystack.com INSIDE THIS TEST BROWSER ONLY (decision D2). Its links set the
 * simulator's outcome and then follow the callback address the server gave Paystack, exactly as the real checkout
 * returns the patron. Nothing in the application changes.
 */
export async function useTestCheckout(page: Page): Promise<void> {
  await page.route('https://checkout.paystack.com/**', async (route) => {
    const url = new URL(route.request().url())
    const match = /^\/e2e-(msp-[0-9a-z]{12})(?:\/(pay|decline|cancel|pay-webhook-first))?$/.exec(url.pathname)
    if (!match) {
      await route.fulfill({ status: 404, contentType: 'text/plain', body: 'Unknown test checkout' })
      return
    }
    const reference = match[1].toUpperCase()
    const tx = await paystack.transaction(reference)
    const action = match[2]
    if (!action) {
      const amount = (tx.amount / 100).toFixed(2)
      await route.fulfill({
        status: 200,
        contentType: 'text/html; charset=utf-8',
        body: `<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Test checkout</title></head><body><main>
          <h1>Test checkout (Paystack simulator)</h1>
          <p>Amount: <span id="amount">${tx.currency} ${amount}</span> · Reference: <span id="reference">${tx.reference}</span></p>
          <ul>
            <li><a href="/e2e-${match[1]}/pay">Pay (test)</a></li>
            <li><a href="/e2e-${match[1]}/pay-webhook-first">Pay, webhook first (test)</a></li>
            <li><a href="/e2e-${match[1]}/decline">Decline (test)</a></li>
            <li><a href="/e2e-${match[1]}/cancel">Cancel (test)</a></li>
          </ul></main></body></html>`,
      })
      return
    }
    if (action === 'pay' || action === 'pay-webhook-first') {
      await paystack.set(reference, { outcome: 'paid' })
      if (action === 'pay-webhook-first') {
        const { statuses } = await paystack.webhook(reference)
        expect(statuses).toEqual([200])
      }
    } else {
      await paystack.set(reference, { outcome: action === 'decline' ? 'failed' : 'abandoned' })
    }
    // Return to the site as Paystack's checkout would. Playwright's WebKit cannot fulfil a route with a
    // redirect status, so every browser gets a page that navigates straight to the callback address.
    const callback = tx.callbackUrl.replaceAll('&', '&amp;').replaceAll('"', '&quot;')
    await route.fulfill({
      status: 200,
      contentType: 'text/html; charset=utf-8',
      body: `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta http-equiv="refresh" content="0;url=${callback}"><title>Returning to the shop</title></head><body><p>Returning to the shop…</p></body></html>`,
    })
  })
}

// ---- Mailpit ---------------------------------------------------------------------------------------------------

export interface MailSummary {
  ID: string
  Subject: string
  To: { Address: string }[]
}

export interface MailMessage {
  Subject: string
  HTML: string
  Text: string
}

/** Every email Mailpit captured for this address, newest first. */
export async function mailTo(address: string): Promise<MailSummary[]> {
  const response = await fetch(`${stack.mailpit}/api/v1/search?query=${encodeURIComponent(`to:"${address}"`)}&limit=50`)
  if (!response.ok) {
    throw new Error(`Mailpit search: ${response.status}`)
  }
  return ((await response.json()) as { messages: MailSummary[] }).messages ?? []
}

export async function mailMessage(id: string): Promise<MailMessage> {
  const response = await fetch(`${stack.mailpit}/api/v1/message/${encodeURIComponent(id)}`)
  if (!response.ok) {
    throw new Error(`Mailpit message: ${response.status}`)
  }
  return (await response.json()) as MailMessage
}

/** Waits until the address has an email with this subject (the dispatcher runs every 5 s on the E2E stack). */
export async function waitForMail(address: string, subject: string, timeoutMs = 60_000): Promise<MailSummary> {
  let found: MailSummary | undefined
  await expect
    .poll(async () => {
      found = (await mailTo(address)).find((m) => m.Subject === subject)
      return found !== undefined
    }, { timeout: timeoutMs, intervals: [1000] })
    .toBe(true)
  return found!
}

/**
 * Counts emails with this subject after a quiet period of three dispatcher cycles (15 s). The period is tied to
 * Email:Dispatch:IntervalSeconds=5 on the E2E stack: a duplicate queued by then would have been sent.
 */
export async function countMailAfterQuietPeriod(page: Page, address: string, subject: string): Promise<number> {
  await page.waitForTimeout(15_000)
  return (await mailTo(address)).filter((m) => m.Subject === subject).length
}

// ---- Shared journey step --------------------------------------------------------------------------------------------

/**
 * Pays the order open on this page through the test checkout and waits until the page shows it as Paid (the order has
 * been reloaded after the server verified the payment). Returns what the server sent "Paystack".
 */
export async function payOrder(page: Page, email: string): Promise<SimulatedTransaction> {
  await page.getByRole('button', { name: 'Pay now' }).click()
  await expect(page.getByRole('heading', { name: 'Test checkout (Paystack simulator)' })).toBeVisible()
  const tx = await paystack.latestFor(email)
  await page.getByRole('link', { name: 'Pay (test)' }).click()
  await expect(page.getByText('Payment received. Your order is confirmed.')).toBeVisible()
  await expect(page.getByText('Paid', { exact: true })).toBeVisible()
  return tx
}
