import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import { describeReportError, niceMax, pageNumbers, shortMonth, shortWeekday, slashed, todayIn } from './reports'
import type { OrderReport, OrderReportRow, OrderTrends } from './types'

const ORDERS = 'GET /api/manager/reports/orders'
const TRENDS = 'GET /api/manager/reports/trends'

function row(overrides: Partial<OrderReportRow> = {}): OrderReportRow {
  return {
    reference: 'MS-7KQ2-X9TD',
    date: '2026-11-20',
    time: '16:40',
    placedAtUtc: '2026-11-20T14:40:00+00:00',
    pickupLocal: '2026-11-20T17:30',
    restaurant: 'Branch A',
    drinkNumber: 1,
    flavour: 'Chocolate',
    topping: 'Oreo crumbs',
    consistency: 'Thick',
    paymentStatus: 'Paid',
    refundStatus: null,
    ...overrides,
  }
}

function report(rows: OrderReportRow[], overrides: Partial<OrderReport> = {}): OrderReport {
  return {
    from: '2026-11-20',
    to: '2026-11-20',
    timeZone: 'Africa/Johannesburg',
    totalOrders: rows.length,
    totalDrinks: rows.length,
    page: 1,
    pageSize: 25,
    totalPages: rows.length > 0 ? 1 : 0,
    rows,
    ...overrides,
  }
}

function trends(overrides: Partial<OrderTrends> = {}): OrderTrends {
  return {
    from: null,
    to: null,
    timeZone: 'Africa/Johannesburg',
    totalOrders: 9,
    totalPaidOrders: 5,
    weekdays: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'].map((label, index) => ({
      label,
      orders: index === 0 ? 3 : index === 4 ? 6 : 0,
      paidOrders: index === 0 ? 2 : index === 4 ? 3 : 0,
    })),
    months: [
      { label: '2026-10', orders: 4, paidOrders: 2 },
      { label: '2026-11', orders: 5, paidOrders: 3 },
    ],
    years: [{ label: '2026', orders: 9, paidOrders: 5 }],
    ...overrides,
  }
}

function lastQuery(fetchMock: ReturnType<typeof renderManagerAt>, path: string): URLSearchParams {
  const calls = callsTo(fetchMock, 'GET', path)
  return new URL(String(calls.at(-1)?.[0]), 'http://localhost').searchParams
}

beforeEach(() => clearCsrfToken())
afterEach(() => vi.unstubAllGlobals())

