import { DRINK_FIELD_LABELS, DRINK_FIELD_OPTIONS, DRINK_FIELDS, type OrderDraft, type OrderingOptions } from './types'

/** Messages match the API's wording, so the patron sees the same text whichever side noticed first. */
export const RECONCILE_MESSAGES = {
  restaurant: 'The selected restaurant is no longer available. Please choose another.',
  choice: (label: string, number: number) =>
    `The ${label} chosen for milkshake ${number} is no longer available. Please choose another.`,
  pickup: 'Your pickup time is no longer available. Please choose another time.',
  noPickupTimes: 'No pickup times are available right now. Please try again later.',
  quantity: (min: number, max: number) =>
    min === max ? `Order exactly ${min} milkshake(s).` : `Choose between ${min} and ${max} milkshakes.`,
} as const

export interface ReconciledDraft {
  /** The draft with stale selections cleared (the same object when nothing changed). */
  draft: OrderDraft
  /** One message per change, for the patron (decision E6). */
  notices: string[]
}

/**
 * Brings a stored draft in line with freshly loaded options: choices that are no longer offered and a pickup time
 * outside the current window are cleared, with a notice for each. A quantity outside the limits is reported but
 * kept, so the patron decides which milkshakes to drop. This is a courtesy only: the API validates everything again.
 */
export function reconcileDraft(draft: OrderDraft, options: OrderingOptions): ReconciledDraft {
  const notices: string[] = []
  let changed = false

  let restaurantId = draft.restaurantId
  if (restaurantId !== null && !options.restaurants.some((r) => r.id === restaurantId)) {
    restaurantId = null
    changed = true
    notices.push(RECONCILE_MESSAGES.restaurant)
  }

  const drinks = draft.drinks.map((drink, index) => {
    let next = drink
    for (const field of DRINK_FIELDS) {
      const id = drink[field]
      if (id !== null && !options[DRINK_FIELD_OPTIONS[field]].some((option) => option.id === id)) {
        next = { ...next, [field]: null }
        changed = true
        notices.push(RECONCILE_MESSAGES.choice(DRINK_FIELD_LABELS[field], index + 1))
      }
    }
    return next
  })

  let pickupLocal = draft.pickupLocal
  if (pickupLocal !== null && !isWithinWindow(pickupLocal, options)) {
    pickupLocal = null
    changed = true
    notices.push(options.pickup.hasAvailableTimes ? RECONCILE_MESSAGES.pickup : RECONCILE_MESSAGES.noPickupTimes)
  }

  const { min, max } = options.drinkLimits
  if (draft.drinks.length > 0 && (draft.drinks.length < min || draft.drinks.length > max)) {
    notices.push(RECONCILE_MESSAGES.quantity(min, max))
  }

  return { draft: changed ? { restaurantId, pickupLocal, drinks } : draft, notices }
}

/**
 * Local times share one fixed format ("yyyy-MM-ddTHH:mm"), so comparing the strings compares the times
 * without converting through the browser's time zone.
 */
export function isWithinWindow(pickupLocal: string, options: OrderingOptions): boolean {
  const { hasAvailableTimes, earliestLocal, latestLocal } = options.pickup
  return hasAvailableTimes && pickupLocal >= earliestLocal && pickupLocal <= latestLocal
}