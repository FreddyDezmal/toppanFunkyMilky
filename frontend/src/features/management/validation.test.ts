import { describe, expect, it } from 'vitest'
import {
  parseAmount,
  validateCatalogForm,
  validatePriceForm,
  validateRestaurantForm,
  validateTierForm,
} from './validation'

const valid = { name: 'Mint', displayOrder: '2', initialPrice: '25.50' }

describe('validateCatalogForm', () => {
  it('accepts a valid new item', () => {
    expect(validateCatalogForm(valid, true)).toEqual({})
  })

  it('requires a name of at most 100 characters', () => {
    expect(validateCatalogForm({ ...valid, name: '  ' }, true).name).toBe('Name is required.')
    expect(validateCatalogForm({ ...valid, name: 'x'.repeat(101) }, true).name).toMatch(/100 characters/)
  })

  it('rejects negative and fractional display orders', () => {
    expect(validateCatalogForm({ ...valid, displayOrder: '-1' }, true).displayOrder).toBeDefined()
    expect(validateCatalogForm({ ...valid, displayOrder: '1.5' }, true).displayOrder).toBeDefined()
  })

  it('rejects a price with three decimal places', () => {
    expect(validateCatalogForm({ ...valid, initialPrice: '10.555' }, true).initialPrice).toBeDefined()
  })

  it('does not ask for a price when editing', () => {
    expect(validateCatalogForm({ ...valid, initialPrice: '' }, false)).toEqual({})
  })
})

describe('parseAmount', () => {
  it('accepts a dot or a comma as the decimal separator', () => {
    expect(parseAmount('25.50')).toBe(25.5)
    expect(parseAmount(' 25,50 ')).toBe(25.5)
    expect(parseAmount('abc')).toBeNull()
  })
})


describe('validatePriceForm', () => {
  const now = new Date('2026-11-19T10:00:00')

  it('accepts an immediate price change', () => {
    expect(validatePriceForm({ amount: '35', when: 'now', effectiveFrom: '' }, now)).toEqual({})
  })

  it('requires a valid amount', () => {
    expect(validatePriceForm({ amount: '', when: 'now', effectiveFrom: '' }, now).amount).toBe('Price is required.')
    expect(validatePriceForm({ amount: '1.234', when: 'now', effectiveFrom: '' }, now).amount).toBeDefined()
  })

  it('requires a future start time for a scheduled price', () => {
    expect(validatePriceForm({ amount: '35', when: 'later', effectiveFrom: '' }, now).effectiveFrom).toBeDefined()
    expect(validatePriceForm({ amount: '35', when: 'later', effectiveFrom: '2026-11-19T09:00' }, now).effectiveFrom).toMatch(/future/)
    expect(validatePriceForm({ amount: '35', when: 'later', effectiveFrom: '2026-11-20T09:00' }, now)).toEqual({})
  })
})

describe('validateRestaurantForm', () => {
  it('requires a name', () => {
    expect(validateRestaurantForm({ name: ' ', address: '' }).name).toBe('Name is required.')
  })

  it('allows an empty address but limits its length', () => {
    expect(validateRestaurantForm({ name: 'Harbour', address: '' })).toEqual({})
    expect(validateRestaurantForm({ name: 'Harbour', address: 'x'.repeat(251) }).address).toMatch(/250/)
  })
})


describe('validateTierForm', () => {
  it('accepts whole-number thresholds and a rate with a comma', () => {
    expect(validateTierForm({ minPreviousOrders: '0', minDrinksPerOrder: '1', discountRate: '0,0525' })).toEqual({})
  })

  it('rejects zero drinks, a rate above 1 and more than four decimals', () => {
    expect(validateTierForm({ minPreviousOrders: '5', minDrinksPerOrder: '0', discountRate: '0.05' }).minDrinksPerOrder).toBeDefined()
    expect(validateTierForm({ minPreviousOrders: '5', minDrinksPerOrder: '2', discountRate: '1.01' }).discountRate).toBeDefined()
    expect(validateTierForm({ minPreviousOrders: '5', minDrinksPerOrder: '2', discountRate: '0.12345' }).discountRate).toBeDefined()
  })
})