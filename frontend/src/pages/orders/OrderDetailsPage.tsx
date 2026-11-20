import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { fetchOrder, fetchOrderingOptions } from '../../features/ordering/api'
import { describeOrderingError } from '../../features/ordering/errors'
import { CancellationPanel } from '../../features/ordering/CancellationPanel'
import { formatLocalTime, timeZoneLabel } from '../../features/ordering/format'
import { RETURN_PARAM } from '../../features/ordering/payment'
import { PaymentPanel } from '../../features/ordering/PaymentPanel'
import { STATUS_LABELS } from '../../features/ordering/placement'
import { PriceSummary } from '../../features/ordering/PriceSummary'
import type { PlacedOrder } from '../../features/ordering/types'
import { ApiError } from '../../lib/api/httpClient'
import { formatMoney } from '../../lib/format/money'

type PageState =
  | { kind: 'loading' }
  | { kind: 'loaded'; order: PlacedOrder; locale: string | undefined }
  | { kind: 'notFound' }
  | { kind: 'error'; message: string }

const LINK_CLASS = 'rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50'

/**
 * /orders/:reference — one of the patron's own orders, exactly as stored when it was placed (names, fees, VAT and
 * total are snapshots, never today's catalogue). Survives a refresh: everything is read from the server.
 */
export function OrderDetailsPage() {
  const { reference = '' } = useParams()
  const [state, setState] = useState<PageState>({ kind: 'loading' })
  const [attempt, setAttempt] = useState(0)
  const heading = useRef<HTMLHeadingElement>(null)
  const [searchParams] = useSearchParams()
  const returning = searchParams.get(RETURN_PARAM.name) === RETURN_PARAM.value

  // After a payment change, show the order as now stored without leaving the page (a failure keeps what is shown).
  const reloadOrder = useCallback(() => {
    fetchOrder(reference).then(
      (order) => setState((current) => (current.kind === 'loaded' ? { ...current, order } : current)),
      () => undefined,
    )
  }, [reference])

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      fetchOrder(reference, controller.signal),
      // Only the display locale is needed; the order still shows if ordering is unavailable.
      fetchOrderingOptions(controller.signal).then(
        (options) => options.display.locale,
        () => undefined,
      ),
    ])
      .then(([order, locale]) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'loaded', order, locale })
        }
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        setState(error instanceof ApiError && error.status === 404 ? { kind: 'notFound' } : { kind: 'error', message: describeOrderingError(error) })
      })
    return () => controller.abort()
  }, [reference, attempt])

  // Move focus to the heading once the order is shown, so screen readers announce the new page.
  useEffect(() => {
    if (state.kind === 'loaded') {
      heading.current?.focus()
    }
  }, [state.kind])

  if (state.kind === 'loading') {
    return (
      <p role="status" className="text-sm text-slate-500">
        Loading your order…
      </p>
    )
  }

  if (state.kind === 'notFound') {
    return (
      <section aria-labelledby="order-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h1 id="order-heading" className="text-2xl font-bold tracking-tight">
          Order not found
        </h1>
        <p className="mt-2 text-slate-600">We could not find this order in your account.</p>
        <Link to="/order" className={`${LINK_CLASS} mt-4 inline-block`}>
          Start an order
        </Link>
      </section>
    )
  }

  if (state.kind === 'error') {
    return (
      <div role="alert" className="rounded-lg bg-rose-50 p-4 text-sm text-rose-800">
        <p>{state.message}</p>
        <button
          type="button"
          onClick={() => {
            setState({ kind: 'loading' })
            setAttempt((count) => count + 1)
          }}
          className={`${LINK_CLASS} mt-3 bg-white`}
        >
          Try again
        </button>
      </div>
    )
  }

  const { order, locale } = state
  const money = (amount: number) => formatMoney(amount, order.totals.currencyCode, locale)

  return (
    <section aria-labelledby="order-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="order-heading" ref={heading} tabIndex={-1} className="text-2xl font-bold tracking-tight focus:outline-none">
        Order {order.reference}
      </h1>

      <dl className="mt-4 grid max-w-md grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-slate-600">Order status</dt>
        <dd className="font-medium">{STATUS_LABELS[order.status]}</dd>
        <dt className="text-slate-600">Payment</dt>
        <dd className="font-medium">{order.isPaid ? 'Paid' : 'Not paid'}</dd>
      </dl>

      {order.note && <p className="mt-4 rounded-lg bg-amber-50 p-3 text-sm font-medium text-amber-900">{order.note}</p>}

      <div className="mt-6 grid gap-6 sm:grid-cols-2">
        <div>
          <h2 className="text-lg font-semibold">Pickup</h2>
          <p className="mt-1 font-medium">{order.restaurant.name}</p>
          {order.restaurant.address && <p className="text-sm text-slate-600">{order.restaurant.address}</p>}
          <p className="mt-2">{formatLocalTime(order.pickup.local, locale)}</p>
          <p className="text-xs text-slate-500">{timeZoneLabel(order.pickup.timeZone, locale)}</p>
        </div>

        <div>
          <h2 className="text-lg font-semibold">{order.drinks.length === 1 ? '1 milkshake' : `${order.drinks.length} milkshakes`}</h2>
          <ol className="mt-1 space-y-3">
            {order.drinks.map((drink) => (
              <li key={drink.number}>
                <h3 className="flex justify-between gap-4 font-medium">
                  <span>Milkshake {drink.number}</span> <span>{money(drink.price)}</span>
                </h3>
                <ul className="text-sm text-slate-700">
                  <li>
                    {drink.flavour.name} · {money(drink.flavour.fee)}
                  </li>
                  <li>
                    {drink.consistency.name} · {money(drink.consistency.fee)}
                  </li>
                  <li>
                    {drink.topping.name} · {money(drink.topping.fee)}
                  </li>
                </ul>
              </li>
            ))}
          </ol>
        </div>
      </div>

      <section aria-labelledby="order-price-heading" className="mt-6 max-w-sm rounded-xl bg-slate-50 p-4">
        <h2 id="order-price-heading" className="mb-2 text-lg font-semibold">
          Price
        </h2>
        <PriceSummary totals={order.totals} locale={locale} totalLabel="Total" />
      </section>

      <PaymentPanel
        key={order.reference}
        reference={order.reference}
        awaitsPayment={order.awaitsPayment}
        amountLabel={money(order.totals.total)}
        returning={returning}
        onOrderChanged={reloadOrder}
      />

      <CancellationPanel reference={order.reference} orderStatus={order.status} locale={locale} onOrderChanged={reloadOrder} />

      <div className="mt-8 flex flex-wrap items-center gap-3 border-t border-slate-100 pt-6">
        <Link to="/order" className={LINK_CLASS}>
          Start a new order
        </Link>
      </div>
    </section>
  )
}
