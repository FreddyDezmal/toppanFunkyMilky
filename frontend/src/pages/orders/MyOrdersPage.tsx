import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fetchOrderingOptions, fetchOrders } from '../../features/ordering/api'
import { describeOrderingError } from '../../features/ordering/errors'
import { formatLocalTime } from '../../features/ordering/format'
import { formatPlacedAt, paymentText } from '../../features/ordering/history'
import { OrderStatusBadges } from '../../features/ordering/OrderStatusBadges'
import type { OrderSummary } from '../../features/ordering/types'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { buttonClasses } from '../../components/ui/buttonClasses'
import { EmptyState } from '../../components/ui/EmptyState'
import { Icon } from '../../components/ui/Icon'
import { PageHeader } from '../../components/ui/PageHeader'
import { Skeleton } from '../../components/ui/Skeleton'
import { formatMoney } from '../../lib/format/money'

type PageState =
  | { kind: 'loading' }
  | { kind: 'loaded'; orders: OrderSummary[]; nextCursor: string | null; locale: string | undefined }
  | { kind: 'error'; message: string }

type MoreState = { kind: 'idle' } | { kind: 'loading' } | { kind: 'error'; message: string }

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
    <section aria-labelledby="my-orders-heading" className="mx-auto max-w-4xl">
      <div>
        <PageHeader
          id="my-orders-heading"
          title="My orders"
          description={
            state.kind === 'loaded' && state.orders.length > 0
              ? `${state.orders.length === 1 ? '1 order' : `${state.orders.length} orders`}, newest first.`
              : undefined
          }
          actions={
            state.kind === 'loaded' && state.orders.length > 0 ? (
              <Link to="/order" className={buttonClasses({ variant: 'secondary' })}>
                Build Your Milkshake
              </Link>
            ) : undefined
          }
        />
      </div>

      {state.kind === 'loading' && (
        <div className="mt-6 space-y-3">
          <p role="status" className="sr-only">
            Loading your orders…
          </p>
          {[0, 1, 2].map((key) => (
            <div key={key} className="rounded-card border border-line bg-surface p-5 shadow-soft">
              <Skeleton className="h-5 w-40" />
              <Skeleton className="mt-3 h-4 w-3/4" />
              <Skeleton className="mt-2 h-4 w-1/2" />
            </div>
          ))}
        </div>
      )}

      {state.kind === 'error' && (
        <Alert tone="danger" role="alert" className="mt-6">
          <p>{state.message}</p>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
              setState({ kind: 'loading' })
              setMore({ kind: 'idle' })
              setAttempt((count) => count + 1)
            }}
            className="mt-3"
          >
            Try again
          </Button>
        </Alert>
      )}

      {state.kind === 'loaded' && state.orders.length === 0 && (
        <div className="mt-6">
          <EmptyState
            title="No orders yet"
            icon="receipt"
            action={
              <Link to="/order" className={buttonClasses()}>
                Start an order
              </Link>
            }
          >
            You have not placed any orders yet.
          </EmptyState>
        </div>
      )}

      {state.kind === 'loaded' && state.orders.length > 0 && (
        <>
          {/* Cards on phones; one list in a single card from the md breakpoint. */}
          <ul className="mt-6 space-y-3 md:space-y-0 md:divide-y md:divide-line md:overflow-hidden md:rounded-card md:border md:border-line md:bg-surface md:shadow-soft">
            {state.orders.map((order) => (
              <li
                key={order.reference}
                className="grid gap-3 rounded-card border border-line bg-surface p-4 shadow-soft md:grid-cols-[minmax(0,1fr)_auto] md:gap-6 md:rounded-none md:border-0 md:px-6 md:py-5 md:shadow-none"
              >
                <div className="min-w-0">
                  <Link
                    to={`/orders/${order.reference}`}
                    className="inline-flex items-center gap-1 font-display text-lg font-semibold text-raspberry underline-offset-4 hover:underline"
                  >
                    Order {order.reference}
                    <Icon name="chevron-right" className="size-4" />
                  </Link>
                  <p className="text-sm text-warm-grey">Placed {formatPlacedAt(order.placedAtUtc, order.pickup.timeZone, state.locale)}</p>
                  <p className="mt-2 text-sm text-chocolate">
                    {order.drinkCount === 1 ? '1 milkshake' : `${order.drinkCount} milkshakes`} · {order.restaurantName} · Pickup{' '}
                    {formatLocalTime(order.pickup.local, state.locale)}
                  </p>
                  <ul aria-label={`Milkshakes in order ${order.reference}`} className="mt-1 space-y-0.5 text-sm text-warm-grey">
                    {order.drinks.map((drink) => (
                      <li key={drink.number}>
                        {drink.flavour} · {drink.consistency} · {drink.topping}
                      </li>
                    ))}
                  </ul>
                </div>
                <div className="flex flex-wrap items-center justify-between gap-2 border-t border-line pt-3 md:flex-col md:items-end md:justify-start md:border-0 md:pt-0">
                  <p className="font-display text-xl font-semibold text-chocolate tabular-nums">
                    {formatMoney(order.total, order.currencyCode, state.locale)}
                  </p>
                  <OrderStatusBadges status={order.status} payment={paymentText(order)} />
                </div>
              </li>
            ))}
          </ul>

          {more.kind === 'error' && (
            <Alert tone="danger" role="alert" className="mt-4">
              {more.message}
            </Alert>
          )}

          <div className="mt-6 text-center">
            {state.nextCursor !== null ? (
              <Button
                variant="secondary"
                disabled={more.kind === 'loading'}
                aria-busy={more.kind === 'loading'}
                onClick={() => {
                  if (state.nextCursor !== null) {
                    loadMore(state.nextCursor)
                  }
                }}
              >
                {more.kind === 'loading' ? 'Loading more orders…' : 'Load more orders'}
              </Button>
            ) : (
              <p className="text-sm text-warm-grey">That is all your orders.</p>
            )}
          </div>
        </>
      )}
    </section>
  )
}
