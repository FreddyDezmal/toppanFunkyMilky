import { describe, expect, it } from 'vitest'
import {
  areDrinksComplete,
  draftReducer,
  EMPTY_DRAFT,
  EMPTY_DRINK,
  isDrinkComplete,
  MAX_DRAFT_DRINKS,
  quantityChoices,
} from './draft'
import type { OrderDraft } from './types'

const complete = { flavourId: 3, consistencyId: 29, toppingId: 19 }

function draftWith(...drinks: OrderDraft['drinks']): OrderDraft {
  return { restaurantId: 35, pickupLocal: '2026-11-20T10:00', drinks }
}

describe('draftReducer', () => {
  it('grows the quantity with empty milkshakes and keeps the existing ones', () => {
    const next = draftReducer(draftWith(complete), { type: 'setQuantity', quantity: 3 })

    expect(next.drinks).toEqual([complete, EMPTY_DRINK, EMPTY_DRINK])
    expect(next.restaurantId).toBe(35)
  })

  it('shrinks the quantity by dropping milkshakes from the end', () => {
    const second = { ...complete, toppingId: 7 }

    const next = draftReducer(draftWith(complete, second, complete), { type: 'setQuantity', quantity: 2 })

    expect(next.drinks).toEqual([complete, second])
  })

  it.each([-1, 1.5, Number.NaN, MAX_DRAFT_DRINKS + 1])('ignores an impossible quantity (%s)', (quantity) => {
    const draft = draftWith(complete)

    expect(draftReducer(draft, { type: 'setQuantity', quantity })).toBe(draft)
  })

  it('sets one choice on one milkshake only', () => {
    const next = draftReducer(draftWith(complete, { ...EMPTY_DRINK }), {
      type: 'setDrinkChoice',
      index: 1,
      field: 'consistencyId',
      id: 29,
    })

    expect(next.drinks).toEqual([complete, { ...EMPTY_DRINK, consistencyId: 29 }])
  })

  it('ignores a choice for a milkshake that does not exist', () => {
    const draft = draftWith(complete)

    expect(draftReducer(draft, { type: 'setDrinkChoice', index: 4, field: 'flavourId', id: 3 })).toBe(draft)
    expect(draftReducer(draft, { type: 'setDrinkChoice', index: -1, field: 'flavourId', id: 3 })).toBe(draft)
  })

  it('removes one milkshake, keeping the others in order', () => {
    const second = { ...complete, flavourId: 8 }
    const third = { ...complete, flavourId: 9 }

    const next = draftReducer(draftWith(complete, second, third), { type: 'removeDrink', index: 1 })

    expect(next.drinks).toEqual([complete, third])
  })

  it('sets the restaurant and pickup time, and resets to an empty draft', () => {
    let draft = draftReducer(EMPTY_DRAFT, { type: 'setRestaurant', restaurantId: 12 })
    draft = draftReducer(draft, { type: 'setPickup', pickupLocal: '2026-11-21T09:15' })

    expect(draft).toEqual({ restaurantId: 12, pickupLocal: '2026-11-21T09:15', drinks: [] })
    expect(draftReducer(draft, { type: 'reset' })).toEqual(EMPTY_DRAFT)
  })
})

describe('draft helpers', () => {
  it('offers every quantity from the minimum to the maximum', () => {
    expect(quantityChoices({ min: 1, max: 4 })).toEqual([1, 2, 3, 4])
    expect(quantityChoices({ min: 3, max: 3 })).toEqual([3])
    expect(quantityChoices({ min: 5, max: 2 })).toEqual([])
  })

  it('treats a milkshake as complete only with all three choices', () => {
    expect(isDrinkComplete(complete)).toBe(true)
    expect(isDrinkComplete({ ...complete, toppingId: null })).toBe(false)
    expect(areDrinksComplete(draftWith(complete, complete))).toBe(true)
    expect(areDrinksComplete(draftWith(complete, EMPTY_DRINK))).toBe(false)
    expect(areDrinksComplete(draftWith())).toBe(false)
  })
})