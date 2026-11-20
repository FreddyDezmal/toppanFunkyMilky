import type { CurrentUser } from '../features/auth/types'
import type { DraftSummary, DrinkSelection, OrderingOptions, PlacedOrder, Quote } from '../features/ordering/types'

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
    // 30.00 + 6.00 + 10.00 = 46.00; VAT 15% = 6.90; total 52.90.
    drinks: [{ number: 1, flavour: options.flavours[0]!, consistency: options.consistencies[0]!, topping: options.toppings[0]!, price: 46 }],
    totals: {
      currencyCode: 'ZAR',
      subtotal: 46,
      discount: { amount: 0, tierNumber: null, rate: null, capApplied: false },
      taxable: 46,
      vatRate: 0.15,
      vat: 6.9,
      total: 52.9,
    },
    isEstimate: true,
    pricedAtUtc: '2026-11-19T10:00:00+00:00',
    note: 'Draft – not yet ordered or paid. Prices are an estimate based on current fees and VAT; the final amount is calculated when you place your order.',
  }
}

/**
 * A stand-in for POST /api/ordering/quote in component tests, using the fixture fees. It plays the server's part;
 * the screens under test never calculate prices themselves. Amounts are worked in cents to stay exact.
 */
export function quoteFor(drinks: DrinkSelection[]): Quote {
  const options = orderingOptions()
  const cents = (list: { id: number; fee: { amount: number } }[], id: number | null) =>
    Math.round((list.find((option) => option.id === id)?.fee.amount ?? 0) * 100)
  const lines = drinks.map((drink, index) => ({
    number: index + 1,
    price:
      drink.flavourId === null || drink.consistencyId === null || drink.toppingId === null
        ? null
        : (cents(options.flavours, drink.flavourId) + cents(options.consistencies, drink.consistencyId) + cents(options.toppings, drink.toppingId)) / 100,
  }))
  const complete = lines.every((line) => line.price !== null)
  const subtotalCents = lines.reduce((sum, line) => sum + Math.round((line.price ?? 0) * 100), 0)
  const vatCents = Math.round((subtotalCents * 15) / 100)

  return {
    isEstimate: true,
    pricedAtUtc: '2026-11-19T10:00:00+00:00',
    currencyCode: 'ZAR',
    vatRate: 0.15,
    drinks: lines,
    totals: complete
      ? {
          currencyCode: 'ZAR',
          subtotal: subtotalCents / 100,
          discount: { amount: 0, tierNumber: null, rate: null, capApplied: false },
          taxable: subtotalCents / 100,
          vatRate: 0.15,
          vat: vatCents / 100,
          total: (subtotalCents + vatCents) / 100,
        }
      : null,
    note: 'Estimated prices based on current fees and VAT. The final amount is calculated when you place your order.',
  }
}

/**
 * A placed order as GET/POST /api/ordering/orders return it: one milkshake at Branch A, unpaid. Its names and fees are
 * the stored snapshot ("Vanilla (old recipe)" at 30.00), deliberately different from today's options.
 */
export function placedOrder(reference = 'MS-7KQ2-X9TD'): PlacedOrder {
  const options = orderingOptions()
  return {
    reference,
    status: 'PendingPayment',
    isPaid: false,
    awaitsPayment: true,
    restaurant: options.restaurants[0]!,
    pickup: { local: '2026-11-20T10:00', utc: '2026-11-20T08:00:00+00:00', timeZone: 'Africa/Johannesburg' },
    drinks: [
      {
        number: 1,
        flavour: { id: 3, name: 'Vanilla (old recipe)', fee: 30 },
        consistency: { id: 29, name: 'Thick', fee: 6 },
        topping: { id: 19, name: 'Oreo crumbs', fee: 10 },
        price: 46,
      },
    ],
    totals: draftSummary().totals,
    pricedAtUtc: '2026-11-19T10:00:00+00:00',
    placedAtUtc: '2026-11-19T10:00:01+00:00',
    note: 'Your order has been placed and is awaiting payment. It has not been paid.',
  }
}
