import { ApiError } from '../../lib/api/httpClient'
import { describeOrderingError } from './errors'
import type { OrderStatus, OrderTotals } from './types'

/** How long to wait for the server before telling the patron the outcome is unknown. */
export const PLACE_ORDER_TIMEOUT_MS = 20_000

export const PLACEMENT_MESSAGES = {
  uncertain:
    'We could not confirm whether your order was placed. Check your connection and try again: trying again will not create a second order.',
  busy: 'Too many requests right now. Please wait a minute, then try again.',
  keyReused: 'Your order changed while it was being placed. Please check it and place it again.',
} as const

export function placementFailureMessage(error: unknown, timedOut: boolean): string {
  if (timedOut || (error instanceof ApiError && (error.status === undefined || (error.status >= 500 && error.status !== 503)))) {
    return PLACEMENT_MESSAGES.uncertain
  }
  if (error instanceof ApiError && error.status === 429) {
    return PLACEMENT_MESSAGES.busy
  }
  if (error instanceof ApiError && error.status === 422) {
    return PLACEMENT_MESSAGES.keyReused
  }
  return describeOrderingError(error)
}

export function isOrderTotals(value: unknown): value is OrderTotals {
  return typeof value === 'object' && value !== null && typeof (value as OrderTotals).total === 'number' &&
    typeof (value as OrderTotals).currencyCode === 'string'
}


/** Order status in words. Payment status is shown separately (isPaid), so "awaiting payment" never reads as paid. */
export const STATUS_LABELS: Record<OrderStatus, string> = {
  PendingPayment: 'Awaiting payment',
  Confirmed: 'Confirmed',
  PaymentFailed: 'Payment failed',
  Cancelled: 'Cancelled',
}
