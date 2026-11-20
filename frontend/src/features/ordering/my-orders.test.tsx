import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { formatMoney } from '../../lib/format/money'
import { jsonResponse } from '../../test/mockApi'
import { callsTo, renderOrderingAt } from '../../test/renderOrdering'
import { formatPlacedAt, paymentText } from './history'
import type { OrderHistoryPage, OrderSummary } from './types'

const LIST = 'GET /api/ordering/orders'
const zar = (amount: number) => formatMoney(amount, 'ZAR', 'en-ZA').replace(/\s/g, ' ')

function summary(overrides: Partial<OrderSummary> = {}): OrderSummary {
  return {
    reference: 'MS-7KQ2-X9TD',
    status: 'Confirmed',
    isPaid: true,
    awaitsPayment: false,
    paymentStatus: 'Paid',
    refundStatus: null,
    restaurantName: 'Branch A',
    pickup: { local: '2026-11-20T10:00', utc: '2026-11-20T08:00:00+00:00', timeZone: 'Africa/Johannesburg' },
    drinkCount: 2,
    drinks: [
      { number: 1, flavour: 'Chocolate', consistency: 'Thick', topping: 'Oreo crumbs' },
      { number: 2, flavour: 'Vanilla', consistency: 'Thin', topping: 'Bar One syrup' },
    ],
    total: 52.9,
    currencyCode: 'ZAR',
    placedAtUtc: '2026-11-20T07:00:00+00:00',
    ...overrides,
  }
}

const page = (items: OrderSummary[], nextCursor: string | null = null): OrderHistoryPage => ({ items, nextCursor })

const oneDrink = { drinkCount: 1, drinks: [{ number: 1, flavour: 'Coffee', consistency: 'Thick', topping: 'Vermicelli' }] }

beforeEach(() => {
  sessionStorage.clear()
  clearCsrfToken()
})

afterEach(() => vi.unstubAllGlobals())