describe('Management → Reports', () => {
  it('is in the management navigation', async () => {
    renderManagerAt('/manager', {})

    const nav = await screen.findByRole('navigation', { name: 'Management sections' })
    expect(within(nav).getByRole('link', { name: 'Reports' })).toHaveAttribute('href', '/manager/reports')
  })

  it('shows today’s orders by default, one row per milkshake with the URS columns and payment status', async () => {
    const fetchMock = renderManagerAt('/manager/reports', {
      [ORDERS]: () =>
        jsonResponse(
          report(
            [
              row(),
              row({ reference: 'MS-AAAA-BBBB', drinkNumber: 2, flavour: 'Coffee', paymentStatus: 'Unpaid' }),
              row({ reference: 'MS-CCCC-DDDD', flavour: 'Oreo', paymentStatus: 'Cancelled', refundStatus: 'RefundedLessFee' }),
            ],
            { totalOrders: 3, totalDrinks: 3 },
          ),
        ),
    })

    const table = await screen.findByRole('table')
    expect(screen.getByRole('heading', { name: 'Reporting history', level: 1 })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Orders' })).toHaveAttribute('aria-selected', 'true')
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual([
      'Date',
      'Time',
      'Order',
      'Flavour',
      'Topping',
      'Consistency',
      'Pickup',
      'Payment status',
    ])
    const [first, second, third] = within(table).getAllByRole('row').slice(1)
    expect(within(first).getAllByRole('cell').map((cell) => cell.textContent)).toEqual([
      '2026/11/20',
      '16:40',
      'MS-7KQ2-X9TD',
      'Chocolate',
      'Oreo crumbs',
      'Thick',
      '2026/11/20 17:30',
      'Paid',
    ])
    expect(within(first).getByRole('link', { name: 'MS-7KQ2-X9TD' })).toHaveAttribute('href', '/manager/orders?reference=MS-7KQ2-X9TD')
    expect(second).toHaveTextContent('(milkshake 2)')
    expect(second).toHaveTextContent('Unpaid')
    expect(third).toHaveTextContent('CancelledRefunded less fee')
    expect(screen.getByText('3 orders · 3 milkshakes')).toBeInTheDocument()

    const query = lastQuery(fetchMock, '/api/manager/reports/orders')
    expect(query.get('from')).toBe(todayIn())
    expect([...query.keys()]).toEqual(['from'])
  })

  it('filters by payment status and search text, starting again at page 1', async () => {
    const fetchMock = renderManagerAt('/manager/reports?from=2026-11-20&page=3', { [ORDERS]: () => jsonResponse(report([row()])) })

    await screen.findByRole('table')
    expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('page')).toBe('3')

    await userEvent.click(screen.getByRole('button', { name: 'Cancelled' }))
    await waitFor(() => expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('status')).toBe('cancelled'))
    expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('page')).toBeNull()
    expect(screen.getByRole('button', { name: 'Cancelled' })).toHaveAttribute('aria-pressed', 'true')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), '  oreo ')
    await userEvent.click(screen.getByRole('button', { name: 'Search' }))
    await waitFor(() => expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('q')).toBe('oreo'))
    expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('status')).toBe('cancelled')
  })

  it('pages with numbered buttons, Previous and Next', async () => {
    let page = 1
    const fetchMock = renderManagerAt('/manager/reports?from=2026-11-20', {
      [ORDERS]: () => jsonResponse(report([row({ reference: `MS-PAGE-000${page}` })], { totalOrders: 60, totalDrinks: 250, totalPages: 10, page })),
    })

    await screen.findByRole('link', { name: 'MS-PAGE-0001' })
    const pages = screen.getByRole('navigation', { name: 'Pages' })
    expect(within(pages).getByRole('button', { name: 'Page 1' })).toHaveAttribute('aria-current', 'page')
    expect(within(pages).getByRole('button', { name: '← Previous' })).toBeDisabled()
    expect(within(pages).getAllByRole('button', { name: /^Page / }).map((b) => b.textContent)).toEqual(['1', '2', '3', '10'])

    page = 2
    await userEvent.click(within(pages).getByRole('button', { name: 'Next →' }))
    await screen.findByRole('link', { name: 'MS-PAGE-0002' })
    expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('page')).toBe('2')

    page = 9
    await userEvent.click(within(screen.getByRole('navigation', { name: 'Pages' })).getByRole('button', { name: 'Page 4' }))
    await waitFor(() => expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('page')).toBe('4'))
  })

  it('switches to a date range, and Clear goes back to today', async () => {
    const fetchMock = renderManagerAt('/manager/reports?from=2026-11-01&q=oreo', { [ORDERS]: () => jsonResponse(report([])) })

    await screen.findByText('No orders match.')
    await userEvent.click(screen.getByRole('button', { name: 'Date range' }))
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-11-15' } })

    await waitFor(() => expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('to')).toBe('2026-11-15'))
    expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('from')).toBe('2026-11-01')
    expect(screen.getByRole('heading', { name: /Orders for 2026\/11\/01 to 2026\/11\/15/ })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect([...lastQuery(fetchMock, '/api/manager/reports/orders').keys()]).toEqual(['from']))
    expect(lastQuery(fetchMock, '/api/manager/reports/orders').get('from')).toBe(todayIn())
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('')
    expect(screen.getByLabelText('Date')).toHaveValue(todayIn())
  })

  it('shows the server’s explanation when a filter is refused', async () => {
    renderManagerAt('/manager/reports?from=2026-11-20', {
      [ORDERS]: () =>
        jsonResponse(
          { status: 400, title: 'One or more validation errors occurred.', errors: { to: ['Choose a period of at most 366 days.'] } },
          400,
        ),
    })

    expect(await screen.findByRole('alert')).toHaveTextContent('Choose a period of at most 366 days.')
  })

  it('shows weekly, monthly and growth trends for all orders when no date is chosen, with the numbers as tables', async () => {
    const fetchMock = renderManagerAt('/manager/reports', {
      [ORDERS]: () => jsonResponse(report([])),
      [TRENDS]: () => jsonResponse(trends()),
    })

    await userEvent.click(await screen.findByRole('tab', { name: 'Trends' }))

    expect(await screen.findByRole('heading', { name: 'Weekly' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Monthly' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Growth' })).toBeInTheDocument()
    expect(screen.getByText('No date chosen: showing all orders.')).toBeInTheDocument()
    expect([...lastQuery(fetchMock, '/api/manager/reports/trends').keys()]).toEqual([])
    expect(screen.getByRole('img', { name: /Weekly: Orders by day of the week\. 9 orders, 5 paid/ })).toBeInTheDocument()

    // The tables sit in closed "Show the numbers" sections, so they are looked up including hidden content.
    const weekly = screen.getByRole('table', { name: 'Weekly: Orders by day of the week', hidden: true })
    expect(within(weekly).getAllByRole('row', { hidden: true }).slice(1, 3).map((r) => r.textContent)).toEqual([`${shortWeekday(0, 'en-ZA')}32`, `${shortWeekday(1, 'en-ZA')}00`])
    const monthly = screen.getByRole('table', { name: 'Monthly: Orders by month', hidden: true })
    expect(within(monthly).getAllByRole('row', { hidden: true })[2]).toHaveTextContent(`${shortMonth('2026-11', 'en-ZA')}53`)
  })

  it('shows trends for the chosen date range', async () => {
    const fetchMock = renderManagerAt('/manager/reports?tab=trends&from=2026-10-01&to=2026-11-30', {
      [TRENDS]: () => jsonResponse(trends({ from: '2026-10-01', to: '2026-11-30' })),
    })

    expect(await screen.findByRole('heading', { name: 'Trends for 2026/10/01 to 2026/11/30' })).toBeInTheDocument()
    const query = lastQuery(fetchMock, '/api/manager/reports/trends')
    expect([query.get('from'), query.get('to')]).toEqual(['2026-10-01', '2026-11-30'])
  })

  it('says when a period has no orders', async () => {
    renderManagerAt('/manager/reports?tab=trends&from=2026-11-20', {
      [TRENDS]: () =>
        jsonResponse(
          trends({
            from: '2026-11-20',
            to: '2026-11-20',
            totalOrders: 0,
            totalPaidOrders: 0,
            weekdays: [],
            months: [],
            years: [],
          }),
        ),
    })

    expect(await screen.findAllByText('No orders in this period.')).toHaveLength(3)
  })
})

