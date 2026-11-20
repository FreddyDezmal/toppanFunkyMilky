import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fetchOrderingOptions, fetchOrders } from '../../features/ordering/api'
import { describeOrderingError } from '../../features/ordering/errors'
import { formatLocalTime } from '../../features/ordering/format'
import { formatPlacedAt, paymentText } from '../../features/ordering/history'
import { STATUS_LABELS } from '../../features/ordering/placement'
import type { OrderSummary } from '../../features/ordering/types'
import { formatMoney } from '../../lib/format/money'

type PageState =
  | { kind: 'loading' }
  | { kind: 'loaded'; orders: OrderSummary[]; nextCursor: string | null; locale: string | undefined }
  | { kind: 'error'; message: string }

type MoreState = { kind: 'idle' } | { kind: 'loading' } | { kind: 'error'; message: string }

const LINK_CLASS = 'rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700'
const SECONDARY_BUTTON_CLASS =
  'rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-60'

/**
 * /orders — the patron's order history (AC-12.1), newest first, a page at a time: when each order was placed, its
 * drinks, total, order status and payment status. Each links to its order page, where it can be paid, cancelled or a
 * restaurant request answered. Only the patron's own orders are ever returned by the server.
 */
export function MyOrdersPage() {
  const [state, setState] = useState<PageState>({ kind: 'loading' })
  const [more, setMore] = useState<MoreState>({ kind: 'idle' })
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      fetchOrders(null, controller.signal),
      // Only the display locale is needed; the list still shows if ordering is unavailable.
      fetchOrderingOptions(controller.signal).then(
        (options) => options.display.locale,
        () => undefined,
      ),
    ])
      .then(([page, locale]) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'loaded', orders: page.items, nextCursor: page.nextCursor, locale })
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'error', message: describeOrderingError(error) })
        }
      })
    return () => controller.abort()
  }, [attempt])

  const loadMore = (cursor: string) => {
    setMore({ kind: 'loading' })
    fetchOrders(cursor)
      .then((page) => {
        setState((current) => {
          if (current.kind !== 'loaded') {
            return current
          }
          const seen = new Set(current.orders.map((order) => order.reference))
          return {
            ...current,
            orders: [...current.orders, ...page.items.filter((order) => !seen.has(order.reference))],
            nextCursor: page.nextCursor,
          }
        })
        setMore({ kind: 'idle' })
      })
      .catch((error: unknown) => setMore({ kind: 'error', message: describeOrderingError(error) }))
  }

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
              setMore({ kind: 'idle' })
              setAttempt((count) => count + 1)
            }}
            className={`${SECONDARY_BUTTON_CLASS} mt-3`}
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
          <p className="mt-1 text-sm text-slate-600">
            {state.orders.length === 1 ? '1 order' : `${state.orders.length} orders`}, newest first.
          </p>
          <ul className="mt-4 divide-y divide-slate-100">
            {state.orders.map((order) => (
              <li key={order.reference} className="flex flex-wrap items-start justify-between gap-3 py-3">
                <div>
                  <Link to={`/orders/${order.reference}`} className="font-semibold text-brand-700 underline">
                    Order {order.reference}
                  </Link>
                  <p className="text-sm text-slate-600">Placed {formatPlacedAt(order.placedAtUtc, order.pickup.timeZone, state.locale)}</p>
                  <p className="text-sm text-slate-600">
                    {order.drinkCount === 1 ? '1 milkshake' : `${order.drinkCount} milkshakes`} · {order.restaurantName} · Pickup{' '}
                    {formatLocalTime(order.pickup.local, state.locale)}
                  </p>
                  <ul aria-label={`Milkshakes in order ${order.reference}`} className="mt-1 list-disc pl-5 text-sm text-slate-700">
                    {order.drinks.map((drink) => (
                      <li key={drink.number}>
                        {drink.flavour} · {drink.consistency} · {drink.topping}
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="text-right text-sm">
                  <p className="font-medium">{formatMoney(order.total, order.currencyCode, state.locale)}</p>
                  <p>
                    {STATUS_LABELS[order.status]} · {paymentText(order)}
                  </p>
                </div>
              </li>
            ))}
          </ul>

          {more.kind === 'error' && (
            <p role="alert" className="mt-4 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
              {more.message}
            </p>
          )}

          {state.nextCursor !== null ? (
            <button
              type="button"
              disabled={more.kind === 'loading'}
              aria-busy={more.kind === 'loading'}
              onClick={() => {
                if (state.nextCursor !== null) {
                  loadMore(state.nextCursor)
                }
              }}
              className={`${SECONDARY_BUTTON_CLASS} mt-4`}
            >
              {more.kind === 'loading' ? 'Loading more orders…' : 'Load more orders'}
            </button>
          ) : (
            <p className="mt-4 text-sm text-slate-500">That is all your orders.</p>
          )}
        </>
      )}
    </section>
  )
}
