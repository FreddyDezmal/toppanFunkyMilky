import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { bodyOf, callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import type { DiscountTier } from './types'

const BLOCKED = 'Discount tiers cannot be activated until the discount rules are confirmed (open questions Q-01 to Q-04).'
const RATE_ERROR = 'Enter a fraction from 0 to 1 with at most four decimal places, e.g. 0.05 for 5%.'
const LIST = 'GET /api/manager/discount-tiers'

function tier(tierNumber: number, minPreviousOrders: number, minDrinksPerOrder: number, discountRate: number): DiscountTier {
  return {
    tierNumber,
    minPreviousOrders,
    minDrinksPerOrder,
    discountRate,
    isActive: false,
    canActivate: false,
    activationBlockedReason: BLOCKED,
    version: 1,
  }
}

const tiers = [tier(1, 5, 2, 0.05), tier(2, 10, 3, 0.1), tier(3, 20, 4, 0.15)]
const normalise = (text: string) => text.replace(/\s+/g, ' ')

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('discount tier management', () => {
  it('shows the tiers with rates as percentages and activation switched off', async () => {
    renderManagerAt('/manager/discount-tiers', { [LIST]: () => jsonResponse(tiers) })

    expect(await screen.findByText(BLOCKED)).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /Tier 1/ })
    const fivePercent = normalise(new Intl.NumberFormat('en-ZA', { style: 'percent', maximumFractionDigits: 2 }).format(0.05))
    expect(within(row).getByText(fivePercent)).toBeInTheDocument()
    for (const button of screen.getAllByRole('button', { name: /Activate tier/ })) {
      expect(button).toBeDisabled()
    }
  })

  it('saves new thresholds and a rate typed with a comma', async () => {
    const fetchMock = renderManagerAt('/manager/discount-tiers', {
      [LIST]: () => jsonResponse(tiers),
      'PUT /api/manager/discount-tiers/2': () => jsonResponse({ ...tier(2, 12, 3, 0.125), version: 2 }),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit tier 2' }))
    expect(screen.getByLabelText('Paid orders needed')).toHaveFocus()
    await user.clear(screen.getByLabelText('Paid orders needed'))
    await user.type(screen.getByLabelText('Paid orders needed'), '12')
    await user.clear(screen.getByLabelText('Discount rate'))
    await user.type(screen.getByLabelText('Discount rate'), '0,125')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Saved tier 2.')).toBeInTheDocument()
    const [put] = callsTo(fetchMock, 'PUT', '/api/manager/discount-tiers/2')
    expect(bodyOf(put)).toEqual({ minPreviousOrders: 12, minDrinksPerOrder: 3, discountRate: 0.125, version: 1 })
  })

  it('refuses a rate above 1 without calling the API', async () => {
    const fetchMock = renderManagerAt('/manager/discount-tiers', { [LIST]: () => jsonResponse(tiers) })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit tier 1' }))
    await user.clear(screen.getByLabelText('Discount rate'))
    await user.type(screen.getByLabelText('Discount rate'), '1.5')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(RATE_ERROR)).toBeInTheDocument()
    expect(screen.getByLabelText('Discount rate')).toHaveFocus()
    expect(callsTo(fetchMock, 'PUT', '/api/manager/discount-tiers/1')).toHaveLength(0)
  })
})