import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { formatMoney } from '../../lib/format/money'
import { jsonResponse, mockApi } from '../../test/mockApi'
import { placedOrder } from '../../test/orderingFixtures'
import { callsTo, renderOrderingAt } from '../../test/renderOrdering'
import { CancellationPanel } from './CancellationPanel'
import { CANCELLATION_MESSAGES } from './cancellation'
import type { OrderCancellation, PlacedOrder, RefundInfo } from './types'

const REF = 'MS-7KQ2-X9TD'
const ORDER = `GET /api/ordering/orders/${REF}`
const QUOTE = `GET /api/ordering/orders/${REF}/cancellation`
const CANCEL = `POST /api/ordering/orders/${REF}/cancel`
const ACCEPT = `POST /api/ordering/orders/${REF}/cancellation-request/accept`
const DECLINE = `POST /api/ordering/orders/${REF}/cancellation-request/decline`
// Intl uses a non-breaking space ("R 46,00"); Testing Library compares page text with ordinary spaces.
const zar = (amount: number) => formatMoney(amount, 'ZAR', 'en-ZA').replace(/\s/g, ' ')
const bodyOf = (init: RequestInit | undefined) => JSON.parse(String(init?.body)) as Record<string, unknown>

const paidOrder: PlacedOrder = { ...placedOrder(), status: 'Confirmed', isPaid: true, awaitsPayment: false, note: '' }
const cancelledOrder: PlacedOrder = { ...placedOrder(), status: 'Cancelled', isPaid: false, awaitsPayment: false, note: '' }

function quote(overrides: Partial<OrderCancellation> = {}): OrderCancellation {
  return {
    orderReference: REF,
    orderStatus: 'PendingPayment',
    canCancel: true,
    cannotCancelReason: null,
    isPaid: false,
    paidAmount: 0,
    feeRate: 0,
    fee: 0,
    refundAmount: 0,
    currencyCode: 'ZAR',
    openRequest: null,
    refunds: [],
    ...overrides,
  }
}

const paidQuote = (overrides: Partial<OrderCancellation> = {}) =>
  quote({ orderStatus: 'Confirmed', isPaid: true, paidAmount: 52.9, feeRate: 0.1, fee: 4.6, refundAmount: 48.3, ...overrides })

function refund(overrides: Partial<RefundInfo> = {}): RefundInfo {
  return {
    reference: 'MSR-0123456789AB',
    kind: 'PatronCancellation',
    status: 'Pending',
    amount: 48.3,
    fee: 4.6,
    needsAttention: false,
    createdAt: '2026-11-20T08:00:00+00:00',
    completedAt: null,
    ...overrides,
  }
}

/** Answers with each value in turn, then keeps answering with the last one. */
function sequence<T>(...values: T[]) {
  let index = 0
  return () => values[Math.min(index++, values.length - 1)] as T
}

