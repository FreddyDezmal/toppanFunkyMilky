import { DRINK_FIELDS, type DrinkField, type DrinkSelection, type OrderDraft } from './types'

/**
 * Upper bound on drinks the browser will hold, whatever it is told: the highest maximum a manager can configure
 * (SettingRules, decision D6). The real limits come from GET /api/ordering/options and are enforced by the API.
 */
export const MAX_DRAFT_DRINKS = 50

export const EMPTY_DRINK: DrinkSelection = Object.freeze({ flavourId: null, consistencyId: null, toppingId: null })

export const EMPTY_DRAFT: OrderDraft = Object.freeze({ restaurantId: null, pickupLocal: null, drinks: [] })

export type DraftAction =
  | { type: 'setQuantity'; quantity: number }
  | { type: 'setDrinkChoice'; index: number; field: DrinkField; id: number | null }
  | { type: 'removeDrink'; index: number }
  | { type: 'setRestaurant'; restaurantId: number | null }
  | { type: 'setPickup'; pickupLocal: string | null }
  | { type: 'replace'; draft: OrderDraft }
  | { type: 'reset' }

/** Pure draft updates. Returns the same object when nothing changes, so React can skip re-rendering. */
export function draftReducer(draft: OrderDraft, action: DraftAction): OrderDraft {
  switch (action.type) {
    case 'setQuantity': {
      const { quantity } = action
      if (!Number.isInteger(quantity) || quantity < 0 || quantity > MAX_DRAFT_DRINKS || quantity === draft.drinks.length) {
        return draft
      }
      // Growing keeps every existing milkshake; shrinking drops from the end (the screen asks first).
      const drinks =
        quantity < draft.drinks.length
          ? draft.drinks.slice(0, quantity)
          : [...draft.drinks, ...Array.from({ length: quantity - draft.drinks.length }, () => ({ ...EMPTY_DRINK }))]
      return { ...draft, drinks }
    }

    case 'setDrinkChoice': {
      const current = draft.drinks.at(action.index)
      if (action.index < 0 || !current || current[action.field] === action.id) {
        return draft
      }
      const drinks = draft.drinks.map((drink, index) => (index === action.index ? { ...drink, [action.field]: action.id } : drink))
      return { ...draft, drinks }
    }

    case 'removeDrink':
      if (action.index < 0 || action.index >= draft.drinks.length) {
        return draft
      }
      return { ...draft, drinks: draft.drinks.filter((_, index) => index !== action.index) }

    case 'setRestaurant':
      return draft.restaurantId === action.restaurantId ? draft : { ...draft, restaurantId: action.restaurantId }

    case 'setPickup':
      return draft.pickupLocal === action.pickupLocal ? draft : { ...draft, pickupLocal: action.pickupLocal }

    case 'replace':
      return action.draft

    case 'reset':
      return EMPTY_DRAFT
  }
}

/** The quantities a patron may pick: min to max inclusive (from the live limits). */
export function quantityChoices(limits: { min: number; max: number }): number[] {
  const min = Math.max(1, limits.min)
  const max = Math.min(MAX_DRAFT_DRINKS, limits.max)
  return max < min ? [] : Array.from({ length: max - min + 1 }, (_, index) => min + index)
}

/** True when the milkshake has a flavour, consistency and topping. */
export function isDrinkComplete(drink: DrinkSelection): boolean {
  return DRINK_FIELDS.every((field) => drink[field] !== null)
}

/** True when there is at least one milkshake and every one is complete. */
export function areDrinksComplete(draft: OrderDraft): boolean {
  return draft.drinks.length > 0 && draft.drinks.every(isDrinkComplete)
}

/** True when the draft holds nothing worth keeping. */
export function isDraftEmpty(draft: OrderDraft): boolean {
  return draft.drinks.length === 0 && draft.restaurantId === null && draft.pickupLocal === null
}