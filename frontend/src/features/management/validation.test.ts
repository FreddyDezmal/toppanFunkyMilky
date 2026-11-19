import { describe, expect, it } from 'vitest'
import { parseAmount, validateCatalogForm } from './validation'

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