import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { bodyOf, callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import type { ManagerOrder, RefundNeedingAttention } from './types'

const REF = 'MS-7KQ2-X9TD'
const ATTENTION = 'GET /api/manager/refunds/attention'

function managerOrder(overrides: Partial<ManagerOrder> = {}): ManagerOrder {
  return {
    reference: REF,
    status: 'Confirmed',
    isPaid: true,
    patronFirstName: 'Thandi',
    patronEmail: 'thandi@example.test',
    restaurantName: 'Branch A',
    pickupAtUtc: '2026-11-20T10:00:00+00:00',
    pickupTimeZone: 'Africa/Johannesburg',
    total: 52.9,
    currencyCode: 'ZAR',
    placedAt: '2026-11-20T08:00:00+00:00',
    canRequestCancellation: true,
    requests: [],
    refunds: [],
    ...overrides,
  }
}

const failedRefund: RefundNeedingAttention = {
  orderReference: REF,
  refundReference: 'MSR-0123456789AB',
  kind: 'PatronCancellation',
  status: 'Failed',
  amount: 48.3,
  currencyCode: 'ZAR',
  providerMessage: 'Refund failed at Paystack.',
  createdAt: '2026-11-20T08:00:00+00:00',
}

beforeEach(() => clearCsrfToken())
afterEach(() => vi.unstubAllGlobals())

async function findOrder(reference = REF) {
  await userEvent.type(await screen.findByLabelText('Order reference'), reference)
  await userEvent.click(screen.getByRole('button', { name: 'Find order' }))
}

describe('Management → Orders', () => {
  it('is in the management navigation', async () => {
    renderManagerAt('/manager', { [ATTENTION]: () => jsonResponse([]) })

    const nav = await screen.findByRole('navigation', { name: 'Management sections' })
    expect(within(nav).getByRole('link', { name: 'Orders' })).toHaveAttribute('href', '/manager/orders')
  })

  it('finds an order by reference and shows the patron’s contact, status and payment', async () => {
    renderManagerAt('/manager/orders', {
      [ATTENTION]: () => jsonResponse([]),
      [`GET /api/manager/orders/${REF.toLowerCase()}`]: () => jsonResponse(managerOrder()),
    })

    await findOrder(REF.toLowerCase())

    const card = await screen.findByRole('region', { name: `Order ${REF}` })
    expect(card).toHaveTextContent('Confirmed')
    expect(card).toHaveTextContent('Paid')
    expect(within(card).getByRole('link', { name: 'thandi@example.test' })).toHaveAttribute('href', 'mailto:thandi@example.test')
  })

  it('says when no order has the reference', async () => {
    renderManagerAt('/manager/orders', {
      [ATTENTION]: () => jsonResponse([]),
      'GET /api/manager/orders/MS-0000-0000': () => jsonResponse({ status: 404, title: 'Order not found.' }, 404),
    })

    await findOrder('MS-0000-0000')

    expect(await screen.findByRole('alert')).toHaveTextContent('No order has the reference MS-0000-0000.')
  })

  it('asks the patron to cancel with a short reason, and then waits for their answer', async () => {
    const requested = managerOrder({
      canRequestCancellation: false,
      requests: [{ id: 7, reason: 'Out of milk', status: 'Pending', isOpen: true, refundAmount: 52.9, createdAt: '2026-11-20T08:00:00+00:00', closedAt: null }],
    })
    const fetchMock = renderManagerAt('/manager/orders', {
      [ATTENTION]: () => jsonResponse([]),
      [`GET /api/manager/orders/${REF}`]: () => jsonResponse(managerOrder()),
      [`POST /api/manager/orders/${REF}/cancellation-requests`]: () => jsonResponse(requested, 201),
    })
    await findOrder()

    await userEvent.click(await screen.findByRole('button', { name: 'Ask the patron to cancel' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Give a reason of 1 to 50 characters.')
    expect(callsTo(fetchMock, 'POST', `/api/manager/orders/${REF}/cancellation-requests`)).toHaveLength(0)

    const reason = screen.getByLabelText('Reason')
    expect(reason).toHaveAttribute('maxLength', '50')
    await userEvent.type(reason, '  Out of milk ')
    expect(screen.getByText('14/50 characters')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Ask the patron to cancel' }))

    expect(await screen.findByText(/Request sent\. The order changes only if the patron accepts/)).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, 'POST', `/api/manager/orders/${REF}/cancellation-requests`)[0])).toEqual({ reason: 'Out of milk' })
    expect(screen.getByText('A cancellation request is waiting for the patron’s answer.')).toBeInTheDocument()
    expect(screen.getByText(/Waiting for the patron/)).toBeInTheDocument()
  })

  it('explains why a request was refused', async () => {
    renderManagerAt('/manager/orders', {
      [ATTENTION]: () => jsonResponse([]),
      [`GET /api/manager/orders/${REF}`]: () => jsonResponse(managerOrder()),
      [`POST /api/manager/orders/${REF}/cancellation-requests`]: () =>
        jsonResponse({ status: 409, title: "A cancellation request is already waiting for the patron's answer." }, 409),
    })
    await findOrder()

    await userEvent.type(await screen.findByLabelText('Reason'), 'Out of milk')
    await userEvent.click(screen.getByRole('button', { name: 'Ask the patron to cancel' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('already waiting')
  })

  it('offers no request for orders that can no longer be cancelled', async () => {
    renderManagerAt('/manager/orders', {
      [ATTENTION]: () => jsonResponse([]),
      [`GET /api/manager/orders/${REF}`]: () => jsonResponse(managerOrder({ status: 'Cancelled', canRequestCancellation: false })),
    })
    await findOrder()

    expect(await screen.findByText(/can no longer be cancelled/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Ask the patron to cancel' })).not.toBeInTheDocument()
  })

  it('lists refunds needing attention and opens their order', async () => {
    const fetchMock = renderManagerAt('/manager/orders', {
      [ATTENTION]: () => jsonResponse([failedRefund]),
      [`GET /api/manager/orders/${REF}`]: () =>
        jsonResponse(managerOrder({ status: 'Cancelled', canRequestCancellation: false, refunds: [{ reference: 'MSR-0123456789AB', kind: 'PatronCancellation', status: 'Failed', amount: 48.3, fee: 4.6, needsAttention: false, createdAt: '2026-11-20T08:00:00+00:00', completedAt: '2026-11-20T09:00:00+00:00' }] })),
    })

    const list = await screen.findByRole('region', { name: 'Refunds needing attention' })
    await waitFor(() => expect(list).toHaveTextContent('Refund failed at Paystack.'))
    await userEvent.click(within(list).getByRole('button', { name: `Open order ${REF}` }))

    expect(await screen.findByRole('region', { name: `Order ${REF}` })).toHaveTextContent('Failed – refund by hand at Paystack')
    expect(callsTo(fetchMock, 'GET', `/api/manager/orders/${REF}`)).toHaveLength(1)
  })
})
