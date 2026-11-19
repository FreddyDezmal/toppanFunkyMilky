import type { CurrentUser } from '../features/auth/types'
import type { DraftSummary, OrderingOptions } from '../features/ordering/types'

export const patronUser: CurrentUser = {
  id: '01a1ea38-0000-7000-8000-000000000011',
  firstName: 'Thandi',
  email: 'thandi@example.test',
  mobileNumber: '+27821234567',
  roles: ['Patron'],
}

/**
 * Options as GET /api/ordering/options returns them, "now" being 2026-11-19 12:00 in Johannesburg
 * (15-minute lead time, 7 days ahead). A fresh copy each call, so tests can change it.
 */
export function orderingOptions(): OrderingOptions {
  return {
    flavours: [
      { id: 3, name: 'Vanilla', fee: { amount: 30, currencyCode: 'ZAR' } },
      { id: 4, name: 'Chocolate', fee: { amount: 32, currencyCode: 'ZAR' } },
    ],
    consistencies: [
      { id: 29, name: 'Thick', fee: { amount: 6, currencyCode: 'ZAR' } },
      { id: 30, name: 'Milky', fee: { amount: 0, currencyCode: 'ZAR' } },
    ],
    toppings: [
      { id: 19, name: 'Oreo crumbs', fee: { amount: 10, currencyCode: 'ZAR' } },
      { id: 20, name: 'Caramel', fee: { amount: 8, currencyCode: 'ZAR' } },
    ],
    restaurants: [
      { id: 35, name: 'Branch A', address: '1 Main Road' },
      { id: 36, name: 'Branch B', address: null },
    ],
    drinkLimits: { min: 1, max: 10 },
    pickup: {
      timeZone: 'Africa/Johannesburg',
      leadTimeMinutes: 15,
      maxAdvanceDays: 7,
      earliestLocal: '2026-11-19T12:15',
      latestLocal: '2026-11-26T23:59',
      earliestUtc: '2026-11-19T10:15:00+00:00',
      latestUtc: '2026-11-26T21:59:00+00:00',
      hasAvailableTimes: true,
    },
    display: { locale: 'en-ZA', currencyCode: 'ZAR' },
  }
}

/** A 200 from POST /api/ordering/draft/validate for one Vanilla / Thick / Oreo crumbs milkshake at Branch A. */
export function draftSummary(): DraftSummary {
  const options = orderingOptions()
  return {
    isDraft: true,
    restaurant: options.restaurants[0]!,
    pickup: { local: '2026-11-20T10:00', utc: '2026-11-20T08:00:00+00:00', timeZone: 'Africa/Johannesburg' },
    drinks: [{ number: 1, flavour: options.flavours[0]!, consistency: options.consistencies[0]!, topping: options.toppings[0]! }],
    note: 'Draft – not yet ordered or paid. Fees shown are current fees; the final price, VAT and any discount are calculated at checkout.',
  }
}