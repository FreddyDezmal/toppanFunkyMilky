/** An amount in an ISO 4217 currency. A current fee shown for information, never a final price. */
export interface Fee {
  amount: number
  currencyCode: string
}

/** A flavour, consistency or topping a patron can choose. */
export interface OrderingOption {
  id: number
  name: string
  fee: Fee
}

/** An active pickup restaurant. */
export interface OrderingRestaurant {
  id: number
  name: string
  address: string | null
}

/**
 * Allowed pickup times (PROVISIONAL rules, open questions Q-05, Q-06, Q-17, Q-18).
 * Local values ("2026-11-20T14:30") are wall-clock times in `timeZone`, not the browser's zone.
 */
export interface PickupWindow {
  timeZone: string
  leadTimeMinutes: number
  maxAdvanceDays: number
  earliestLocal: string
  latestLocal: string
  /** UTC ISO 8601. */
  earliestUtc: string
  /** UTC ISO 8601. */
  latestUtc: string
  hasAvailableTimes: boolean
}

/** GET /api/ordering/options: everything the ordering screens offer, read at one moment. */
export interface OrderingOptions {
  flavours: OrderingOption[]
  consistencies: OrderingOption[]
  toppings: OrderingOption[]
  restaurants: OrderingRestaurant[]
  drinkLimits: { min: number; max: number }
  pickup: PickupWindow
  display: { locale: string; currencyCode: string }
}

/** The three choices every milkshake needs, named as in the API request. */
export const DRINK_FIELDS = ['flavourId', 'consistencyId', 'toppingId'] as const
export type DrinkField = (typeof DRINK_FIELDS)[number]

/** Lower-case labels, matching the API's messages ("Choose a topping for milkshake 2."). */
export const DRINK_FIELD_LABELS: Record<DrinkField, string> = {
  flavourId: 'flavour',
  consistencyId: 'consistency',
  toppingId: 'topping',
}

/** Which options list each field chooses from. */
export const DRINK_FIELD_OPTIONS: Record<DrinkField, 'flavours' | 'consistencies' | 'toppings'> = {
  flavourId: 'flavours',
  consistencyId: 'consistencies',
  toppingId: 'toppings',
}

/** One milkshake as chosen so far: identifiers only. */
export type DrinkSelection = Record<DrinkField, number | null>

/**
 * The patron's draft, kept in the browser (decision E2). Identifiers and the pickup time only:
 * no names, fees or totals, so nothing here can be trusted for pricing. The quantity is `drinks.length`.
 */
export interface OrderDraft {
  restaurantId: number | null
  /** Business-local "yyyy-MM-ddTHH:mm", or null when not chosen. */
  pickupLocal: string | null
  drinks: DrinkSelection[]
}

/** The chosen pickup time as confirmed by the API. */
export interface DraftPickup {
  local: string
  /** UTC ISO 8601. */
  utc: string
  timeZone: string
}

/** A validated milkshake with the names and current fees of its choices, and its price (the sum of the fees). */
export interface DraftSummaryDrink {
  number: number
  flavour: OrderingOption
  consistency: OrderingOption
  topping: OrderingOption
  price: number
}

/** The frequent-customer discount applied: 0 with no tier while discounts are deferred (M6 decision P5). */
export interface DiscountSummary {
  amount: number
  tierNumber: number | null
  rate: number | null
  capApplied: boolean
}

/**
 * Order totals calculated by the server (M6): subtotal, discount before VAT, taxable amount, VAT rate (a fraction,
 * 0.15 = 15%) and amount, and total. Display only: the browser never calculates or sends these.
 */
export interface OrderTotals {
  currencyCode: string
  subtotal: number
  discount: DiscountSummary
  taxable: number
  vatRate: number
  vat: number
  total: number
}

/** POST /api/ordering/draft/validate (200): a valid draft with its estimated price. Not an order. */
export interface DraftSummary {
  isDraft: true
  restaurant: OrderingRestaurant
  pickup: DraftPickup
  drinks: DraftSummaryDrink[]
  totals: OrderTotals
  isEstimate: true
  /** UTC ISO 8601: when the fees and VAT were read. */
  pricedAtUtc: string
  note: string
}

/** One milkshake in a live quote: its price, or null until all three choices are made. */
export interface QuotedDrink {
  number: number
  price: number | null
}

/** POST /api/ordering/quote (200): a non-binding estimate. `totals` is null until every milkshake is complete. */
export interface Quote {
  isEstimate: true
  /** UTC ISO 8601. */
  pricedAtUtc: string
  currencyCode: string
  vatRate: number
  drinks: QuotedDrink[]
  totals: OrderTotals | null
  note: string
}