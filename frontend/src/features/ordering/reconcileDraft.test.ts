import { describe, expect, it } from 'vitest'
import { orderingOptions } from '../../test/orderingFixtures'
import { reconcileDraft, RECONCILE_MESSAGES } from './reconcileDraft'
import type { OrderDraft } from './types'

const valid: OrderDraft = {
  restaurantId: 35,
  pickupLocal: '2026-11-20T10:00',
  drinks: [{ flavourId: 3, consistencyId: 29, toppingId: 19 }],
}

describe('reconcileDraft', () => {
  it('returns the same draft and no notices when everything is still offered', () => {
    const result = reconcileDraft(valid, orderingOptions())

    expect(result.draft).toBe(valid)
    expect(result.notices).toEqual([])
  })

  it('clears a choice that is no longer offered and says which milkshake', () => {
    const draft: OrderDraft = { ...valid, drinks: [valid.drinks[0]!, { flavourId: 999, consistencyId: 29, toppingId: 19 }] }

    const result = reconcileDraft(draft, orderingOptions())

    expect(result.draft.drinks[1]).toEqual({ flavourId: null, consistencyId: 29, toppingId: 19 })
    expect(result.draft.drinks[0]).toEqual(valid.drinks[0])
    expect(result.notices).toEqual([RECONCILE_MESSAGES.choice('flavour', 2)])
  })

  it('clears a restaurant that is no longer active', () => {
    const result = reconcileDraft({ ...valid, restaurantId: 77 }, orderingOptions())

    expect(result.draft.restaurantId).toBeNull()
    expect(result.notices).toEqual([RECONCILE_MESSAGES.restaurant])
  })

  it.each(['2026-11-19T12:00', '2026-11-27T00:00'])('clears a pickup time outside the current window (%s)', (pickupLocal) => {
    const result = reconcileDraft({ ...valid, pickupLocal }, orderingOptions())

    expect(result.draft.pickupLocal).toBeNull()
    expect(result.notices).toEqual([RECONCILE_MESSAGES.pickup])
  })

  it('says when no pickup times are available at all', () => {
    const options = orderingOptions()
    options.pickup.hasAvailableTimes = false

    const result = reconcileDraft(valid, options)

    expect(result.draft.pickupLocal).toBeNull()
    expect(result.notices).toEqual([RECONCILE_MESSAGES.noPickupTimes])
  })

  it('reports a quantity above a lowered maximum but keeps every milkshake', () => {
    const options = orderingOptions()
    options.drinkLimits = { min: 1, max: 1 }
    const draft = { ...valid, drinks: [valid.drinks[0]!, valid.drinks[0]!] }

    const result = reconcileDraft(draft, options)

    expect(result.draft.drinks).toHaveLength(2)
    expect(result.notices).toEqual([RECONCILE_MESSAGES.quantity(1, 1)])
  })
})