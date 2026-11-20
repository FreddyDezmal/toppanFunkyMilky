import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fetchOrderingOptions, fetchOrders } from '../../features/ordering/api'
import { describeOrderingError } from '../../features/ordering/errors'
import { formatLocalTime } from '../../features/ordering/format'
import { STATUS_LABELS } from '../../features/ordering/placement'
import type { OrderSummary } from '../../features/ordering/types'
import { formatMoney } from '../../lib/format/money'

type PageState =
  | { kind: 'loading' }
  | { kind: 'loaded'; orders: OrderSummary[]; locale: string | undefined }
  | { kind: 'error'; message: string }

const LINK_CLASS = 'rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700'

/**
 * /orders — the patron's most recent orders (part of M11, brought forward for M8b): each links to its order page, where
 * it can be paid, cancelled or a restaurant request answered. Order status and payment are shown separately.
 */
export function MyOrdersPage() {
  const [state, setState] = useState<PageState>({ kind: 'loading' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      fetchOrders(controller.signal),
      // Only the display locale is needed; the list still shows if ordering is unavailable.
      fetchOrderingOptions(controller.signal).then(
        (options) => options.display.locale,
        () => undefined,
      ),
    ])
      .then(([orders, locale]) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'loaded', orders, locale })
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'error', message: describeOrderingError(error) })
        }
      })
    return () => controller.abort()
  }, [attempt])

  return (
    <section aria-labelledby="my-orders-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="my-orders-heading" className="text-2xl font-bold tracking-tight">
        My orders
      </h1>

      {state.kind === 'loading' && (
        <p role="status" className="mt-4 text-sm text-slate-500">
          Loading your orders…
        </p>
      )}

      {state.kind === 'error' && (
        <div role="alert" className="mt-4 rounded-lg bg-rose-50 p-4 text-sm text-rose-800">
          <p>{state.message}</p>
          <button
            type="button"
            onClick={() => {
              setState({ kind: 'loading' })
              setAttempt((count) => count + 1)
            }}
            className="mt-3 rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
          >
            Try again
          </button>
        </div>
      )}

      {state.kind === 'loaded' && state.orders.length === 0 && (
        <div className="mt-4">
          <p className="text-slate-600">You have not placed any orders yet.</p>
          <Link to="/order" className={`${LINK_CLASS} mt-4 inline-block`}>
            Start an order
          </Link>
        </div>
      )}

      {state.kind === 'loaded' && state.orders.length > 0 && (
        <>
          <p className="mt-1 text-sm text-slate-600">Your {state.orders.length === 1 ? 'order' : `${state.orders.length} most recent orders`}, newest first.</p>
          <ul className="mt-4 divide-y divide-slate-100">
            {state.orders.map((order) => (
              <li key={order.reference} className="flex flex-wrap items-start justify-between gap-3 py-3">
                <div>
                  <Link to={`/orders/${order.reference}`} className="font-semibold text-brand-700 underline">
                    Order {order.reference}
                  </Link>
                  <p className="text-sm text-slate-600">
                    {order.drinkCount === 1 ? '1 milkshake' : `${order.drinkCount} milkshakes`} · {order.restaurantName} ·{' '}
                    {formatLocalTime(order.pickup.local, state.locale)}
                  </p>
                </div>
                <div className="text-right text-sm">
                  <p className="font-medium">{formatMoney(order.total, order.currencyCode, state.locale)}</p>
                  <p>
                    {STATUS_LABELS[order.status]} · {order.isPaid ? 'Paid' : 'Not paid'}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}
