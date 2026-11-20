import type { OrderSummary, RefundLabel } from './types'

const REFUND_TEXT: Record<RefundLabel, string> = {
  NotPaid: 'Not paid',
  RefundInProgress: 'Refund in progress',
  Refunded: 'Refunded',
  RefundedLessFee: 'Refunded less fee',
  RefundFailed: 'Refund failed',
}

/** The payment wording of an order-history row (decision R3): Paid, Not paid, or what happened to a cancelled order's money. */
export function paymentText(order: Pick<OrderSummary, 'paymentStatus' | 'refundStatus'>): string {
  switch (order.paymentStatus) {
    case 'Paid':
      return 'Paid'
    case 'Unpaid':
      return 'Not paid'
    case 'Cancelled':
      return REFUND_TEXT[order.refundStatus ?? 'NotPaid']
  }
}

/** When the order was placed, in the business time zone (not the browser's), e.g. "20 Nov 2026, 09:00". */
export function formatPlacedAt(placedAtUtc: string, timeZone: string, locale?: string): string {
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(new Date(placedAtUtc))
  } catch {
    return placedAtUtc
  }
}
