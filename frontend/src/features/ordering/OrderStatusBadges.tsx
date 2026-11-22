import { Badge, type BadgeTone } from '../../components/ui/Badge'
import { STATUS_LABELS } from './placement'
import type { OrderStatus } from './types'

const STATUS_TONES: Record<OrderStatus, BadgeTone> = {
  PendingPayment: 'warning',
  Confirmed: 'success',
  PaymentFailed: 'danger',
  Cancelled: 'neutral',
}

/** Tone for a payment wording from paymentText() or the order page ("Paid", "Not paid", "Refund failed", …). */
function paymentTone(text: string, status: OrderStatus): BadgeTone {
  if (text === 'Paid') {
    return 'success'
  }
  if (text === 'Refund failed') {
    return 'danger'
  }
  if (text === 'Refund in progress') {
    return 'warning'
  }
  // "Not paid" is a call to action only while the order can still be paid.
  return text === 'Not paid' && status !== 'Cancelled' ? 'warning' : 'neutral'
}

/**
 * The order status and the payment status as two labelled badges. The words carry the meaning (colour only
 * reinforces it), and a hidden " · " keeps them apart for screen readers: "Confirmed · Paid".
 */
export function OrderStatusBadges({ status, payment }: { status: OrderStatus; payment: string }) {
  return (
    <span className="inline-flex flex-wrap items-center gap-1.5">
      <Badge tone={STATUS_TONES[status]}>{STATUS_LABELS[status]}</Badge>
      <span className="sr-only"> · </span>
      <Badge tone={paymentTone(payment, status)}>{payment}</Badge>
    </span>
  )
}
