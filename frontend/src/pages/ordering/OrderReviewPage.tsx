import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
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

const LINK_CLASS = 'rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50'

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

  return (
    <section aria-labelledby="review-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="review-heading" className="text-2xl font-bold tracking-tight">
        Review your order
      </h1>

      {state.kind === 'checking' && (
        <p role="status" className="mt-4 text-sm text-slate-500">
          Checking your choices…
        </p>
      )}

      {state.kind === 'invalid' && (
        <div role="alert" className="mt-4 rounded-lg bg-rose-50 p-4 text-sm text-rose-800">
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
                <Link key={step.to} to={step.to} className={`${LINK_CLASS} bg-white`}>
                  {step.label}
                </Link>
              ),
            )}
            {state.errors.form && (
              <button
                type="button"
                onClick={() => {
                  setState({ kind: 'checking' })
                  setAttempt((count) => count + 1)
                }}
                className={`${LINK_CLASS} bg-white`}
              >
                Try again
              </button>
            )}
          </div>
        </div>
      )}

      {state.kind === 'valid' && (
        <>
          <p className="mt-4 rounded-lg bg-brand-50 p-3 text-sm font-medium text-brand-900">{state.summary.note}</p>

          <div className="mt-6 grid gap-6 sm:grid-cols-2">
            <div>
              <h2 className="text-lg font-semibold">Pickup</h2>
              <p className="mt-1 font-medium">{state.summary.restaurant.name}</p>
              {state.summary.restaurant.address && <p className="text-sm text-slate-600">{state.summary.restaurant.address}</p>}
              <p className="mt-2">{formatLocalTime(state.summary.pickup.local, locale)}</p>
              <p className="text-xs text-slate-500">{timeZoneLabel(state.summary.pickup.timeZone, locale)}</p>
            </div>

            <div>
              <h2 className="text-lg font-semibold">
                {state.summary.drinks.length === 1 ? '1 milkshake' : `${state.summary.drinks.length} milkshakes`}
              </h2>
              <ol className="mt-1 space-y-3">
                {state.summary.drinks.map((drink) => (
                  <li key={drink.number}>
                    <h3 className="flex justify-between gap-4 font-medium">
                      <span>Milkshake {drink.number}</span>{' '}
                      <span>{formatMoney(drink.price, state.summary.totals.currencyCode, locale)}</span>
                    </h3>
                    <ul className="text-sm text-slate-700">
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
            </div>
          </div>

          <section aria-labelledby="review-price-heading" className="mt-6 max-w-sm rounded-xl bg-slate-50 p-4">
            <h2 id="review-price-heading" className="mb-2 text-lg font-semibold">
              Estimated price
            </h2>
            <PriceSummary totals={state.summary.totals} locale={locale} />
          </section>

          <div className="mt-6 flex flex-wrap gap-2">
            <Link to="/order/drinks" className={LINK_CLASS}>
              Change milkshakes
            </Link>
            <Link to="/order/pickup" className={LINK_CLASS}>
              Change pickup
            </Link>
          </div>
        </>
      )}

      {placement.kind === 'priceChanged' && (
        <section role="alert" aria-labelledby="price-changed-heading" className="mt-6 rounded-lg bg-amber-50 p-4 text-sm text-amber-900">
          <h2 id="price-changed-heading" className="font-semibold">
            The price has changed
          </h2>
          <p className="mt-1">
            Prices changed since you reviewed your order. The new total is{' '}
            {formatMoney(placement.totals.total, placement.totals.currencyCode, locale)}. Nothing has been ordered yet.
          </p>
          <div className="mt-3 max-w-sm rounded-lg bg-white p-3">
            <PriceSummary totals={placement.totals} locale={locale} totalLabel="New total" />
          </div>
        </section>
      )}

      {placement.kind === 'failed' && (
        <p role="alert" className="mt-6 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          {placement.message}
        </p>
      )}

      <div className="mt-8 flex flex-wrap items-center gap-3 border-t border-slate-100 pt-6">
        {state.kind === 'valid' && placement.kind === 'priceChanged' ? (
          <button
            type="button"
            onClick={() => void place(placement.totals)}
            className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
          >
            Place order at {formatMoney(placement.totals.total, placement.totals.currencyCode, locale)}
          </button>
        ) : (
          <button
            type="button"
            disabled={state.kind !== 'valid' || placement.kind === 'placing'}
            aria-describedby="place-order-note"
            onClick={() => {
              if (state.kind === 'valid') {
                setPlacement({ kind: 'confirming', totals: state.summary.totals })
              }
            }}
            className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50"
          >
            {placement.kind === 'placing' ? 'Placing your order…' : placement.kind === 'failed' ? 'Try again' : 'Place order'}
          </button>
        )}
        <button
          type="button"
          disabled={placement.kind === 'placing'}
          onClick={() => setConfirmStartOver(true)}
          className="rounded-lg px-3 py-2 text-sm font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-50"
        >
          Start over
        </button>
        <p role="status" className="w-full text-xs text-slate-500" id="place-order-note">
          {placement.kind === 'placing'
            ? 'Placing your order…'
            : 'Placing your order saves it as awaiting payment. You will not be charged now: payment is not available yet.'}
        </p>
      </div>

      <ConfirmDialog
        open={placement.kind === 'confirming'}
        title="Place your order?"
        message={
          placement.kind === 'confirming'
            ? `Your order will be placed for ${formatMoney(placement.totals.total, placement.totals.currencyCode, locale)} and saved as awaiting payment. You will not be charged now.`
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