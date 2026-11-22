import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { jsonResponse } from '../../test/mockApi'
import { callsTo, renderManagerAt } from '../../test/managementFixtures'

const overviewRoutes = {
  'GET /api/manager/ping': () => jsonResponse({ message: 'Manager access confirmed.' }),
  'GET /api/health': () => jsonResponse({ status: 'Healthy', checkedAtUtc: '2026-11-22T06:00:00Z', totalDurationMs: 3, checks: [] }),
  'GET /api/manager/reports/orders': () =>
    jsonResponse({ from: '2026-11-22', to: '2026-11-22', timeZone: 'Africa/Johannesburg', totalOrders: 3, totalDrinks: 5, page: 1, pageSize: 50, totalPages: 1, rows: [] }),
  'GET /api/manager/refunds/attention': () => jsonResponse([]),
  'GET /api/manager/emails': () => jsonResponse({ items: [], counts: { Pending: 1, Sending: 0, Sent: 9, Failed: 2, Suppressed: 0 } }),
}

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => vi.unstubAllGlobals())

describe('management overview', () => {
  it('shows only figures the API provides, each linking to its section', async () => {
    const fetchMock = renderManagerAt('/manager', overviewRoutes)

    const today = await screen.findByRole('region', { name: 'Today’s orders' })
    expect(await within(today).findByText('3')).toBeInTheDocument()
    expect(within(today).getByText(/^5 milkshakes placed on /)).toBeInTheDocument()
    expect(within(today).getByRole('link', { name: 'Open reports' })).toHaveAttribute('href', '/manager/reports')

    const refunds = screen.getByRole('region', { name: 'Refunds needing attention' })
    expect(await within(refunds).findByText('Nothing needs attention.')).toBeInTheDocument()

    const emails = screen.getByRole('region', { name: 'Emails' })
    expect(await within(emails).findByText('2')).toBeInTheDocument()
    expect(within(emails).getByText('1 waiting · 9 sent')).toBeInTheDocument()

    expect(screen.getByText('Manager access confirmed.')).toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/api/manager/reports/orders').length).toBeGreaterThan(0)
  })

  it('keeps the other cards when one figure cannot be loaded', async () => {
    renderManagerAt('/manager', { ...overviewRoutes, 'GET /api/manager/emails': () => jsonResponse({ status: 500 }, 500) })

    const emails = await screen.findByRole('region', { name: 'Emails' })
    expect(await within(emails).findByText('Could not be loaded just now.')).toBeInTheDocument()
    expect(await within(screen.getByRole('region', { name: 'Today’s orders' })).findByText('3')).toBeInTheDocument()
  })
})

describe('management sections menu', () => {
  it('groups the sections and folds them behind one button on small screens', async () => {
    renderManagerAt('/manager', overviewRoutes)

    const toggle = await screen.findByRole('button', { name: /Sections/ })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(toggle).toHaveAttribute('aria-controls', 'management-sections')
    const nav = screen.getByRole('navigation', { name: 'Management sections' })
    for (const name of ['Overview', 'Flavours', 'Orders', 'Emails', 'Reports', 'Restaurants', 'Settings', 'Discount tiers']) {
      expect(within(nav).getByRole('link', { name })).toBeInTheDocument()
    }
    expect(within(nav).getByText('Operations')).toBeInTheDocument()

    await userEvent.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    await userEvent.keyboard('{Escape}')
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(toggle).toHaveFocus()
  })
})
