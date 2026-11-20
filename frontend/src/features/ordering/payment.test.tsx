import { act, fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { jsonResponse } from '../../test/mockApi'
import { placedOrder } from '../../test/orderingFixtures'
import { callsTo, renderOrderingAt } from '../../test/renderOrdering'
import { checkoutNavigation, isTrustedCheckoutUrl, PAYMENT_MESSAGES, PAYMENT_POLLING, paymentOutcome } from './payment'
import type { PaymentStatus, PlacedOrder } from './types'

const REF = 'MS-7KQ2-X9TD'
const ORDER = `GET /api/ordering/orders/${REF}`
const START = `POST /api/ordering/orders/${REF}/payments`
const REFRESH = `POST /api/ordering/orders/${REF}/payments/refresh`
const CHECKOUT = 'https://checkout.paystack.com/abc123'
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const keyOf = (init: RequestInit | undefined) => new Headers(init?.headers).get('Idempotency-Key')

const confirmed: PlacedOrder = { ...placedOrder(), status: 'Confirmed', isPaid: true, awaitsPayment: false, note: '' }
const failedOrder: PlacedOrder = { ...placedOrder(), status: 'PaymentFailed', note: '' }

function status(overrides: Partial<PaymentStatus>): PaymentStatus {
  return {
    orderStatus: 'PendingPayment',
    isPaid: false,
    awaitsPayment: true,
    checking: true,
    payment: { reference: 'MSP-ABCDEFGHJKMN', status: 'Pending', message: null },
    ...overrides,
  }
}

/** Answers with each value in turn, then keeps answering with the last one. */
function sequence<T>(...values: T[]) {
  let index = 0
  return () => values[Math.min(index++, values.length - 1)] as T
}

let navigate: ReturnType<typeof vi.spyOn>
const originalInterval = PAYMENT_POLLING.intervalMs

beforeEach(() => {
  sessionStorage.clear()
  clearCsrfToken()
  PAYMENT_POLLING.intervalMs = 1
  navigate = vi.spyOn(checkoutNavigation, 'go').mockImplementation(() => undefined)
})

afterEach(() => {
  PAYMENT_POLLING.intervalMs = originalInterval
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
})

describe('paying for an order', () => {
  it('asks the server for a checkout with a fresh key and no amount, then goes only to Paystack', async () => {
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [START]: () => jsonResponse({ paymentReference: 'MSP-ABCDEFGHJKMN', checkoutUrl: CHECKOUT }),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Pay now' }))

    await waitFor(() => expect(navigate).toHaveBeenCalledWith(CHECKOUT))
    const [call] = callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/payments`)
    expect(JSON.parse(String(call?.[1]?.body))).toEqual({})
    expect(keyOf(call?.[1])).toMatch(UUID)
    expect(screen.getByRole('status')).toHaveTextContent('Taking you to Paystack')
    expect(screen.queryByRole('button', { name: 'Pay now' })).not.toBeInTheDocument()
    // Leaving for checkout is not payment: the order still says so.
    expect(screen.getByText('Not paid')).toBeInTheDocument()
  })

  it('sends one request however often the patron clicks', async () => {
    let release: (() => void) | undefined
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [START]: () =>
        new Promise<Response>((resolve) => (release = () => resolve(jsonResponse({ paymentReference: 'MSP-ABCDEFGHJKMN', checkoutUrl: CHECKOUT })))),
    })

    const pay = await screen.findByRole('button', { name: 'Pay now' })
    fireEvent.click(pay)
    fireEvent.click(pay)
    fireEvent.click(pay)

    expect(await screen.findByRole('button', { name: 'Opening secure checkout…' })).toBeDisabled()
    await waitFor(() => expect(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/payments`)).toHaveLength(1))
    await act(async () => release?.())
    await waitFor(() => expect(navigate).toHaveBeenCalledTimes(1))
    expect(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/payments`)).toHaveLength(1)
  })

  it('never sends the browser to an address that is not Paystack’s checkout', async () => {
    renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [START]: () => jsonResponse({ paymentReference: 'MSP-ABCDEFGHJKMN', checkoutUrl: 'https://checkout.paystack.com.example.net/abc' }),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Pay now' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(PAYMENT_MESSAGES.untrusted)
    expect(navigate).not.toHaveBeenCalled()
  })

  it('keeps the same key after a network failure, so trying again cannot open a second payment', async () => {
    let attempts = 0
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [START]: () => {
        attempts += 1
        return attempts === 1
          ? Promise.reject(new TypeError('Failed to fetch'))
          : jsonResponse({ paymentReference: 'MSP-ABCDEFGHJKMN', checkoutUrl: CHECKOUT })
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Pay now' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(PAYMENT_MESSAGES.uncertain)
    await userEvent.click(screen.getByRole('button', { name: 'Pay now' }))

    await waitFor(() => expect(navigate).toHaveBeenCalledWith(CHECKOUT))
    const keys = callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/payments`).map(([, init]) => keyOf(init))
    expect(keys).toHaveLength(2)
    expect(keys[1]).toBe(keys[0])
  })

  it('shows the server’s reason when payments are unavailable, and uses a new key next time', async () => {
    let attempts = 0
    const fetchMock = renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [START]: () => {
        attempts += 1
        return attempts === 1
          ? jsonResponse({ status: 503, title: 'Payments are temporarily unavailable.', detail: 'You have not been charged. Please try again in a few minutes.' }, 503)
          : jsonResponse({ paymentReference: 'MSP-ABCDEFGHJKMN', checkoutUrl: CHECKOUT })
      },
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Pay now' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Payments are temporarily unavailable. You have not been charged.')
    await userEvent.click(screen.getByRole('button', { name: 'Pay now' }))

    await waitFor(() => expect(navigate).toHaveBeenCalled())
    const keys = callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/payments`).map(([, init]) => keyOf(init))
    expect(keys[1]).not.toBe(keys[0])
  })

  it('shows the order as stored when the server says it has already been paid', async () => {
    const order = sequence(placedOrder(), confirmed)
    renderOrderingAt(`/orders/${REF}`, {
      [ORDER]: () => jsonResponse(order()),
      [START]: () => jsonResponse({ status: 409, title: 'This order has already been paid.' }, 409),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Pay now' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This order has already been paid.')
    expect(await screen.findByText('Paid')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pay now' })).not.toBeInTheDocument()
    expect(navigate).not.toHaveBeenCalled()
  })

  it('offers no payment for an order that is already paid', async () => {
    renderOrderingAt(`/orders/${REF}`, { [ORDER]: () => jsonResponse(confirmed) })

    expect(await screen.findByRole('heading', { name: `Order ${REF}` })).toBeInTheDocument()
    expect(screen.getByText('Confirmed')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pay now' })).not.toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Payment' })).not.toBeInTheDocument()
  })
})

describe('returning from checkout', () => {
  const returnPath = `/orders/${REF}?payment=return`

  it('says it is checking, and shows the order paid only once the server has verified it', async () => {
    const order = sequence(placedOrder(), confirmed)
    let release: (() => void) | undefined
    const answers = sequence<() => Response | Promise<Response>>(
      () => jsonResponse(status({})),
      () => new Promise<Response>((resolve) => (release = () => resolve(jsonResponse(status({ orderStatus: 'Confirmed', isPaid: true, awaitsPayment: false, checking: false, payment: { reference: 'MSP-ABCDEFGHJKMN', status: 'Succeeded', message: null } })))))
    )
    renderOrderingAt(returnPath, {
      [ORDER]: () => jsonResponse(order()),
      [REFRESH]: () => answers()(),
    })

    expect(await screen.findByText(PAYMENT_MESSAGES.checking)).toBeInTheDocument()
    expect(screen.getByText('Not paid')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pay now' })).not.toBeInTheDocument()

    await waitFor(() => expect(release).toBeDefined())
    await act(async () => release?.())

    expect(await screen.findByText(PAYMENT_MESSAGES.paid)).toBeInTheDocument()
    expect(await screen.findByText('Paid')).toBeInTheDocument()
    expect(screen.getByText('Confirmed')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Pay now' })).not.toBeInTheDocument()
  })

  it('explains a declined payment and lets the patron try again', async () => {
    const order = sequence(placedOrder(), failedOrder)
    renderOrderingAt(returnPath, {
      [ORDER]: () => jsonResponse(order()),
      [REFRESH]: () =>
        jsonResponse(status({ orderStatus: 'PaymentFailed', checking: false, payment: { reference: 'MSP-ABCDEFGHJKMN', status: 'Failed', message: 'Declined' } })),
    })

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(PAYMENT_MESSAGES.failed)
    expect(alert).toHaveTextContent('Paystack said: Declined')
    expect(await screen.findByText('Payment failed')).toBeInTheDocument()
    expect(screen.getByText('Not paid')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pay now' })).toBeEnabled()
  })

  it('says when no payment was made (cancelled), and keeps the order payable', async () => {
    renderOrderingAt(returnPath, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [REFRESH]: () =>
        jsonResponse(status({ checking: false, payment: { reference: 'MSP-ABCDEFGHJKMN', status: 'Abandoned', message: 'Checkout expired.' } })),
    })

    expect(await screen.findByText(PAYMENT_MESSAGES.cancelled)).toBeInTheDocument()
    expect(screen.getByText('Not paid')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pay now' })).toBeEnabled()
  })

  it('stops asking after a while, never claims payment, and lets the patron check again', async () => {
    const fetchMock = renderOrderingAt(returnPath, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [REFRESH]: () => jsonResponse(status({})),
    })

    expect(await screen.findByText(PAYMENT_MESSAGES.stillChecking)).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/payments/refresh`)).toHaveLength(PAYMENT_POLLING.attempts)
    expect(screen.getByText('Not paid')).toBeInTheDocument()
    expect(screen.queryByText(PAYMENT_MESSAGES.paid)).not.toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Check again' }))
    await waitFor(() =>
      expect(callsTo(fetchMock, 'POST', `/api/ordering/orders/${REF}/payments/refresh`)).toHaveLength(PAYMENT_POLLING.attempts * 2),
    )
    expect(await screen.findByText(PAYMENT_MESSAGES.stillChecking)).toBeInTheDocument()
  })

  it('says when the payment cannot be checked right now', async () => {
    renderOrderingAt(returnPath, {
      [ORDER]: () => jsonResponse(placedOrder()),
      [REFRESH]: () => Promise.reject(new TypeError('Failed to fetch')),
    })

    expect(await screen.findByRole('alert')).toHaveTextContent(PAYMENT_MESSAGES.checkFailed)
    expect(screen.getByText('Not paid')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Check again' })).toBeInTheDocument()
  })
})

describe('checkout address', () => {
  it.each([
    ['https://checkout.paystack.com/abc123', true],
    ['http://checkout.paystack.com/abc123', false],
    ['https://checkout.paystack.com.example.net/abc', false],
    ['https://evil.example/checkout.paystack.com', false],
    ['https://checkout.paystack.com:8443/abc', false],
    ['https://user:pass@checkout.paystack.com/abc', false],
    ['javascript:alert(1)', false],
    ['not a url', false],
  ])('%s → %s', (url, trusted) => {
    expect(isTrustedCheckoutUrl(url)).toBe(trusted)
  })
})

describe('payment outcome', () => {
  it('is paid only when the server says so', () => {
    expect(paymentOutcome(status({ isPaid: true, checking: false }))).toBe('paid')
    expect(paymentOutcome(status({ checking: false, payment: { reference: 'x', status: 'Succeeded', message: null } }))).toBe('needsRefund')
    expect(paymentOutcome(status({ checking: false, payment: { reference: 'x', status: 'RequiresRefund', message: null } }))).toBe('needsRefund')
    expect(paymentOutcome(status({ checking: false, payment: null }))).toBe('none')
  })
})
