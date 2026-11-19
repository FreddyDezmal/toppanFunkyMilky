import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { bodyOf, callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import { CONFLICT_MESSAGE } from './errors'
import type { Setting } from './types'

function setting(key: string, value: string, valueType: string, isEditable: boolean, hint: string): Setting {
  return { key, value, valueType, description: null, isEditable, hint, version: 1 }
}

const LEAD_TIME = 'Pickup.MinLeadTimeMinutes'
const MAX_DRINKS = 'Orders.MaxDrinksPerOrder'

const allSettings: Setting[] = [
  setting('Orders.MinDrinksPerOrder', '1', 'WholeNumber', true, 'Whole number, at least 1.'),
  setting(MAX_DRINKS, '10', 'WholeNumber', true, 'Whole number up to 50.'),
  setting('Tax.VatRate', '0.15', 'DecimalNumber', true, 'Fraction from 0 to 1.'),
  setting('Discounts.MaxDiscountAmount', '50.00', 'DecimalNumber', true, 'Meaning pending confirmation (open question Q-02).'),
  setting(LEAD_TIME, '15', 'WholeNumber', true, 'Whole number of minutes from 0 to 240.'),
  setting('Pricing.DisplayLocale', 'en-ZA', 'Text', true, 'Locale used to format prices, e.g. en-ZA.'),
  setting('Pricing.CurrencyCode', 'ZAR', 'Text', false, 'Read-only.'),
]

const LIST = 'GET /api/manager/settings'

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('settings management', () => {
  it('lists every setting, with the currency read-only and the discount caveat visible', async () => {
    renderManagerAt('/manager/settings', { [LIST]: () => jsonResponse(allSettings) })

    expect(await screen.findByRole('heading', { name: 'Minimum pickup lead time' })).toBeInTheDocument()
    expect(screen.getByText('15 minutes')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit VAT rate' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit Currency' })).not.toBeInTheDocument()
    expect(screen.getByText('Read-only')).toBeInTheDocument()
    expect(screen.getByText('Meaning pending confirmation (open question Q-02).')).toBeInTheDocument()
  })

  it('saves a setting and shows the values returned by the server', async () => {
    const fetchMock = renderManagerAt('/manager/settings', {
      [LIST]: () => jsonResponse(allSettings),
      [`PUT /api/manager/settings/${LEAD_TIME}`]: () =>
        jsonResponse(allSettings.map((s) => (s.key === LEAD_TIME ? { ...s, value: '20', version: 2 } : s))),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit Minimum pickup lead time' }))
    const input = screen.getByLabelText('Minimum pickup lead time')
    expect(input).toHaveFocus()
    await user.clear(input)
    await user.type(input, '20')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved “Minimum pickup lead time”. It applies to orders placed from now on.')).toBeInTheDocument()
    expect(screen.getByText('20 minutes')).toBeInTheDocument()
    const [put] = callsTo(fetchMock, 'PUT', `/api/manager/settings/${LEAD_TIME}`)
    expect(bodyOf(put)).toEqual({ value: '20', version: 1 })
  })

  it("shows the server's limit message on the field and focuses it", async () => {
    const limit = 'Maximum drinks per order cannot be more than 50.'
    renderManagerAt('/manager/settings', {
      [LIST]: () => jsonResponse(allSettings),
      [`PUT /api/manager/settings/${MAX_DRINKS}`]: () =>
        jsonResponse({ status: 400, title: 'One or more validation errors occurred.', errors: { value: [limit] } }, 400),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit Maximum drinks per order' }))
    const input = screen.getByLabelText('Maximum drinks per order')
    await user.clear(input)
    await user.type(input, '51')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(limit)).toBeInTheDocument()
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveFocus()
  })

  it('explains a conflict and offers a reload', async () => {
    renderManagerAt('/manager/settings', {
      [LIST]: () => jsonResponse(allSettings),
      [`PUT /api/manager/settings/${LEAD_TIME}`]: () =>
        jsonResponse({ status: 409, title: 'This record was changed by someone else.' }, 409),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit Minimum pickup lead time' }))
    await user.type(screen.getByLabelText('Minimum pickup lead time'), '0')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(CONFLICT_MESSAGE)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })
})