describe('report helpers', () => {
  it('shows a refused filter’s own messages', async () => {
    const { ApiError } = await import('../../lib/api/httpClient')
    const refused = new ApiError('One or more validation errors occurred.', 400, {
      status: 400,
      title: 'One or more validation errors occurred.',
      errors: { from: ['Use a date such as 2026-11-20 (year 2000 to 2099).'], q: ['Use at most 100 characters.'] },
    })
    expect(describeReportError(refused)).toBe('Use a date such as 2026-11-20 (year 2000 to 2099). Use at most 100 characters.')
    expect(describeReportError(new TypeError('Failed to fetch'))).toBe('Something went wrong. Please try again.')
  })

  it('formats dates as on the URS screen', () => {
    expect(slashed('2026-11-20')).toBe('2026/11/20')
    expect(todayIn('Not/AZone')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it('offers the first, last and nearby pages with gaps', () => {
    expect(pageNumbers(1, 1)).toEqual([1])
    expect(pageNumbers(1, 10)).toEqual([1, 2, 3, null, 10])
    expect(pageNumbers(6, 10)).toEqual([1, null, 4, 5, 6, 7, 8, null, 10])
    expect(pageNumbers(10, 10)).toEqual([1, null, 8, 9, 10])
  })

  it('rounds the chart axis up to 1, 2 or 5 × 10ⁿ', () => {
    expect([0, 1, 3, 7, 12, 99, 100, 101].map(niceMax)).toEqual([1, 1, 5, 10, 20, 100, 100, 200])
  })
})