beforeEach(() => {
  sessionStorage.clear()
  clearCsrfToken()
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('cancelling an order', () => {
  it('cancels an unpaid order for free after confirmation, and shows it cancelled', async () => {
    const order = sequence(placedOrder(), cancelledOrder)
    const cancelled = quote({ orderStatus: 'Cancelled', canCancel: false, cannotCancelReason: 'AlreadyCancelled' })
    let done = false
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(order()),
      [QUOTE]: () => jsonResponse(done ? cancelled : quote()),
      [CANCEL]: () => {
        done = true
        return jsonResponse(cancelled)
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel order' }))
    const dialog = screen.getByRole('alertdialog', { name: 'Cancel this order?' })
    expect(dialog).toHaveTextContent('You have not paid for this order, so nothing is charged.')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel order' }))

    expect(await screen.findByText(CANCELLATION_MESSAGES.cancelled)).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/cancel`)[0]?.[1])).toEqual({ expectedRefund: 0 })
    expect(await screen.findByText('Cancelled')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pay now' })).not.toBeInTheDocument()
  })

  it('shows the fee and the refund for a paid order before cancelling, and sends back only the refund it showed', async () => {
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(paidOrder),
      [QUOTE]: () => jsonResponse(paidQuote()),
      [CANCEL]: () => jsonResponse(paidQuote({ orderStatus: 'Cancelled', canCancel: false, cannotCancelReason: 'AlreadyCancelled', refunds: [refund()] })),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel order' }))
    const dialog = screen.getByRole('alertdialog')
    expect(dialog).toHaveTextContent(`You paid ${zar(52.9)}`)
    expect(dialog).toHaveTextContent(`A cancellation fee of ${zar(4.6)}`)
    expect(dialog).toHaveTextContent(`you will get back ${zar(48.3)}`)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel order' }))

    expect(await screen.findByText(new RegExp(`Refund of ${zar(48.3)} – on its way`))).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/cancel`)[0]?.[1])).toEqual({ expectedRefund: 48.3 })
  })

  it('shows changed figures and cancels nothing until the patron confirms them', async () => {
    let attempts = 0
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [QUOTE]: () => jsonResponse(quote()),
      [CANCEL]: () => {
        attempts += 1
        return attempts === 1
          ? jsonResponse({ status: 409, title: 'The refund for this order has changed.', cancellation: paidQuote() }, 409)
          : jsonResponse(paidQuote({ orderStatus: 'Cancelled', canCancel: false, cannotCancelReason: 'AlreadyCancelled' }))
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel order' }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel order' }))

    expect(await screen.findByText(CANCELLATION_MESSAGES.refundChanged)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Cancel order' }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent(`you will get back ${zar(48.3)}`)
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel order' }))

    expect(await screen.findByText(CANCELLATION_MESSAGES.cancelled)).toBeInTheDocument()
    const sent = callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/cancel`).map(([, init]) => bodyOf(init))
    expect(sent).toEqual([{ expectedRefund: 0 }, { expectedRefund: 48.3 }])
  })

  it('sends one request however often the patron confirms', async () => {
    let release: (() => void) | undefined
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [QUOTE]: () => jsonResponse(quote()),
      [CANCEL]: () => new Promise<Response>((resolve) => (release = () => resolve(jsonResponse(quote({ canCancel: false, cannotCancelReason: 'AlreadyCancelled' }))))),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel order' }))
    const confirm = within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel order' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)
    fireEvent.click(confirm)

    await waitFor(() => expect(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/cancel`)).toHaveLength(1))
    await act(async () => release?.())
    expect(await screen.findByText(CANCELLATION_MESSAGES.cancelled)).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/cancel`)).toHaveLength(1)
  })

  it('says when the outcome is unknown, and that trying again is safe', async () => {
    renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [QUOTE]: () => jsonResponse(quote()),
      [CANCEL]: () => Promise.reject(new TypeError('Failed to fetch')),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Cancel order' }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel order' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(CANCELLATION_MESSAGES.uncertain)
  })

  it('offers no cancellation once the pickup time has passed', async () => {
    renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(paidOrder),
      [QUOTE]: () => jsonResponse(paidQuote({ canCancel: false, cannotCancelReason: 'PickupPassed' })),
    })

    expect(await screen.findByText(CANCELLATION_MESSAGES.pickupPassed)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeInTheDocument()
  })
})

describe('refunds', () => {
  it.each([
    [refund({ status: 'Processed', completedAt: '2026-11-21T08:00:00+00:00' }), /– completed on/],
    [refund({ status: 'Failed' }), new RegExp(CANCELLATION_MESSAGES.contactRestaurant)],
    [refund({ status: 'Pending', needsAttention: true }), new RegExp(CANCELLATION_MESSAGES.contactRestaurant)],
    [refund({ kind: 'UnacceptedPayment', amount: 52.9, fee: 0 }), /Refund of a payment we could not accept/],
  ])('describes a refund as stored by the server (%#)', async (stored, expected) => {
    renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(cancelledOrder),
      [QUOTE]: () => jsonResponse(quote({ orderStatus: 'Cancelled', canCancel: false, cannotCancelReason: 'AlreadyCancelled', refunds: [stored] })),
    })

    expect(await screen.findByText(expected)).toBeInTheDocument()
    expect(screen.queryByText(/– completed/) !== null).toBe(stored.status === 'Processed')
  })
})

describe('a request from the restaurant', () => {
  const request = { id: 7, reason: 'Out of milk', status: 'Pending' as const, isOpen: true, refundAmount: 52.9, createdAt: '2026-11-20T08:00:00+00:00', closedAt: null }

  it('shows the reason and the full refund, and accepts it after confirmation', async () => {
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(paidOrder),
      [QUOTE]: () => jsonResponse(paidQuote({ openRequest: request })),
      [ACCEPT]: () => jsonResponse(paidQuote({ orderStatus: 'Cancelled', canCancel: false, cannotCancelReason: 'AlreadyCancelled', refunds: [refund({ kind: 'RestaurantCancellation', amount: 52.9, fee: 0 })] })),
    })

    const banner = await screen.findByRole('region', { name: 'The restaurant has asked to cancel this order' })
    expect(banner).toHaveTextContent('Out of milk')
    expect(banner).toHaveTextContent(`everything you paid (${zar(52.9)}), with no fee`)
    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeInTheDocument()

    await userEvent.click(within(banner).getByRole('button', { name: 'Accept cancellation' }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Accept cancellation' }))

    expect(await screen.findByText(CANCELLATION_MESSAGES.cancelled)).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/cancellation-request/accept`)[0]?.[1])).toEqual({ expectedRefund: 52.9 })
  })

  it('declines and keeps the order', async () => {
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [QUOTE]: () => jsonResponse(quote({ openRequest: { ...request, refundAmount: 0 } })),
      [DECLINE]: () => jsonResponse(quote()),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Keep my order' }))

    expect(await screen.findByText(CANCELLATION_MESSAGES.declined)).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/cancellation-request/decline`)).toHaveLength(1)
    expect(screen.queryByRole('region', { name: 'The restaurant has asked to cancel this order' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeInTheDocument()
  })

  it('is announced on the Home page with a link to the order', async () => {
    renderOrderingAt('/', {
      'GET /api/health': () => jsonResponse({ status: 'Healthy', checks: [] }),
      'GET /api/ordering/cancellation-requests': () =>
        jsonResponse([{ orderReference: REF, reason: 'Out of milk', createdAt: '2026-11-20T08:00:00+00:00', pickupAtUtc: '2026-11-20T10:00:00+00:00' }]),
    })

    const notice = await screen.findByRole('region', { name: 'The restaurant has asked to cancel an order' })
    expect(notice).toHaveTextContent('Out of milk')
    expect(within(notice).getByRole('link', { name: `Review order ${REF}` })).toHaveAttribute('href', `/orders/${REF}`)
  })
})

describe('after a payment is confirmed on the order page', () => {
  it('never shows the unpaid figures while it reloads for the paid order (M13 DEF-03)', async () => {
    const pending: { release?: () => void } = {}
    let paid = false
    mockApi({
      [QUOTE]: () =>
        paid
          ? new Promise<Response>((resolve) => {
              pending.release = () => resolve(jsonResponse(paidQuote()))
            })
          : jsonResponse(quote()),
    })
    const panel = (orderStatus: 'PendingPayment' | 'Confirmed') => (
      <CancellationPanel reference={REF} orderStatus={orderStatus} locale="en-ZA" onOrderChanged={() => undefined} />
    )
    const view = render(panel('PendingPayment'))
    expect(await screen.findByRole('button', { name: 'Cancel order' })).toBeInTheDocument()

    // The order is now paid; the panel's details for it have not arrived yet.
    paid = true
    view.rerender(panel('Confirmed'))
    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeInTheDocument()
    expect(screen.queryByText(/nothing is charged/)).not.toBeInTheDocument()

    await waitFor(() => expect(pending.release).toBeDefined())
    await act(async () => {
      pending.release?.()
    })
    await userEvent.click(await screen.findByRole('button', { name: 'Cancel order' }))
    expect(screen.getByRole('alertdialog')).toHaveTextContent(`You paid ${zar(52.9)}`)
  })
})
