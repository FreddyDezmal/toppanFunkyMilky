import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { buttonClasses } from '../../components/ui/buttonClasses'
import { Card } from '../../components/ui/Card'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { Icon } from '../../components/ui/Icon'
import { Skeleton } from '../../components/ui/Skeleton'
import { formatMoney } from '../../lib/format/money'
import { useAuth } from '../../features/auth/authContext'
import { placeOrder, validateDraft } from '../../features/ordering/api'
import { forgetOrderSubmissionKey, orderSubmissionKey } from '../../features/ordering/draftStorage'
import { EmptyDraft } from '../../features/ordering/EmptyDraft'
import { draftErrorsFrom, type DraftErrors } from '../../features/ordering/errors'
import { formatLocalTime, timeZoneLabel } from '../../features/ordering/format'
import { useOrderDraft } from '../../features/ordering/orderDraftContext'
import { placementFailureMessage, isOrderTotals, PLACE_ORDER_TIMEOUT_MS } from '../../features/ordering/placement'
import { PriceSummary } from '../../features/ordering/PriceSummary'
import type { DraftSummary, OrderingOption, OrderTotals } from '../../features/ordering/types'
import { ApiError } from '../../lib/api/httpClient'
import { useOrdering } from '../../features/ordering/useOrdering'

type ReviewState =
  | { kind: 'checking' }
  | { kind: 'valid'; summary: DraftSummary }
  | { kind: 'invalid'; errors: DraftErrors }

/** Placing an order: never more than one request at a time; the draft is kept until the order is stored. */
type Placement =
  | { kind: 'idle' }
  | { kind: 'confirming'; totals: OrderTotals }
  | { kind: 'placing' }
  | { kind: 'priceChanged'; totals: OrderTotals }
  | { kind: 'failed'; message: string }

const LINK_CLASS = buttonClasses({ variant: 'secondary', size: 'sm', className: 'whitespace-nowrap' })

/** Which step fixes an error, by its field key. */
function stepFor(field: string): { to: string; label: string } {
  if (field === 'restaurantId' || field === 'pickupLocal') {
    return { to: '/order/pickup', label: 'Change pickup' }
  }
  if (field === 'drinks') {
    return { to: '/order', label: 'Change quantity' }
  }
  return { to: '/order/drinks', label: 'Change milkshakes' }
}

/**
 * /order/review — the server checks the draft against the options in force now and returns the summary shown here,
 * with its estimated price (M6). Place order (M7) asks for confirmation of that total, then the server revalidates,
 * reprices and stores the order unpaid. Every amount comes from the server; the draft is cleared only once the order
 * is stored.
 */
