import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router'
import { fetchOrder, fetchOrderingOptions } from '../../features/ordering/api'
import { describeOrderingError } from '../../features/ordering/errors'
import { CancellationPanel } from '../../features/ordering/CancellationPanel'
import { formatLocalTime, timeZoneLabel } from '../../features/ordering/format'
import { RETURN_PARAM } from '../../features/ordering/payment'
import { PaymentPanel } from '../../features/ordering/PaymentPanel'
import { STATUS_LABELS } from '../../features/ordering/placement'
import { Alert } from '../../components/ui/Alert'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { buttonClasses } from '../../components/ui/buttonClasses'
import { Card } from '../../components/ui/Card'
import { EmptyState } from '../../components/ui/EmptyState'
import { Icon } from '../../components/ui/Icon'
import { Skeleton } from '../../components/ui/Skeleton'
import { PriceSummary } from '../../features/ordering/PriceSummary'
import type { PlacedOrder } from '../../features/ordering/types'
import { ApiError } from '../../lib/api/httpClient'
import { formatMoney } from '../../lib/format/money'

type PageState =
  | { kind: 'loading' }
  | { kind: 'loaded'; order: PlacedOrder; locale: string | undefined }
  | { kind: 'notFound' }
  | { kind: 'error'; message: string }

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
      <div className="mx-auto max-w-5xl">
        <p role="status" className="sr-only">
          Loading your order…
        </p>
        <Skeleton className="h-10 w-72 max-w-full" />
        <Card className="mt-6 space-y-3">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-2/3" />
        </Card>
      </div>
    )
  }

  if (state.kind === 'notFound') {
    return (
      <section aria-labelledby="order-heading" className="mx-auto max-w-2xl">
        <h1 id="order-heading" className="text-3xl font-semibold text-chocolate">
          Order not found
        </h1>
        <div className="mt-6">
          <EmptyState
            title="Nothing to show"
            icon="receipt"
            action={
              <Link to="/order" className={buttonClasses()}>
                Start an order
              </Link>
            }
          >
            We could not find this order in your account.
          </EmptyState>
        </div>
      </section>
    )
  }

  if (state.kind === 'error') {
    return (
      <Alert tone="danger" role="alert" className="mx-auto max-w-2xl">
        <p>{state.message}</p>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => {
            setState({ kind: 'loading' })
            setAttempt((count) => count + 1)
          }}
          className="mt-3"
        >
          Try again
        </Button>
      </Alert>
    )
  }

  const { order, locale } = state
  const money = (amount: number) => formatMoney(amount, order.totals.currencyCode, locale)
  const paid = order.isPaid

  return (
    <section aria-labelledby="order-heading" className="mx-auto max-w-5xl">
      <Link to="/orders" className="inline-flex min-h-10 items-center gap-1 text-sm font-semibold text-raspberry hover:underline">
        <Icon name="chevron-left" className="size-4" />
        All my orders
      </Link>
      <h1 id="order-heading" ref={heading} tabIndex={-1} className="mt-2 text-3xl font-semibold text-chocolate focus:outline-none sm:text-4xl">
        Order {order.reference}
      </h1>

      <dl className="mt-4 flex flex-wrap gap-x-6 gap-y-2 text-sm">
        <div className="flex items-center gap-2">
          <dt className="text-warm-grey">Order status</dt>
          <dd>
            <Badge tone={order.status === 'Confirmed' ? 'success' : order.status === 'PaymentFailed' ? 'danger' : order.status === 'Cancelled' ? 'neutral' : 'warning'}>
              {STATUS_LABELS[order.status]}
            </Badge>
          </dd>
        </div>
        <div className="flex items-center gap-2">
          <dt className="text-warm-grey">Payment</dt>
          <dd>
            <Badge tone={paid ? 'success' : order.status === 'Cancelled' ? 'neutral' : 'warning'}>{paid ? 'Paid' : 'Not paid'}</Badge>
          </dd>
        </div>
      </dl>

      {order.note && (
        <Alert tone="warning" className="mt-4">
          <span className="font-semibold">{order.note}</span>
        </Alert>
      )}

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          <Card as="section" aria-labelledby="order-pickup-heading" padding="md">
            <h2 id="order-pickup-heading" className="flex items-center gap-2 font-display text-xl font-semibold text-chocolate">
              <Icon name="store" className="size-5 text-raspberry" />
              Pickup
            </h2>
            <p className="mt-3 font-semibold text-chocolate">{order.restaurant.name}</p>
            {order.restaurant.address && <p className="text-sm text-warm-grey">{order.restaurant.address}</p>}
            <p className="mt-3 flex items-center gap-2 text-chocolate">
              <Icon name="clock" className="size-5 text-raspberry" />
              {formatLocalTime(order.pickup.local, locale)}
            </p>
            <p className="mt-1 text-sm text-warm-grey">{timeZoneLabel(order.pickup.timeZone, locale)}</p>
          </Card>

          <Card as="section" aria-labelledby="order-drinks-heading" padding="md">
            <h2 id="order-drinks-heading" className="font-display text-xl font-semibold text-chocolate">
              {order.drinks.length === 1 ? '1 milkshake' : `${order.drinks.length} milkshakes`}
            </h2>
            <ol className="mt-3 divide-y divide-line">
              {order.drinks.map((drink) => (
                <li key={drink.number} className="py-3">
                  <h3 className="flex justify-between gap-4 font-semibold text-chocolate">
                    <span>Milkshake {drink.number}</span> <span className="tabular-nums">{money(drink.price)}</span>
                  </h3>
                  <ul className="mt-1 text-sm text-warm-grey">
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
          </Card>
        </div>

        <aside aria-label="Price and payment" className="space-y-6 lg:sticky lg:top-24 lg:self-start">
          <Card as="section" aria-labelledby="order-price-heading" padding="md">
            <h2 id="order-price-heading" className="mb-3 font-display text-xl font-semibold text-chocolate">
              Price
            </h2>
            <PriceSummary totals={order.totals} locale={locale} totalLabel="Total" />
          </Card>

          <PaymentPanel
            key={order.reference}
            reference={order.reference}
            awaitsPayment={order.awaitsPayment}
            amountLabel={money(order.totals.total)}
            returning={returning}
            onOrderChanged={reloadOrder}
          />
        </aside>
      </div>

      {/* Cancelling, restaurant requests and refunds are kept apart from paying. */}
      <div className="mt-10 border-t border-line pt-2">
        <CancellationPanel reference={order.reference} orderStatus={order.status} locale={locale} onOrderChanged={reloadOrder} />
      </div>

      <div className="mt-10">
        <Link to="/order" className={buttonClasses({ variant: 'secondary' })}>
          Start a new order
        </Link>
      </div>
    </section>
  )
}
