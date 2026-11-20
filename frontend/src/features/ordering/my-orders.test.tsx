import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { formatMoney } from '../../lib/format/money'
import { jsonResponse } from '../../test/mockApi'
import { callsTo, renderOrderingAt } from '../../test/renderOrdering'
import type { OrderSummary } from './types'

const LIST = 'GET /api/ordering/orders'
const zar = (amount: number) => formatMoney(amount, 'ZAR', 'en-ZA').replace(/\s/g, ' ')

function summary(overrides: Partial<OrderSummary> = {}): OrderSummary {
  return {
    reference: 'MS-7KQ2-X9TD',
    status: 'Confirmed',
    isPaid: true,
    awaitsPayment: false,
    restaurantName: 'Branch A',
    pickup: { local: '2026-11-20T10:00', utc: '2026-11-20T08:00:00+00:00', timeZone: 'Africa/Johannesburg' },
    drinkCount: 2,
    total: 52.9,
    currencyCode: 'ZAR',
    placedAtUtc: '2026-11-20T07:00:00+00:00',
    ...overrides,
  }
}

beforeEach(() => {
  sessionStorage.clear()
  clearCsrfToken()
})

afterEach(() => vi.unstubAllGlobals())

describe('My orders', () => {
  it('is in the header for patrons', async () => {
    renderOrderingAt('/orders', { [LIST]: () => jsonResponse([]) })

    const nav = await screen.findByRole('navigation', { name: 'Main' })
    expect(await within(nav).findByRole('link', { name: 'My orders' })).toHaveAttribute('href', '/orders')
  })

  it('lists the patron’s orders with their status, payment and total, each linking to its page', async () => {
    renderOrderingAt('/orders', {
      [LIST]: () => jsonResponse([summary(), summary({ reference: 'MS-AAAA-BBBB', status: 'Cancelled', isPaid: false, drinkCount: 1, total: 29.9 })]),
    })

    const first = (await screen.findByRole('link', { name: 'Order MS-7KQ2-X9TD' })).closest('li')!
    expect(within(first).getByRole('link', { name: 'Order MS-7KQ2-X9TD' })).toHaveAttribute('href', '/orders/MS-7KQ2-X9TD')
    expect(first).toHaveTextContent('2 milkshakes · Branch A')
    expect(first).toHaveTextContent(zar(52.9))
    expect(first).toHaveTextContent('Confirmed · Paid')

    const second = screen.getByRole('link', { name: 'Order MS-AAAA-BBBB' }).closest('li')!
    expect(second).toHaveTextContent('1 milkshake')
    expect(second).toHaveTextContent('Cancelled · Not paid')
  })

  it('opens an order from the list', async () => {
    renderOrderingAt('/orders', {
      [LIST]: () => jsonResponse([summary()]),
      'GET /api/ordering/orders/MS-7KQ2-X9TD': () => jsonResponse({ status: 404, title: 'Order not found.' }, 404),
    })

    await userEvent.click(await screen.findByRole('link', { name: 'Order MS-7KQ2-X9TD' }))

    expect(await screen.findByRole('heading', { name: 'Order not found' })).toBeInTheDocument()
  })

  it('invites a patron without orders to start one', async () => {
    renderOrderingAt('/orders', { [LIST]: () => jsonResponse([]) })

    expect(await screen.findByText('You have not placed any orders yet.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Start an order' })).toHaveAttribute('href', '/order')
  })

  it('explains a failure and tries again', async () => {
    let attempts = 0
    const fetchMock = renderOrderingAt('/orders', {
      [LIST]: () => {
        attempts += 1
        return attempts === 1 ? Promise.reject(new TypeError('Failed to fetch')) : jsonResponse([summary()])
      },
    })

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('link', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/api/ordering/orders')).toHaveLength(2)
  })
})