export function OrderReviewPage() {
  const { options, reloadOptions } = useOrdering()
  const { draft, dispatch } = useOrderDraft()
  const { user } = useAuth()
  const navigate = useNavigate()
  const [placement, setPlacement] = useState<Placement>({ kind: 'idle' })
  const placing = useRef(false)
  const placeController = useRef<AbortController | null>(null)
  const [state, setState] = useState<ReviewState>({ kind: 'checking' })
  const [attempt, setAttempt] = useState(0)
  const [confirmStartOver, setConfirmStartOver] = useState(false)
  const locale = options.display.locale

  useEffect(() => {
    if (draft.drinks.length === 0) {
      return undefined
    }
    const controller = new AbortController()
    validateDraft(draft, controller.signal)
      .then((summary) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'valid', summary })
        }
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        const errors = draftErrorsFrom(error)
        setState({ kind: 'invalid', errors })
        // Something may have changed since the options were loaded: reload them so stale choices are cleared (E6).
        if (Object.keys(errors.fields).length > 0) {
          reloadOptions()
        }
      })
    return () => controller.abort()
  }, [draft, attempt, reloadOptions])

  // Leaving the page cancels a request in flight; the stored key makes a later retry safe.
  useEffect(() => () => placeController.current?.abort(), [])

  if (draft.drinks.length === 0) {
    return <EmptyDraft />
  }

  async function place(expected: OrderTotals) {
    if (placing.current || !user) {
      return
    }
    placing.current = true
    setPlacement({ kind: 'placing' })
    const controller = new AbortController()
    placeController.current = controller
    let timedOut = false
    const timer = setTimeout(() => {
      timedOut = true
      controller.abort()
    }, PLACE_ORDER_TIMEOUT_MS)

    try {
      const key = orderSubmissionKey(user.id, draft)
      const order = await placeOrder(draft, { total: expected.total, currencyCode: expected.currencyCode }, key, controller.signal)
      // Stored on the server: only now is the draft cleared.
      forgetOrderSubmissionKey(user.id)
      dispatch({ type: 'reset' })
      void navigate(`/orders/${encodeURIComponent(order.reference)}`, { replace: true })
    } catch (error: unknown) {
      if (controller.signal.aborted && !timedOut) {
        return
      }
      if (error instanceof ApiError && error.status === 409 && isOrderTotals(error.problem?.currentTotals)) {
        setPlacement({ kind: 'priceChanged', totals: error.problem.currentTotals })
        return
      }
      if (error instanceof ApiError && error.status === 422) {
        forgetOrderSubmissionKey(user.id)
      }
      if (error instanceof ApiError && error.status === 400 && Object.keys(error.fieldErrors).length > 0) {
        setPlacement({ kind: 'idle' })
        setState({ kind: 'invalid', errors: draftErrorsFrom(error) })
        reloadOptions()
        return
      }
      setPlacement({ kind: 'failed', message: placementFailureMessage(error, timedOut) })
    } finally {
      clearTimeout(timer)
      placing.current = false
    }
  }

  function fee(option: OrderingOption) {
    return formatMoney(option.fee.amount, option.fee.currencyCode, locale)
  }

  const placeLabel =
    placement.kind === 'placing' ? 'Placing your order…' : placement.kind === 'failed' ? 'Try again' : 'Place order'

  return (
    <section aria-labelledby="review-heading">
      <h1 id="review-heading" className="text-3xl font-semibold text-chocolate sm:text-4xl">
        Review your order
      </h1>

      {state.kind === 'checking' && (
        <div className="mt-6">
          <p role="status" className="text-warm-grey">
            Checking your choices…
          </p>
          <Card className="mt-4 space-y-3">
            <Skeleton className="h-5 w-48" />
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-4 w-2/3" />
          </Card>
        </div>
      )}

      {state.kind === 'invalid' && (
        <Alert tone="danger" role="alert" className="mt-6">
          <h2 className="font-semibold">Some choices need attention</h2>
          {state.errors.form && <p className="mt-1">{state.errors.form}</p>}
          {Object.keys(state.errors.fields).length > 0 && (
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {Object.entries(state.errors.fields).map(([field, message]) => (
                <li key={field}>{message}</li>
              ))}
            </ul>
          )}
          <div className="mt-3 flex flex-wrap gap-2">
            {[...new Map(Object.keys(state.errors.fields).map((field) => [stepFor(field).to, stepFor(field)])).values()].map(
              (step) => (
                <Link key={step.to} to={step.to} className={LINK_CLASS}>
                  {step.label}
                </Link>
              ),
            )}
            {state.errors.form && (
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  setState({ kind: 'checking' })
                  setAttempt((count) => count + 1)
                }}
              >
                Try again
              </Button>
            )}
          </div>
        </Alert>
      )}

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="min-w-0 space-y-6">
          {state.kind === 'valid' && (
            <>
              <Alert tone="info">
                <span className="font-semibold">{state.summary.note}</span>
              </Alert>

              <Card as="section" aria-labelledby="review-pickup-heading" padding="md">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h2 id="review-pickup-heading" className="flex items-center gap-2 font-display text-xl font-semibold text-chocolate">
                    <Icon name="store" className="size-5 text-raspberry" />
                    Pickup
                  </h2>
                  <Link to="/order/pickup" className={LINK_CLASS}>
                    Change pickup
                  </Link>
                </div>
                <p className="mt-3 font-semibold text-chocolate">{state.summary.restaurant.name}</p>
                {state.summary.restaurant.address && <p className="text-sm text-warm-grey">{state.summary.restaurant.address}</p>}
                <p className="mt-3 flex items-center gap-2 text-chocolate">
                  <Icon name="clock" className="size-5 text-raspberry" />
                  {formatLocalTime(state.summary.pickup.local, locale)}
                </p>
                <p className="mt-1 text-sm text-warm-grey">{timeZoneLabel(state.summary.pickup.timeZone, locale)}</p>
              </Card>

              <Card as="section" aria-labelledby="review-drinks-heading" padding="md">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h2 id="review-drinks-heading" className="font-display text-xl font-semibold text-chocolate">
                    {state.summary.drinks.length === 1 ? '1 milkshake' : `${state.summary.drinks.length} milkshakes`}
                  </h2>
                  <Link to="/order/drinks" className={LINK_CLASS}>
                    Change milkshakes
                  </Link>
                </div>
                <ol className="mt-3 divide-y divide-line">
                  {state.summary.drinks.map((drink) => (
                    <li key={drink.number} className="py-3">
                      <h3 className="flex justify-between gap-4 font-semibold text-chocolate">
                        <span>Milkshake {drink.number}</span>{' '}
                        <span className="tabular-nums">{formatMoney(drink.price, state.summary.totals.currencyCode, locale)}</span>
                      </h3>
                      <ul className="mt-1 text-sm text-warm-grey">
                        <li>
                          {drink.flavour.name} · {fee(drink.flavour)}
                        </li>
                        <li>
                          {drink.consistency.name} · {fee(drink.consistency)}
                        </li>
                        <li>
                          {drink.topping.name} · {fee(drink.topping)}
                        </li>
                      </ul>
                    </li>
                  ))}
                </ol>
              </Card>
            </>
          )}

          {placement.kind === 'priceChanged' && (
            <section role="alert" aria-labelledby="price-changed-heading" className="rounded-card border border-warning/30 bg-warning-tint p-5 text-sm text-warning">
              <h2 id="price-changed-heading" className="font-semibold">
                The price has changed
              </h2>
              <p className="mt-1">
                Prices changed since you reviewed your order. The new total is{' '}
                {formatMoney(placement.totals.total, placement.totals.currencyCode, locale)}. Nothing has been ordered yet.
              </p>
              <div className="mt-3 max-w-sm rounded-control bg-surface p-3 text-chocolate">
                <PriceSummary totals={placement.totals} locale={locale} totalLabel="New total" />
              </div>
            </section>
          )}

          {placement.kind === 'failed' && (
            <Alert tone="danger" role="alert">
              {placement.message}
            </Alert>
          )}
        </div>

        <aside aria-label="Place your order" className="lg:sticky lg:top-24 lg:self-start">
          <Card padding="md">
            {state.kind === 'valid' && (
              <section aria-labelledby="review-price-heading">
                <h2 id="review-price-heading" className="mb-3 font-display text-xl font-semibold text-chocolate">
                  Estimated price
                </h2>
                <PriceSummary totals={state.summary.totals} locale={locale} />
              </section>
            )}
            <div className={`flex flex-col gap-2 ${state.kind === 'valid' ? 'mt-6' : ''}`}>
              {state.kind === 'valid' && placement.kind === 'priceChanged' ? (
                <Button size="lg" fullWidth onClick={() => void place(placement.totals)}>
                  Place order at {formatMoney(placement.totals.total, placement.totals.currencyCode, locale)}
                </Button>
              ) : (
                <Button
                  size="lg"
                  fullWidth
                  disabled={state.kind !== 'valid' || placement.kind === 'placing'}
                  aria-describedby="place-order-note"
                  onClick={() => {
                    if (state.kind === 'valid') {
                      setPlacement({ kind: 'confirming', totals: state.summary.totals })
                    }
                  }}
                >
                  {placeLabel}
                </Button>
              )}
              <Button
                variant="ghost"
                fullWidth
                disabled={placement.kind === 'placing'}
                onClick={() => setConfirmStartOver(true)}
                className="text-danger hover:bg-danger-tint hover:text-danger"
              >
                Start over
              </Button>
            </div>
            <p role="status" className="mt-3 text-sm text-warm-grey" id="place-order-note">
              {placement.kind === 'placing'
                ? 'Placing your order…'
                : 'Placing your order saves it as awaiting payment. You pay on the next page.'}
            </p>
          </Card>
        </aside>
      </div>

      <ConfirmDialog
        open={placement.kind === 'confirming'}
        title="Place your order?"
        message={
          placement.kind === 'confirming'
            ? `Your order will be placed for ${formatMoney(placement.totals.total, placement.totals.currencyCode, locale)} and saved as awaiting payment. You pay on the next page.`
            : ''
        }
        confirmLabel="Place order"
        cancelLabel="Not yet"
        onConfirm={() => {
          if (placement.kind === 'confirming') {
            void place(placement.totals)
          }
        }}
        onCancel={() => setPlacement({ kind: 'idle' })}
      />

      <ConfirmDialog
        open={confirmStartOver}
        title="Start over?"
        message="Every milkshake, the restaurant and the pickup time will be cleared from this draft."
        confirmLabel="Start over"
        cancelLabel="Keep my draft"
        onConfirm={() => {
          setConfirmStartOver(false)
          dispatch({ type: 'reset' })
          void navigate('/order')
        }}
        onCancel={() => setConfirmStartOver(false)}
      />
    </section>
  )
}
