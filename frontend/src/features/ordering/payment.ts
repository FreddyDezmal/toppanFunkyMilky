import { ApiError } from '../../lib/api/httpClient'
import { describeOrderingError } from './errors'
import type { PaymentStatus } from './types'

/** The only host the browser is ever sent to for payment (Paystack's hosted checkout). */
export const CHECKOUT_HOST = 'checkout.paystack.com'

/** The query string Paystack's return address carries (`/orders/{reference}?payment=return`). It proves nothing. */
export const RETURN_PARAM = { name: 'payment', value: 'return' } as const

/** How long to wait for the server to open a checkout before saying the outcome is unknown. */
export const START_PAYMENT_TIMEOUT_MS = 20_000

/**
 * How often, and how many times, to ask the server about a payment after returning from checkout. About 30 seconds in
 * all, well inside the ordering rate limit; the server's background check keeps going after that.
 */
export const PAYMENT_POLLING = { intervalMs: 3_000, attempts: 10 }

export const PAYMENT_MESSAGES = {
  checking: 'Checking your payment…',
  paid: 'Payment received. Your order is confirmed.',
  failed: 'Your payment did not go through. You have not been charged for this order. You can try again.',
  cancelled: 'We have not received a payment for this order yet. If you cancelled, your order is saved and you can pay when you are ready.',
  stillChecking:
    'We have not had confirmation of a payment yet. If you paid, your order will be confirmed automatically: check again in a minute. If you cancelled, your order is saved and you can pay when you are ready.',
  needsRefund:
    'We received a payment that could not be applied to this order, so it needs to be refunded. Please contact the restaurant.',
  uncertain: 'We could not open the payment page. Check your connection and try again: trying again will not create a second payment.',
  untrusted: 'We could not open the payment page safely. Please try again later.',
  checkFailed: 'We could not check your payment right now. Your payment, if made, is safe. Please check again in a minute.',
} as const

/** True only for an https address on Paystack's checkout host: the browser is never sent anywhere else. */
export function isTrustedCheckoutUrl(value: string): boolean {
  try {
    const url = new URL(value)
    return url.protocol === 'https:' && url.hostname === CHECKOUT_HOST && url.port === '' && url.username === '' && url.password === ''
  } catch {
    return false
  }
}

/** Leaving the app for checkout. An object, so tests can replace the navigation. */
export const checkoutNavigation = {
  go(url: string): void {
    window.location.assign(url)
  },
}

/** Whether a failed start may have reached the server, so the same key must be reused on the next try. */
export function isUncertainStartFailure(error: unknown, timedOut: boolean): boolean {
  return timedOut || (error instanceof ApiError && (error.status === undefined || (error.status >= 500 && error.status !== 503)))
}

/** A message for a failed start: the server's title and detail when it gave them (409, 422, 503). */
export function startPaymentFailureMessage(error: unknown, timedOut: boolean): string {
  if (isUncertainStartFailure(error, timedOut)) {
    return PAYMENT_MESSAGES.uncertain
  }
  if (error instanceof ApiError && (error.status === 409 || error.status === 422 || error.status === 503)) {
    const { title, detail } = error.problem ?? {}
    if (title) {
      return detail ? `${title} ${detail}` : title
    }
  }
  return describeOrderingError(error)
}

/** What the patron sees once the server has answered and is no longer checking an open attempt. */
export type PaymentOutcome = 'paid' | 'failed' | 'cancelled' | 'needsRefund' | 'none'

export function paymentOutcome(status: PaymentStatus): PaymentOutcome {
  if (status.isPaid) {
    return 'paid'
  }
  switch (status.payment?.status) {
    case 'Failed':
      return 'failed'
    case 'Abandoned':
    case 'Superseded':
      return 'cancelled'
    case 'RequiresRefund':
    case 'Succeeded':
      // Succeeded without a paid order cannot normally happen; it still means money arrived that needs attention.
      return 'needsRefund'
    default:
      return 'none'
  }
}
