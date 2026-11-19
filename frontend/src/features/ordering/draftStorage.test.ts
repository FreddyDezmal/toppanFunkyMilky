import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { EMPTY_DRAFT } from './draft'
import { clearOrderDrafts, draftStorageKey, loadDraft, saveDraft } from './draftStorage'
import type { OrderDraft } from './types'

const thandi = 'user-thandi'
const sipho = 'user-sipho'
const draft: OrderDraft = {
  restaurantId: 35,
  pickupLocal: '2026-11-20T10:00',
  drinks: [{ flavourId: 3, consistencyId: 29, toppingId: null }],
}

beforeEach(() => {
  sessionStorage.clear()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('draft storage', () => {
  it('saves and loads a draft for the same user', () => {
    saveDraft(thandi, draft)

    expect(loadDraft(thandi)).toEqual(draft)
  })

  it('keeps each user’s draft separate', () => {
    saveDraft(thandi, draft)

    expect(loadDraft(sipho)).toEqual(EMPTY_DRAFT)
  })

  it('removes the entry when the draft is empty', () => {
    saveDraft(thandi, draft)
    saveDraft(thandi, EMPTY_DRAFT)

    expect(sessionStorage.getItem(draftStorageKey(thandi))).toBeNull()
  })

  it('rebuilds tampered data, dropping prices, names and malformed values', () => {
    sessionStorage.setItem(
      draftStorageKey(thandi),
      JSON.stringify({
        restaurantId: '35',
        pickupLocal: '2026-11-20T10:00:00Z',
        total: 0.01,
        drinks: [
          { flavourId: 3, consistencyId: -2, toppingId: 1.5, price: 0.01, name: 'Free shake' },
          'not a drink',
        ],
      }),
    )

    const loaded = loadDraft(thandi)

    expect(loaded).toEqual({
      restaurantId: null,
      pickupLocal: null,
      drinks: [
        { flavourId: 3, consistencyId: null, toppingId: null },
        { flavourId: null, consistencyId: null, toppingId: null },
      ],
    })
    expect(JSON.stringify(loaded)).not.toMatch(/price|total|name/)
  })

  it('caps the number of stored milkshakes', () => {
    const many = Array.from({ length: 500 }, () => ({ flavourId: 3, consistencyId: 29, toppingId: 19 }))
    sessionStorage.setItem(draftStorageKey(thandi), JSON.stringify({ drinks: many }))

    expect(loadDraft(thandi).drinks).toHaveLength(50)
  })

  it('treats unreadable data as no draft', () => {
    sessionStorage.setItem(draftStorageKey(thandi), '{not json')

    expect(loadDraft(thandi)).toEqual(EMPTY_DRAFT)
  })

  it('clears every user’s draft, or every draft but one, without touching other entries', () => {
    saveDraft(thandi, draft)
    saveDraft(sipho, draft)
    sessionStorage.setItem('something-else', 'keep me')

    clearOrderDrafts(thandi)
    expect(loadDraft(thandi)).toEqual(draft)
    expect(loadDraft(sipho)).toEqual(EMPTY_DRAFT)

    clearOrderDrafts()
    expect(loadDraft(thandi)).toEqual(EMPTY_DRAFT)
    expect(sessionStorage.getItem('something-else')).toBe('keep me')
  })

  it('carries on when storage is unavailable', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Quota exceeded', 'QuotaExceededError')
    })
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Access denied', 'SecurityError')
    })

    expect(() => saveDraft(thandi, draft)).not.toThrow()
    expect(loadDraft(thandi)).toEqual(EMPTY_DRAFT)
  })
})