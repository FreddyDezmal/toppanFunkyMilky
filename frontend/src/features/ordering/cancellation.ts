import { ApiError } from '../../lib/api/httpClient'
import { describeOrderingError } from './errors'
import type { OrderCancellation } from './types'

export const CANCELLATION_MESSAGES = {
  cancelled: 'Your order has been cancelled.',
  declined: 'You declined the request. Your order stays as it is.',
  refundChanged: 'The refund for this order has changed. Please check the new amount and confirm again. Nothing was cancelled.',
  uncertain:
    'We could not confirm whether your order was cancelled. Check your connection and try again: trying again will not cancel or refund twice.',
  pickupPassed: 'This order can no longer be cancelled because its pickup time has passed.',
  unavailable: 'Cancelling paid orders is temporarily unavailable. Please try again later.',
  loadFailed: 'Cancellation details are unavailable right now.',
  contactRestaurant: 'Please contact the restaurant about this refund.',
} as const

/** The new figures a 409 "refund changed" answer carries in its `cancellation` extension, if any. */
export function changedCancellation(error: unknown): OrderCancellation | null {
  if (!(error instanceof ApiError) || error.status !== 409) {
    return null
  }
  const value = error.problem?.cancellation
  return typeof value === 'object' && value !== null && typeof (value as OrderCancellation).refundAmount === 'number'
    ? (value as OrderCancellation)
    : null
}

/** A message for a failed cancel, accept or decline: the server's title and detail when it gave them. */
export function cancellationFailureMessage(error: unknown): string {
  if (error instanceof ApiError && (error.status === undefined || (error.status >= 500 && error.status !== 503))) {
    return CANCELLATION_MESSAGES.uncertain
  }
  if (error instanceof ApiError && (error.status === 409 || error.status === 422 || error.status === 503)) {
    const { title, detail } = error.problem ?? {}
    if (title) {
      return detail ? `${title} ${detail}` : title
    }
  }
  return describeOrderingError(error)
}