describe('My orders', () => {
  it('is in the header for patrons', async () => {
    renderOrderingAt('/orders', { [LIST]: () => jsonResponse(page([])) })

    const nav = await screen.findByRole('navigation', { name: 'Main' })
    expect(await within(nav).findByRole('link', { name: 'My orders' })).toHaveAttribute('href', '/orders')
  })

  it('lists each order with when it was placed, its drinks, total, status and payment, linking to its page', async () => {
    renderOrderingAt('/orders', {
      [LIST]: () =>
        jsonResponse(
          page([
            summary(),
            summary({ reference: 'MS-AAAA-BBBB', status: 'Cancelled', isPaid: false, paymentStatus: 'Cancelled', refundStatus: 'NotPaid', total: 29.9, ...oneDrink }),
          ]),
        ),
    })

    const first = (await screen.findByRole('link', { name: 'Order MS-7KQ2-X9TD' })).closest('li')!
    expect(within(first).getByRole('link', { name: 'Order MS-7KQ2-X9TD' })).toHaveAttribute('href', '/orders/MS-7KQ2-X9TD')
    expect(first).toHaveTextContent(`Placed ${formatPlacedAt('2026-11-20T07:00:00+00:00', 'Africa/Johannesburg')}`)
    expect(first).toHaveTextContent('2 milkshakes · Branch A')
    const drinks = within(first).getByRole('list', { name: 'Milkshakes in order MS-7KQ2-X9TD' })
    expect(within(drinks).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'Chocolate · Thick · Oreo crumbs',
      'Vanilla · Thin · Bar One syrup',
    ])
    expect(first).toHaveTextContent(zar(52.9))
    expect(first).toHaveTextContent('Confirmed · Paid')

    const second = screen.getByRole('link', { name: 'Order MS-AAAA-BBBB' }).closest('li')!
    expect(second).toHaveTextContent('1 milkshake')
    expect(second).toHaveTextContent('Coffee · Thick · Vermicelli')
    expect(second).toHaveTextContent('Cancelled · Not paid')
    expect(screen.getByText('That is all your orders.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Load more orders' })).not.toBeInTheDocument()
  })

  it('shows the placed time in the business time zone, not the browser’s', () => {
    // 23:30 UTC on the 19th is already the 20th in Johannesburg.
    expect(formatPlacedAt('2026-11-19T23:30:00+00:00', 'Africa/Johannesburg', 'en-ZA')).toMatch(/20/)
    expect(formatPlacedAt('2026-11-19T23:30:00+00:00', 'Africa/Johannesburg', 'en-ZA')).toMatch(/01:30/)
    expect(formatPlacedAt('not a date', 'Africa/Johannesburg')).toBe('not a date')
  })

  it.each([
    [{ paymentStatus: 'Paid', refundStatus: null }, 'Paid'],
    [{ paymentStatus: 'Unpaid', refundStatus: null }, 'Not paid'],
    [{ paymentStatus: 'Cancelled', refundStatus: 'NotPaid' }, 'Not paid'],
    [{ paymentStatus: 'Cancelled', refundStatus: 'RefundInProgress' }, 'Refund in progress'],
    [{ paymentStatus: 'Cancelled', refundStatus: 'Refunded' }, 'Refunded'],
    [{ paymentStatus: 'Cancelled', refundStatus: 'RefundedLessFee' }, 'Refunded less fee'],
    [{ paymentStatus: 'Cancelled', refundStatus: 'RefundFailed' }, 'Refund failed'],
  ] as const)('describes payment %o as “%s”', (state, text) => {
    expect(paymentText(state)).toBe(text)
  })

  it('loads more orders with the cursor until there are no more', async () => {
    let calls = 0
    const fetchMock = renderOrderingAt('/orders', {
      [LIST]: () => {
        calls += 1
        return calls === 1
          ? jsonResponse(page([summary()], 'next+page/1'))
          : jsonResponse(page([summary({ reference: 'MS-OLDR-0001', ...oneDrink })]))
      },
    })

    await screen.findByRole('link', { name: 'Order MS-7KQ2-X9TD' })
    expect(screen.queryByText('That is all your orders.')).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Load more orders' }))

    expect(await screen.findByRole('link', { name: 'Order MS-OLDR-0001' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    expect(screen.getByText('2 orders, newest first.')).toBeInTheDocument()
    expect(screen.getByText('That is all your orders.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Load more orders' })).not.toBeInTheDocument()

    const urls = callsTo(fetchMock, 'GET', '/api/ordering/orders').map(([input]) => String(input))
    expect(urls).toEqual(['/api/ordering/orders', '/api/ordering/orders?cursor=next%2Bpage%2F1'])
  })

  it('keeps the loaded orders and offers to try again when loading more fails', async () => {
    let calls = 0
    renderOrderingAt('/orders', {
      [LIST]: () => {
        calls += 1
        if (calls === 1) {
          return jsonResponse(page([summary()], 'c1'))
        }
        return calls === 2 ? Promise.reject(new TypeError('Failed to fetch')) : jsonResponse(page([summary({ reference: 'MS-OLDR-0001' })]))
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Load more orders' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Load more orders' }))

    expect(await screen.findByRole('link', { name: 'Order MS-OLDR-0001' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('opens an order from the list', async () => {
    renderOrderingAt('/orders', {
      [LIST]: () => jsonResponse(page([summary()])),
      'GET /api/ordering/orders/MS-7KQ2-X9TD': () => jsonResponse({ status: 404, title: 'Order not found.' }, 404),
    })

    await userEvent.click(await screen.findByRole('link', { name: 'Order MS-7KQ2-X9TD' }))

    expect(await screen.findByRole('heading', { name: 'Order not found' })).toBeInTheDocument()
  })

  it('invites a patron without orders to start one', async () => {
    renderOrderingAt('/orders', { [LIST]: () => jsonResponse(page([])) })

    expect(await screen.findByText('You have not placed any orders yet.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Start an order' })).toHaveAttribute('href', '/order')
  })

  it('explains a failure and tries again', async () => {
    let attempts = 0
    const fetchMock = renderOrderingAt('/orders', {
      [LIST]: () => {
        attempts += 1
        return attempts === 1 ? Promise.reject(new TypeError('Failed to fetch')) : jsonResponse(page([summary()]))
      },
    })

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('link', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'GET', '/api/ordering/orders')).toHaveLength(2)
  })
})
