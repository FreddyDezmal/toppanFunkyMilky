import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken, fileNameFrom } from '../../lib/api/httpClient'
import { toTabSeparated } from '../../lib/download'
import { callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import type { OrderReport, OrderTrends } from './types'

const ORDERS = 'GET /api/manager/reports/orders'
const ORDERS_EXPORT = 'GET /api/manager/reports/orders/export'

function csvResponse(fileName: string | null): Response {
  const headers = new Headers({ 'Content-Type': 'text/csv; charset=utf-8' })
  if (fileName) {
    headers.set('Content-Disposition', `attachment; filename=${fileName}; filename*=UTF-8''${encodeURIComponent(fileName)}`)
  }
  return { ok: true, status: 200, headers, blob: () => Promise.resolve(new Blob(['Date,Time\r\n'], { type: 'text/csv' })) } as unknown as Response
}

const report: OrderReport = {
  from: '2026-11-20',
  to: '2026-11-20',
  timeZone: 'Africa/Johannesburg',
  totalOrders: 2,
  totalDrinks: 2,
  page: 1,
  pageSize: 25,
  totalPages: 1,
  rows: [
    {
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
    },
    {
      reference: 'MS-CCCC-DDDD',
      date: '2026-11-20',
      time: '09:05',
      placedAtUtc: '2026-11-20T07:05:00+00:00',
      pickupLocal: '2026-11-20T10:00',
      restaurant: 'Branch A',
      drinkNumber: 1,
      flavour: 'Coffee\tbean',
      topping: 'None',
      consistency: 'Thin',
      paymentStatus: 'Cancelled',
      refundStatus: 'RefundInProgress',
    },
  ],
}

let saved: { href: string; download: string }[] = []

beforeEach(() => {
  clearCsrfToken()
  saved = []
  // jsdom has no object URLs; the download itself is checked through the link that would be clicked.
  URL.createObjectURL = vi.fn(() => 'blob:export')
  URL.revokeObjectURL = vi.fn()
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
    saved.push({ href: this.href, download: this.download })
  })
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

function exportQuery(fetchMock: ReturnType<typeof renderManagerAt>, path: string): URLSearchParams {
  return new URL(String(callsTo(fetchMock, 'GET', path).at(-1)?.[0]), 'http://localhost').searchParams
}

describe('Reports → Copy and Export', () => {
  it('exports every row of the current filters as CSV and saves it under the server’s name', async () => {
    const fetchMock = renderManagerAt('/manager/reports?from=2026-11-20&status=paid&q=oreo&page=2', {
      [ORDERS]: () => jsonResponse(report),
      [ORDERS_EXPORT]: () => csvResponse('milkshake-orders_2026-11-20.csv'),
    })

    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Export all matching orders as CSV' }))

    expect(await screen.findByText('Saved milkshake-orders_2026-11-20.csv.')).toBeInTheDocument()
    expect(saved).toEqual([{ href: 'blob:export', download: 'milkshake-orders_2026-11-20.csv' }])
    expect(Object.fromEntries(exportQuery(fetchMock, '/api/manager/reports/orders/export'))).toEqual({ from: '2026-11-20', status: 'paid', q: 'oreo' })
  })

  it('explains when an export would be too large, and saves nothing', async () => {
    renderManagerAt('/manager/reports?from=2026-11-20', {
      [ORDERS]: () => jsonResponse(report),
      [ORDERS_EXPORT]: () =>
        jsonResponse(
          {
            status: 422,
            title: 'Too many records to export.',
            detail: 'An export holds at most 10,000 records. Choose a shorter period or add filters, then export again.',
          },
          422,
        ),
    })

    await screen.findByRole('table')
    await userEvent.click(screen.getByRole('button', { name: 'Export all matching orders as CSV' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('An export holds at most 10,000 records.')
    expect(saved).toEqual([])
  })

  it('copies the rows on this page as tab-separated text for a spreadsheet', async () => {
    const writeText = vi.fn(() => Promise.resolve())
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    renderManagerAt('/manager/reports?from=2026-11-20', { [ORDERS]: () => jsonResponse(report) })

    await screen.findByRole('table')
    fireEvent.click(screen.getByRole('button', { name: 'Copy the rows on this page' }))

    expect(await screen.findByText('Copied 2 rows (this page).')).toBeInTheDocument()
    expect(writeText).toHaveBeenCalledWith(
      [
        'Date\tTime\tOrder\tMilkshake\tFlavour\tTopping\tConsistency\tPickup\tPayment status',
        '2026/11/20\t16:40\tMS-7KQ2-X9TD\t1\tChocolate\tOreo crumbs\tThick\t2026/11/20 17:30\tPaid',
        '2026/11/20\t09:05\tMS-CCCC-DDDD\t1\tCoffee bean\tNone\tThin\t2026/11/20 10:00\tCancelled (Refund in progress)',
      ].join('\n'),
    )
  })

  it('says when the browser does not allow copying', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('denied')) }, configurable: true })
    renderManagerAt('/manager/reports?from=2026-11-20', { [ORDERS]: () => jsonResponse(report) })

    await screen.findByRole('table')
    fireEvent.click(screen.getByRole('button', { name: 'Copy the rows on this page' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This browser did not allow copying. Use Export instead.')
  })

  it('exports the trends for the period shown, using a fallback name when the server gives none', async () => {
    const trends: OrderTrends = {
      from: null,
      to: null,
      timeZone: 'Africa/Johannesburg',
      totalOrders: 0,
      totalPaidOrders: 0,
      weekdays: [],
      months: [],
      years: [],
    }
    const fetchMock = renderManagerAt('/manager/reports?tab=trends', {
      'GET /api/manager/reports/trends': () => jsonResponse(trends),
      'GET /api/manager/reports/trends/export': () => csvResponse(null),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Export the trends as CSV' }))

    expect(await screen.findByText('Saved milkshake-trends.csv.')).toBeInTheDocument()
    expect([...exportQuery(fetchMock, '/api/manager/reports/trends/export').keys()]).toEqual([])
  })

  it('exports the audit history with the filters that are applied', async () => {
    const fetchMock = renderManagerAt('/manager/reports?tab=audit', {
      'GET /api/manager/audit/changes': () => jsonResponse({ items: [], nextCursor: null }),
      'GET /api/manager/reports/audit/changes/export': () => csvResponse('milkshake-audit-changes_all.csv'),
      'GET /api/manager/audit/security': () => jsonResponse({ items: [], nextCursor: null }),
      'GET /api/manager/reports/audit/security/export': () => csvResponse('milkshake-sign-in-events_all.csv'),
    })

    await screen.findByText('No changes match.')
    await userEvent.click(screen.getByRole('button', { name: 'Export these lookup changes as CSV' }))
    await waitFor(() => expect(saved.map((s) => s.download)).toEqual(['milkshake-audit-changes_all.csv']))
    expect(Object.fromEntries(exportQuery(fetchMock, '/api/manager/reports/audit/changes/export'))).toEqual({ entity: 'flavours' })

    await userEvent.click(screen.getByRole('button', { name: 'Sign-in events' }))
    await screen.findByText('No sign-in events match.')
    await userEvent.click(screen.getByRole('button', { name: 'Export these sign-in events as CSV' }))
    await waitFor(() => expect(saved.map((s) => s.download)).toContain('milkshake-sign-in-events_all.csv'))
  })
})

describe('download helpers', () => {
  it('reads the file name from Content-Disposition', () => {
    expect(fileNameFrom("attachment; filename=a.csv; filename*=UTF-8''cr%C3%A8me.csv")).toBe('crème.csv')
    expect(fileNameFrom('attachment; filename="report.csv"')).toBe('report.csv')
    expect(fileNameFrom(null)).toBeNull()
    expect(fileNameFrom('inline')).toBeNull()
  })

  it('writes tab-separated rows, flattening tabs and line breaks inside values', () => {
    expect(toTabSeparated([['a', 'b\tc'], ['d\r\ne', 'f']])).toBe('a\tb c\nd e\tf')
  })
})
