import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { getManagerOrder, listRefundsNeedingAttention, requestCancellation } from '../../features/management/api'
import { useDisplayFormat } from '../../features/management/displayFormat'
import { REASON_MAX_LENGTH, type ManagerOrder, type ManagerRefund, type RefundNeedingAttention } from '../../features/management/types'
import { describeError } from '../../lib/api/describeError'
import { ApiError } from '../../lib/api/httpClient'
import { formatMoney } from '../../lib/format/money'

const ORDER_STATUS: Record<ManagerOrder['status'], string> = {
  PendingPayment: 'Awaiting payment',
  Confirmed: 'Confirmed',
  PaymentFailed: 'Payment failed',
  Cancelled: 'Cancelled',
}

const REQUEST_STATUS: Record<ManagerOrder['requests'][number]['status'], string> = {
  Pending: 'Waiting for the patron',
  Accepted: 'Accepted – order cancelled',
  Declined: 'Declined by the patron',
  Lapsed: 'Lapsed (not answered before pickup, or the patron cancelled)',
}

const REFUND_KIND: Record<ManagerRefund['kind'], string> = {
  PatronCancellation: 'Patron cancelled (fee kept)',
  RestaurantCancellation: 'Restaurant request (full refund)',
  UnacceptedPayment: 'Payment the order could not accept (full refund)',
}

const REFUND_STATUS: Record<ManagerRefund['status'], string> = {
  Initiating: 'Being sent to Paystack',
  Pending: 'On its way',
  Processed: 'Completed',
  Failed: 'Failed – refund by hand at Paystack',
}

const INPUT_CLASS = 'mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-brand-600 focus:outline-none'
const BUTTON_CLASS = 'rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50'

function formatDateTime(iso: string, locale?: string, timeZone?: string): string {
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(new Date(iso))
  } catch {
    return iso
  }
}

/**
 * /manager/orders — find an order by reference and ask its patron to accept cancelling it (B1.2, Q3); refunds that need
 * handling by hand at Paystack (Q4). Managers never cancel or refund directly. ?reference=… (linked from Reports) opens
 * that order straight away.
 */
