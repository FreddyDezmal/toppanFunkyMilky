import { useState } from 'react'
import { useNavigate } from 'react-router'
import { ChoiceCardGroup } from '../../components/forms/ChoiceCardGroup'
import { TextField } from '../../components/forms/TextField'
import { focusFirstError } from '../../components/forms/focusFirstError'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Icon } from '../../components/ui/Icon'
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
    <Card as="section" aria-labelledby="pickup-heading" className="mx-auto max-w-3xl">
      <h1 id="pickup-heading" className="text-3xl font-semibold text-chocolate sm:text-4xl">
        Where and when?
      </h1>
      <p className="mt-2 text-warm-grey">Choose the restaurant you will collect from and a pickup time.</p>

      {!pickup.hasAvailableTimes && (
        <Alert tone="warning" role="alert" className="mt-4">
          {RECONCILE_MESSAGES.noPickupTimes}
        </Alert>
      )}

      <div className="mt-8 space-y-8">
        <ChoiceCardGroup
          id={DRAFT_FIELDS.restaurantId}
          name="pickup-restaurant"
          legend="Pickup restaurant"
          layout="list"
          value={draft.restaurantId === null ? '' : String(draft.restaurantId)}
          options={options.restaurants.map((restaurant) => ({
            value: String(restaurant.id),
            label: restaurant.name,
            detail: restaurant.address ?? undefined,
            visual: (
              <span className="flex size-10 items-center justify-center rounded-full bg-strawberry-tint text-raspberry">
                <Icon name="store" />
              </span>
            ),
          }))}
          error={errors[DRAFT_FIELDS.restaurantId]}
          onChange={(value) => {
            dispatch({ type: 'setRestaurant', restaurantId: Number(value) })
            clearError(DRAFT_FIELDS.restaurantId)
          }}
        />
        <div className="max-w-md">
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
        </div>
      </div>

      <div className="mt-10 flex flex-col-reverse gap-3 sm:flex-row sm:justify-between">
        <Button variant="secondary" size="lg" onClick={() => void navigate('/order/drinks')} icon={<Icon name="chevron-left" />}>
          Back to milkshakes
        </Button>
        <Button size="lg" onClick={handleContinue}>
          Review order
          <Icon name="arrow-right" />
        </Button>
      </div>
    </Card>
  )
}
