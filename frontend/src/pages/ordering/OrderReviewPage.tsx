import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { formatMoney } from '../../lib/format/money'
import { validateDraft } from '../../features/ordering/api'
import { EmptyDraft } from '../../features/ordering/EmptyDraft'
import { draftErrorsFrom, type DraftErrors } from '../../features/ordering/errors'
import { formatLocalTime, timeZoneLabel } from '../../features/ordering/format'
import { useOrderDraft } from '../../features/ordering/orderDraftContext'
import type { DraftSummary, OrderingOption } from '../../features/ordering/types'
import { useOrdering } from '../../features/ordering/useOrdering'

type ReviewState =
  | { kind: 'checking' }
  | { kind: 'valid'; summary: DraftSummary }
  | { kind: 'invalid'; errors: DraftErrors }

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
 * /order/review — the server checks the draft against the options in force now and returns the summary shown here.
 * Fees are informational (decision E5): no totals, VAT or discount until checkout (M6). Nothing is ordered or paid.
 */
export function OrderReviewPage() {
  const { options, reloadOptions } = useOrdering()
  const { draft, dispatch } = useOrderDraft()
  const navigate = useNavigate()
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

  if (draft.drinks.length === 0) {
    return <EmptyDraft />
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
                    <h3 className="font-medium">Milkshake {drink.number}</h3>
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

      <div className="mt-8 flex flex-wrap items-center gap-3 border-t border-slate-100 pt-6">
        <button
          type="button"
          disabled
          aria-describedby="payment-coming-soon"
          className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
        >
          Continue to payment (coming soon)
        </button>
        <button
          type="button"
          onClick={() => setConfirmStartOver(true)}
          className="rounded-lg px-3 py-2 text-sm font-medium text-rose-700 hover:bg-rose-50"
        >
          Start over
        </button>
        <p id="payment-coming-soon" className="w-full text-xs text-slate-500">
          Payment is not available yet. Your draft stays in this browser tab until you sign out or close it.
        </p>
      </div>

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