export function OrdersPage() {
  const { locale } = useDisplayFormat()
  const [searchParams] = useSearchParams()
  const linkedReference = searchParams.get('reference')?.trim() ?? ''
  const [reference, setReference] = useState(linkedReference)
  const [order, setOrder] = useState<ManagerOrder | null>(null)
  const [findError, setFindError] = useState<string | null>(null)
  const [finding, setFinding] = useState(false)
  const [attention, setAttention] = useState<RefundNeedingAttention[] | null>(null)
  const [attentionError, setAttentionError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listRefundsNeedingAttention(controller.signal).then(
      (loaded) => {
        if (!controller.signal.aborted) {
          setAttention(loaded)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setAttentionError(describeError(error))
        }
      },
    )
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!linkedReference) {
      return
    }
    const controller = new AbortController()
    getManagerOrder(linkedReference, controller.signal).then(
      (loaded) => {
        if (!controller.signal.aborted) {
          setOrder(loaded)
          setFindError(null)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setOrder(null)
          setFindError(
            error instanceof ApiError && error.status === 404 ? `No order has the reference ${linkedReference.toUpperCase()}.` : describeError(error),
          )
        }
      },
    )
    return () => controller.abort()
  }, [linkedReference])

  async function find(value: string) {
    const wanted = value.trim()
    if (!wanted) {
      setFindError('Enter an order reference such as MS-7KQ2-X9TD.')
      return
    }
    setFinding(true)
    setFindError(null)
    try {
      setOrder(await getManagerOrder(wanted))
    } catch (error: unknown) {
      setOrder(null)
      setFindError(error instanceof ApiError && error.status === 404 ? `No order has the reference ${wanted.toUpperCase()}.` : describeError(error))
    } finally {
      setFinding(false)
    }
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    void find(reference)
  }

  return (
    <div className="space-y-6">
      <section aria-labelledby="find-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h1 id="find-heading" className="text-2xl font-bold tracking-tight">
          Orders
        </h1>
        <form onSubmit={onSubmit} noValidate className="mt-4 flex max-w-md flex-wrap items-end gap-2">
          <label className="grow text-sm font-medium text-slate-700">
            Order reference
            <input value={reference} onChange={(event) => setReference(event.target.value)} placeholder="MS-XXXX-XXXX" className={INPUT_CLASS} />
          </label>
          <button type="submit" disabled={finding} className={BUTTON_CLASS}>
            {finding ? 'Finding…' : 'Find order'}
          </button>
        </form>
        {findError && (
          <p role="alert" className="mt-3 text-sm text-rose-700">
            {findError}
          </p>
        )}
      </section>

      {order && <OrderCard key={order.reference} order={order} locale={locale} onChanged={setOrder} />}

      <section aria-labelledby="attention-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h2 id="attention-heading" className="text-lg font-semibold">
          Refunds needing attention
        </h2>
        <p className="mt-1 text-sm text-slate-600">
          Paystack could not complete these refunds, or needs the patron&rsquo;s bank details. Handle them in the Paystack dashboard and contact the patron.
        </p>
        {attentionError && (
          <p role="alert" className="mt-3 text-sm text-rose-700">
            {attentionError}
          </p>
        )}
        {attention && attention.length === 0 && <p className="mt-3 text-sm text-slate-600">No refunds need attention.</p>}
        {attention && attention.length > 0 && (
          <ul className="mt-3 divide-y divide-slate-100 text-sm">
            {attention.map((refund) => (
              <li key={refund.refundReference} className="flex flex-wrap items-center justify-between gap-2 py-2">
                <span>
                  {formatMoney(refund.amount, refund.currencyCode, locale)} · {REFUND_STATUS[refund.status]}
                  {refund.providerMessage ? ` · ${refund.providerMessage}` : ''} · {refund.refundReference}
                </span>
                <button
                  type="button"
                  onClick={() => {
                    setReference(refund.orderReference)
                    void find(refund.orderReference)
                  }}
                  className="font-medium text-brand-700 underline"
                >
                  Open order {refund.orderReference}
                </button>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

function OrderCard({ order, locale, onChanged }: { order: ManagerOrder; locale: string | undefined; onChanged: (order: ManagerOrder) => void }) {
  const money = (amount: number) => formatMoney(amount, order.currencyCode, locale)
  const heading = useRef<HTMLHeadingElement>(null)
  const [sent, setSent] = useState(false)

  useEffect(() => {
    heading.current?.focus()
  }, [])

  return (
    <section aria-labelledby="order-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h2 id="order-heading" ref={heading} tabIndex={-1} className="text-xl font-semibold focus:outline-none">
        Order {order.reference}
      </h2>
      <dl className="mt-3 grid max-w-xl grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-slate-600">Order status</dt>
        <dd className="font-medium">{ORDER_STATUS[order.status]}</dd>
        <dt className="text-slate-600">Payment</dt>
        <dd className="font-medium">{order.isPaid ? 'Paid' : 'Not paid'}</dd>
        <dt className="text-slate-600">Patron</dt>
        <dd>
          {order.patronFirstName} · <a href={`mailto:${order.patronEmail}`} className="text-brand-700 underline">{order.patronEmail}</a>
        </dd>
        <dt className="text-slate-600">Pickup</dt>
        <dd>
          {order.restaurantName}, {formatDateTime(order.pickupAtUtc, locale, order.pickupTimeZone)}
        </dd>
        <dt className="text-slate-600">Total</dt>
        <dd>{money(order.total)}</dd>
      </dl>

      {order.requests.length > 0 && (
        <>
          <h3 className="mt-5 font-semibold">Cancellation requests</h3>
          <ul className="mt-1 space-y-1 text-sm">
            {order.requests.map((request) => (
              <li key={request.id}>
                <q>{request.reason}</q> – {REQUEST_STATUS[request.status]} ({formatDateTime(request.createdAt, locale)})
              </li>
            ))}
          </ul>
        </>
      )}

      {order.refunds.length > 0 && (
        <>
          <h3 className="mt-5 font-semibold">Refunds</h3>
          <ul className="mt-1 space-y-1 text-sm">
            {order.refunds.map((refund) => (
              <li key={refund.reference}>
                {money(refund.amount)} – {REFUND_KIND[refund.kind]} – {refund.needsAttention ? 'Needs attention at Paystack' : REFUND_STATUS[refund.status]}
              </li>
            ))}
          </ul>
        </>
      )}

      {sent && (
        <p role="status" className="mt-5 text-sm text-emerald-800">
          Request sent. The order changes only if the patron accepts; unanswered requests lapse at the pickup time.
        </p>
      )}
      {order.canRequestCancellation ? (
        <RequestForm
          order={order}
          onRequested={(updated) => {
            setSent(true)
            onChanged(updated)
          }}
        />
      ) : (
        <p className="mt-5 text-sm text-slate-600">
          {order.requests.some((request) => request.isOpen)
            ? 'A cancellation request is waiting for the patron’s answer.'
            : 'This order can no longer be cancelled: it is cancelled or its pickup time has passed.'}
        </p>
      )}
    </section>
  )
}

function RequestForm({ order, onRequested }: { order: ManagerOrder; onRequested: (order: ManagerOrder) => void }) {
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const busyRef = useRef(false)
  const hintId = useId()
  const errorId = useId()

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const trimmed = reason.trim()
    if (!trimmed || trimmed.length > REASON_MAX_LENGTH) {
      setError(`Give a reason of 1 to ${REASON_MAX_LENGTH} characters.`)
      return
    }
    if (busyRef.current) {
      return
    }
    busyRef.current = true
    setBusy(true)
    setError(null)
    try {
      const updated = await requestCancellation(order.reference, trimmed)
      onRequested(updated)
    } catch (failure: unknown) {
      if (failure instanceof ApiError && failure.status === 400) {
        setError(failure.fieldErrors.reason?.[0] ?? describeError(failure))
      } else if (failure instanceof ApiError && (failure.status === 409 || failure.status === 422)) {
        setError(failure.problem?.title ?? describeError(failure))
      } else {
        setError(describeError(failure))
      }
    } finally {
      busyRef.current = false
      setBusy(false)
    }
  }

  return (
    <form onSubmit={(event) => void onSubmit(event)} noValidate className="mt-5 max-w-xl">
      <h3 className="font-semibold">Ask the patron to cancel</h3>
      <p id={hintId} className="text-xs text-slate-600">
        The patron sees this reason and decides. If the order was paid and they accept, they get everything back.
      </p>
      <label className="mt-2 block text-sm font-medium text-slate-700">
        Reason
        <input
          value={reason}
          maxLength={REASON_MAX_LENGTH}
          onChange={(event) => setReason(event.target.value)}
          aria-describedby={error ? `${hintId} ${errorId}` : hintId}
          aria-invalid={error ? true : undefined}
          className={INPUT_CLASS}
        />
      </label>
      <p className="mt-1 text-xs text-slate-600">
        {reason.length}/{REASON_MAX_LENGTH} characters
      </p>
      {error && (
        <p id={errorId} role="alert" className="mt-1 text-sm text-rose-700">
          {error}
        </p>
      )}
      <button type="submit" disabled={busy} className={`${BUTTON_CLASS} mt-2`}>
        {busy ? 'Sending…' : 'Ask the patron to cancel'}
      </button>
    </form>
  )
}
