import { useEffect, useRef, useState } from 'react'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { formatMoney, formatPercent } from '../../lib/format/money'
import { acceptCancellationRequest, cancelOrder, declineCancellationRequest, fetchCancellation } from './api'
import { CANCELLATION_MESSAGES, cancellationFailureMessage, changedCancellation } from './cancellation'
import type { OrderCancellation, OrderStatus, RefundInfo } from './types'

type Dialog = 'cancel' | 'accept' | null
type Notice = { tone: 'success' | 'warning' | 'error'; text: string } | null

interface CancellationPanelProps {
  reference: string
  /** The stored order's status: the panel reloads when it changes (e.g. a payment was just confirmed). */
  orderStatus: OrderStatus
  locale: string | undefined
  /** Reload the stored order after a cancellation. Must be stable. */
  onOrderChanged: () => void
}

const SECONDARY_CLASS = 'rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-50'
const DANGER_CLASS = 'rounded-lg border border-rose-300 bg-white px-4 py-2 text-sm font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-50'

/**
 * Cancelling an order and answering a manager's request (M8b). Every amount shown comes from the server: the browser
 * only sends back the refund the patron saw, and the server refuses it if it has changed.
 */
export function CancellationPanel({ reference, orderStatus, locale, onOrderChanged }: CancellationPanelProps) {
  // Details are shown only for the order status they were loaded for (M13 DEF-03): while the panel reloads after a
  // payment is confirmed, the unpaid figures ("nothing is charged") must not be shown or confirmed. The answer to the
  // patron's own action is the server's latest state and stays shown until the order catches up.
  const [loaded, setLoaded] = useState<{ data: OrderCancellation; forStatus: OrderStatus | null } | null>(null)
  const data = loaded && (loaded.forStatus === null || loaded.forStatus === orderStatus) ? loaded.data : null
  const [loadFailed, setLoadFailed] = useState(false)
  const [dialog, setDialog] = useState<Dialog>(null)
  const [busy, setBusy] = useState(false)
  const busyRef = useRef(false)
  const [notice, setNotice] = useState<Notice>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchCancellation(reference, controller.signal).then(
      (cancellation) => {
        if (!controller.signal.aborted) {
          setLoaded({ data: cancellation, forStatus: orderStatus })
        }
      },
      () => {
        if (!controller.signal.aborted) {
          setLoadFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [reference, orderStatus])

  if (!data) {
    return loadFailed ? <p className="mt-6 text-sm text-slate-600">{CANCELLATION_MESSAGES.loadFailed}</p> : null
  }

  const money = (amount: number) => formatMoney(amount, data.currencyCode, locale)

  async function run(action: () => Promise<OrderCancellation>, success: string, changesOrder: boolean) {
    if (busyRef.current) {
      return
    }
    busyRef.current = true
    setBusy(true)
    try {
      const result = await action()
      setLoaded({ data: result, forStatus: null })
      setNotice({ tone: 'success', text: success })
      if (changesOrder) {
        onOrderChanged()
      }
    } catch (error: unknown) {
      const changed = changedCancellation(error)
      if (changed) {
        setLoaded({ data: changed, forStatus: null })
        setNotice({ tone: 'warning', text: CANCELLATION_MESSAGES.refundChanged })
        onOrderChanged()
      } else {
        setNotice({ tone: 'error', text: cancellationFailureMessage(error) })
      }
    } finally {
      busyRef.current = false
      setBusy(false)
      setDialog(null)
    }
  }

  const request = data.openRequest
  const cancelMessage = data.isPaid
    ? `You paid ${money(data.paidAmount)}. A cancellation fee of ${money(data.fee)} (${formatPercent(data.feeRate, locale)} of the price before VAT) is kept, so you will get back ${money(data.refundAmount)}. Refunds can take several business days to reach your card. This cannot be undone.`
    : 'You have not paid for this order, so nothing is charged. This cannot be undone.'
  const acceptMessage = request && data.isPaid
    ? `Your order will be cancelled and you will get back everything you paid: ${money(request.refundAmount)}. Refunds can take several business days to reach your card.`
    : 'Your order will be cancelled. You have not paid for it, so nothing is charged.'

  return (
    <>
      {notice && (
        <div
          role={notice.tone === 'error' ? 'alert' : 'status'}
          className={`mt-6 rounded-lg p-3 text-sm ${
            notice.tone === 'success' ? 'bg-emerald-50 text-emerald-900' : notice.tone === 'warning' ? 'bg-amber-50 text-amber-900' : 'bg-rose-50 text-rose-800'
          }`}
        >
          {notice.text}
        </div>
      )}

      {request && (
        <section aria-labelledby="request-heading" className="mt-6 max-w-xl rounded-xl border border-amber-300 bg-amber-50 p-4">
          <h2 id="request-heading" className="text-lg font-semibold text-amber-900">
            The restaurant has asked to cancel this order
          </h2>
          <p className="mt-1 text-sm text-amber-900">
            Reason: <q>{request.reason}</q>
          </p>
          <p className="mt-2 text-sm text-amber-900">
            {data.isPaid
              ? `If you accept, your order is cancelled and you get back everything you paid (${money(request.refundAmount)}), with no fee.`
              : 'If you accept, your order is cancelled. You have not paid, so nothing is charged.'}{' '}
            If you decline, your order stays as it is.
          </p>
          <div className="mt-3 flex flex-wrap gap-2">
            <button type="button" onClick={() => setDialog('accept')} disabled={busy} className={DANGER_CLASS}>
              Accept cancellation
            </button>
            <button
              type="button"
              disabled={busy}
              onClick={() => void run(() => declineCancellationRequest(reference), CANCELLATION_MESSAGES.declined, false)}
              className={SECONDARY_CLASS}
            >
              Keep my order
            </button>
          </div>
        </section>
      )}

      {data.refunds.length > 0 && (
        <section aria-labelledby="refunds-heading" className="mt-6 max-w-xl">
          <h2 id="refunds-heading" className="text-lg font-semibold">
            {data.refunds.length === 1 ? 'Refund' : 'Refunds'}
          </h2>
          <ul className="mt-1 space-y-2">
            {data.refunds.map((refund) => (
              <li key={refund.reference} className="rounded-lg bg-slate-50 p-3 text-sm">
                <RefundLine refund={refund} money={money} locale={locale} />
              </li>
            ))}
          </ul>
        </section>
      )}

      {data.canCancel && !request && (
        <div className="mt-6">
          <button type="button" onClick={() => setDialog('cancel')} disabled={busy} className={DANGER_CLASS}>
            Cancel order
          </button>
          {data.isPaid && (
            <p className="mt-2 text-xs text-slate-600">
              Cancelling keeps a fee of {formatPercent(data.feeRate, locale)} of the price before VAT; you get back {money(data.refundAmount)}.
            </p>
          )}
        </div>
      )}
      {data.cannotCancelReason === 'PickupPassed' && <p className="mt-6 text-sm text-slate-600">{CANCELLATION_MESSAGES.pickupPassed}</p>}
      {!data.canCancel && data.cannotCancelReason === null && data.isPaid && (
        <p className="mt-6 text-sm text-slate-600">{CANCELLATION_MESSAGES.unavailable}</p>
      )}

      <ConfirmDialog
        open={dialog === 'cancel'}
        title="Cancel this order?"
        message={cancelMessage}
        confirmLabel="Cancel order"
        cancelLabel="Keep order"
        busy={busy}
        onConfirm={() => void run(() => cancelOrder(reference, data.refundAmount), CANCELLATION_MESSAGES.cancelled, true)}
        onCancel={() => setDialog(null)}
      />
      <ConfirmDialog
        open={dialog === 'accept'}
        title="Accept the cancellation?"
        message={acceptMessage}
        confirmLabel="Accept cancellation"
        cancelLabel="Go back"
        busy={busy}
        onConfirm={() =>
          void run(() => acceptCancellationRequest(reference, request?.refundAmount ?? 0), CANCELLATION_MESSAGES.cancelled, true)
        }
        onCancel={() => setDialog(null)}
      />
    </>
  )
}

function RefundLine({ refund, money, locale }: { refund: RefundInfo; money: (amount: number) => string; locale: string | undefined }) {
  const what = refund.kind === 'UnacceptedPayment' ? `Refund of a payment we could not accept: ${money(refund.amount)}` : `Refund of ${money(refund.amount)}`
  const fee = refund.fee > 0 ? ` A cancellation fee of ${money(refund.fee)} was kept.` : ''
  if (refund.status === 'Processed') {
    const date = refund.completedAt ? new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(refund.completedAt)) : ''
    return <p>{`${what} – completed${date ? ` on ${date}` : ''}.${fee}`}</p>
  }
  if (refund.status === 'Failed' || refund.needsAttention) {
    return (
      <p className="text-rose-800">
        {`${what} – could not be completed automatically. ${CANCELLATION_MESSAGES.contactRestaurant}`}
      </p>
    )
  }
  return <p>{`${what} – on its way. Refunds can take several business days to reach your card.${fee}`}</p>
}
