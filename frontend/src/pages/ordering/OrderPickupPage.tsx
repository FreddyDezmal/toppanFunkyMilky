import { useState } from 'react'
import { useNavigate } from 'react-router'
import { SelectField } from '../../components/forms/SelectField'
import { TextField } from '../../components/forms/TextField'
import { focusFirstError } from '../../components/forms/focusFirstError'
import { EmptyDraft } from '../../features/ordering/EmptyDraft'
import { DRAFT_FIELDS } from '../../features/ordering/errors'
import { formatLocalTime, timeZoneLabel } from '../../features/ordering/format'
import { useOrderDraft } from '../../features/ordering/orderDraftContext'
import { isWithinWindow, RECONCILE_MESSAGES } from '../../features/ordering/reconcileDraft'
import { useOrdering } from '../../features/ordering/useOrdering'

const FIELDS = [DRAFT_FIELDS.restaurantId, DRAFT_FIELDS.pickupLocal] as const
const LOCAL_MINUTES = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/

/** /order/pickup — an active restaurant and a pickup time inside the provisional window. */
export function OrderPickupPage() {
  const { options } = useOrdering()
  const { draft, dispatch } = useOrderDraft()
  const navigate = useNavigate()
  const [errors, setErrors] = useState<Record<string, string>>({})

  if (draft.drinks.length === 0) {
    return <EmptyDraft />
  }

  const { pickup, display } = options
  const zone = timeZoneLabel(pickup.timeZone, display.locale)
  const earliest = formatLocalTime(pickup.earliestLocal, display.locale)
  const latest = formatLocalTime(pickup.latestLocal, display.locale)

  function clearError(field: string) {
    setErrors((current) => Object.fromEntries(Object.entries(current).filter(([name]) => name !== field)))
  }

  function handleContinue() {
    const next: Record<string, string> = {}
    if (draft.restaurantId === null) {
      next[DRAFT_FIELDS.restaurantId] = 'Choose a pickup restaurant.'
    }
    if (!pickup.hasAvailableTimes) {
      next[DRAFT_FIELDS.pickupLocal] = RECONCILE_MESSAGES.noPickupTimes
    } else if (draft.pickupLocal === null) {
      next[DRAFT_FIELDS.pickupLocal] = 'Choose a pickup date and time.'
    } else if (!isWithinWindow(draft.pickupLocal, options)) {
      next[DRAFT_FIELDS.pickupLocal] = `Choose a time between ${earliest} and ${latest}.`
    }

    setErrors(next)
    if (Object.keys(next).length > 0) {
      focusFirstError(FIELDS, next)
      return
    }
    void navigate('/order/review')
  }

  return (
    <section aria-labelledby="pickup-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="pickup-heading" className="text-2xl font-bold tracking-tight">
        Where and when?
      </h1>
      <p className="mt-1 text-sm text-slate-600">Choose the restaurant you will collect from and a pickup time.</p>

      {!pickup.hasAvailableTimes && (
        <p role="alert" className="mt-4 rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
          {RECONCILE_MESSAGES.noPickupTimes}
        </p>
      )}

      <div className="mt-6 max-w-md space-y-4">
        <SelectField
          id={DRAFT_FIELDS.restaurantId}
          label="Pickup restaurant"
          value={draft.restaurantId === null ? '' : String(draft.restaurantId)}
          placeholder="Choose a restaurant"
          options={options.restaurants.map((restaurant) => ({
            value: String(restaurant.id),
            label: restaurant.address ? `${restaurant.name} – ${restaurant.address}` : restaurant.name,
          }))}
          error={errors[DRAFT_FIELDS.restaurantId]}
          onChange={(value) => {
            dispatch({ type: 'setRestaurant', restaurantId: value === '' ? null : Number(value) })
            clearError(DRAFT_FIELDS.restaurantId)
          }}
        />
        <TextField
          id={DRAFT_FIELDS.pickupLocal}
          label="Pickup date and time"
          type="datetime-local"
          value={draft.pickupLocal ?? ''}
          min={pickup.earliestLocal}
          max={pickup.latestLocal}
          disabled={!pickup.hasAvailableTimes}
          hint={`Times are in ${zone}. Earliest: ${earliest}. Latest: ${latest}.`}
          error={errors[DRAFT_FIELDS.pickupLocal]}
          onChange={(value) => {
            // Browsers send "yyyy-MM-ddTHH:mm"; anything longer is trimmed to the minute, the API's format.
            const minutes = LOCAL_MINUTES.exec(value)?.[0] ?? null
            dispatch({ type: 'setPickup', pickupLocal: minutes })
            clearError(DRAFT_FIELDS.pickupLocal)
          }}
        />
        <p className="text-xs text-slate-500">
          Pickup times are provisional: opening hours and collection slots are not yet confirmed.
        </p>
      </div>

      <div className="mt-6 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => void navigate('/order/drinks')}
          className="rounded-lg border border-slate-300 px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          Back to milkshakes
        </button>
        <button
          type="button"
          onClick={handleContinue}
          className="rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700"
        >
          Review order
        </button>
      </div>
    </section>
  )
}