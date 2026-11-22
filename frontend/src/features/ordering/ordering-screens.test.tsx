import { act, fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { formatMoney, formatPercent } from '../../lib/format/money'
import { jsonResponse } from '../../test/mockApi'
import { draftSummary, patronUser, placedOrder, quoteFor } from '../../test/orderingFixtures'
import { callsTo, renderOrderingAt } from '../../test/renderOrdering'
import type { CurrentUser } from '../auth/types'
import { draftStorageKey, saveDraft } from './draftStorage'
import { PLACEMENT_MESSAGES } from './placement'
import { RECONCILE_MESSAGES } from './reconcileDraft'
import type { DrinkSelection, OrderDraft } from './types'

const manager: CurrentUser = { ...patronUser, id: 'manager-1', firstName: 'Mandla', roles: ['Manager'] }
const vanillaThickOreo = { flavourId: 3, consistencyId: 29, toppingId: 19 }
const empty = { flavourId: null, consistencyId: null, toppingId: null }
// Intl uses a non-breaking space ("R 46,00"); Testing Library compares page text with ordinary spaces.
const zar = (amount: number) => formatMoney(amount, 'ZAR', 'en-ZA').replace(/\s/g, ' ')

function storeDraft(draft: Partial<OrderDraft>) {
  saveDraft(patronUser.id, { restaurantId: null, pickupLocal: null, drinks: [], ...draft })
}

beforeEach(() => {
  sessionStorage.clear()
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('starting an order', () => {
  it('offers patrons an Order link and a Build Your Milkshake button', async () => {
    renderOrderingAt('/')

    await userEvent.click(await screen.findByRole('link', { name: 'Build Your Milkshake' }))

    expect(await screen.findByRole('heading', { name: 'How many milkshakes?' })).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toHaveTextContent('Order')
  })

  it('keeps managers out of ordering (patron-only, open question Q-19)', async () => {
    renderOrderingAt('/order', {}, manager)

    expect(await screen.findByRole('heading', { name: 'Access denied' })).toBeInTheDocument()
  })

  it('explains when ordering is unavailable', async () => {
    renderOrderingAt('/order', {
      'GET /api/ordering/options': () =>
        jsonResponse({ status: 503, title: 'Ordering is temporarily unavailable.', detail: 'Please try again later.' }, 503),
    })

    expect(await screen.findByRole('alert')).toHaveTextContent('Ordering is temporarily unavailable. Please try again later.')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})

describe('quantity', () => {
  it('offers the configured range and requires a choice', async () => {
    renderOrderingAt('/order')
    const select = await screen.findByLabelText('Number of milkshakes')

    expect(within(select).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'Choose a number',
      ...Array.from({ length: 10 }, (_, index) => String(index + 1)),
    ])
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }))

    expect(screen.getByText('Choose how many milkshakes you would like.')).toBeInTheDocument()
    expect(select).toHaveFocus()
    expect(select).toHaveAttribute('aria-invalid', 'true')
  })

  it('creates one card per milkshake', async () => {
    renderOrderingAt('/order')

    await userEvent.selectOptions(await screen.findByLabelText('Number of milkshakes'), '2')
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }))

    expect(await screen.findByRole('heading', { name: 'Milkshake 1' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Milkshake 2' })).toBeInTheDocument()
  })

  it('asks before dropping milkshakes that already have choices', async () => {
    storeDraft({ drinks: [vanillaThickOreo, vanillaThickOreo, vanillaThickOreo] })
    renderOrderingAt('/order')

    await userEvent.selectOptions(await screen.findByLabelText('Number of milkshakes'), '1')
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }))
    const dialog = screen.getByRole('alertdialog', { name: 'Remove milkshakes?' })
    expect(dialog).toHaveTextContent('Milkshakes 2 to 3 and their choices will be removed from your order.')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Keep them' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'How many milkshakes?' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Continue' }))
    await userEvent.click(screen.getByRole('button', { name: 'Remove' }))
    expect(await screen.findByRole('heading', { name: 'Milkshake 1' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Milkshake 2' })).not.toBeInTheDocument()
  })
})

describe('milkshakes', () => {
  it('lists the options with their current fees and checks each milkshake is complete', async () => {
    storeDraft({ drinks: [empty] })
    renderOrderingAt('/order/drinks')

    const flavour = await screen.findByLabelText('Flavour')
    expect(within(flavour).getByRole('option', { name: /^Vanilla \(R/ })).toBeInTheDocument()
    expect(screen.getByText(/Prices are estimates based on current fees and VAT/)).toBeInTheDocument()

    await userEvent.selectOptions(flavour, '3')
    await userEvent.selectOptions(screen.getByLabelText('Consistency'), '29')
    await userEvent.click(screen.getByRole('button', { name: 'Done with milkshake 1' }))

    expect(screen.getByText('Choose a topping for milkshake 1.')).toBeInTheDocument()
    expect(screen.getByLabelText('Topping')).toHaveFocus()

    await userEvent.selectOptions(screen.getByLabelText('Topping'), '19')
    await userEvent.click(screen.getByRole('button', { name: 'Done with milkshake 1' }))

    expect(screen.queryByLabelText('Flavour')).not.toBeInTheDocument()
    expect(screen.getByText(/^Vanilla ·/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Edit milkshake 1' })).toHaveFocus()
  })

  it('reports every missing choice before moving on, then continues to pickup', async () => {
    storeDraft({ drinks: [vanillaThickOreo, empty] })
    renderOrderingAt('/order/drinks')

    await userEvent.click(await screen.findByRole('button', { name: 'Continue to pickup' }))

    expect(screen.getByRole('alert')).toHaveTextContent('3 choices are missing.')
    expect(screen.getByText('Choose a flavour for milkshake 2.')).toBeInTheDocument()

    const second = screen.getByRole('listitem', { name: 'Milkshake 2' })
    await userEvent.selectOptions(within(second).getByLabelText('Flavour'), '4')
    await userEvent.selectOptions(within(second).getByLabelText('Consistency'), '30')
    await userEvent.selectOptions(within(second).getByLabelText('Topping'), '20')
    await userEvent.click(screen.getByRole('button', { name: 'Continue to pickup' }))

    expect(await screen.findByRole('heading', { name: 'Where and when?' })).toBeInTheDocument()
  })

  it('removes a milkshake after confirmation, but never below the minimum', async () => {
    storeDraft({ drinks: [vanillaThickOreo, { ...vanillaThickOreo, flavourId: 4 }] })
    renderOrderingAt('/order/drinks')

    await userEvent.click(await screen.findByRole('button', { name: 'Remove milkshake 1' }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Remove' }))

    expect(screen.queryByRole('heading', { name: 'Milkshake 2' })).not.toBeInTheDocument()
    expect(screen.getByText(/^Chocolate ·/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Remove milkshake 1' })).not.toBeInTheDocument()
  })

  it('clears a stored choice that is no longer offered and says so', async () => {
    storeDraft({ drinks: [{ ...vanillaThickOreo, flavourId: 999 }] })
    renderOrderingAt('/order/drinks')

    expect(await screen.findByText(RECONCILE_MESSAGES.choice('flavour', 1))).toBeInTheDocument()
    expect(screen.getByLabelText('Flavour')).toHaveValue('')
  })
})

describe('pickup', () => {
  it('shows the business time zone and window, and keeps times inside it', async () => {
    storeDraft({ drinks: [vanillaThickOreo] })
    renderOrderingAt('/order/pickup')

    const time = await screen.findByLabelText('Pickup date and time')
    expect(time).toHaveAttribute('min', '2026-11-19T12:15')
    expect(time).toHaveAttribute('max', '2026-11-26T23:59')
    expect(screen.getByText(/Times are in .*Africa\/Johannesburg/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Review order' }))
    expect(screen.getByText('Choose a pickup restaurant.')).toBeInTheDocument()
    expect(screen.getByLabelText('Pickup restaurant')).toHaveFocus()

    await userEvent.selectOptions(screen.getByLabelText('Pickup restaurant'), '35')
    fireEvent.change(time, { target: { value: '2026-11-27T09:00' } })
    await userEvent.click(screen.getByRole('button', { name: 'Review order' }))
    expect(screen.getByText(/^Choose a time between/)).toBeInTheDocument()
    expect(time).toHaveFocus()

    fireEvent.change(time, { target: { value: '2026-11-20T10:00' } })
    await userEvent.click(screen.getByRole('button', { name: 'Review order' }))
    expect(await screen.findByRole('heading', { name: 'Review your order' })).toBeInTheDocument()
  })
})

describe('review', () => {
  const ready: Partial<OrderDraft> = { restaurantId: 35, pickupLocal: '2026-11-20T10:00', drinks: [vanillaThickOreo] }
  const VALIDATE = 'POST /api/ordering/draft/validate'

  it('shows the server-checked draft with per-drink prices and estimated totals, and offers to place it', async () => {
    storeDraft(ready)
    const fetchMock = renderOrderingAt('/order/review', { [VALIDATE]: () => jsonResponse(draftSummary()) })

    expect(await screen.findByText(/^Draft – not yet ordered or paid/)).toBeInTheDocument()
    expect(screen.getByText('Branch A')).toBeInTheDocument()
    expect(screen.getByText(/^Vanilla ·/)).toBeInTheDocument()
    expect(within(screen.getByRole('heading', { name: /^Milkshake 1/ })).getByText(zar(46))).toBeInTheDocument()
    const price = screen.getByRole('region', { name: 'Estimated price' })
    expect(within(price).getByText(zar(46))).toBeInTheDocument()
    expect(within(price).getByText(`${zar(0)} (none)`)).toBeInTheDocument()
    expect(within(price).getByText(`VAT (${formatPercent(0.15, 'en-ZA')})`)).toBeInTheDocument()
    expect(within(price).getByText(zar(6.9))).toBeInTheDocument()
    expect(within(price).getByText(zar(52.9))).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Place order' })).toBeEnabled()
    expect(screen.getByText(/You will not be charged now/)).toBeInTheDocument()

    const [call] = callsTo(fetchMock, 'POST', '/api/ordering/draft/validate')
    expect(JSON.parse(String(call?.[1]?.body))).toEqual({
      restaurantId: 35,
      pickupLocal: '2026-11-20T10:00',
      drinks: [vanillaThickOreo],
    })
  })

  it('lists what the server rejected, links to the step that fixes it and reloads the options', async () => {
    storeDraft(ready)
    const fetchMock = renderOrderingAt('/order/review', {
      [VALIDATE]: () =>
        jsonResponse(
          {
            status: 400,
            title: 'One or more validation errors occurred.',
            errors: {
              pickupLocal: ['That pickup time has already passed. Please choose a later time.'],
              'drinks[0].toppingId': ['The topping chosen for milkshake 1 is no longer available. Please choose another.'],
            },
          },
          400,
        ),
    })

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('That pickup time has already passed.')
    expect(within(alert).getByRole('link', { name: 'Change pickup' })).toHaveAttribute('href', '/order/pickup')
    expect(within(alert).getByRole('link', { name: 'Change milkshakes' })).toHaveAttribute('href', '/order/drinks')
    await vi.waitFor(() => expect(callsTo(fetchMock, 'GET', '/api/ordering/options').length).toBeGreaterThanOrEqual(2))
  })

  it('starts over after confirmation', async () => {
    storeDraft(ready)
    renderOrderingAt('/order/review', { [VALIDATE]: () => jsonResponse(draftSummary()) })

    await userEvent.click(await screen.findByRole('button', { name: 'Start over' }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Start over' }))

    expect(await screen.findByRole('heading', { name: 'How many milkshakes?' })).toBeInTheDocument()
    expect(screen.getByLabelText('Number of milkshakes')).toHaveValue('')
  })
})

describe('live prices', () => {
  const QUOTE = 'POST /api/ordering/quote'
  const bodyOf = (init: RequestInit | undefined) => (JSON.parse(String(init?.body)) as { drinks: DrinkSelection[] }).drinks

  it('shows each milkshake’s price and the estimated total from the server', async () => {
    // 30 + 6 + 10 = 46.00 and 32 + 0 + 8 = 40.00; subtotal 86.00; VAT 12.90; total 98.90.
    storeDraft({ drinks: [vanillaThickOreo, { flavourId: 4, consistencyId: 30, toppingId: 20 }] })
    const fetchMock = renderOrderingAt('/order/drinks')

    expect(await screen.findByText(`Estimated total ${zar(98.9)}.`)).toBeInTheDocument()
    expect(within(screen.getByRole('listitem', { name: 'Milkshake 2' })).getByText(zar(40))).toBeInTheDocument()
    expect(within(screen.getByRole('region', { name: 'Estimated price' })).getByText(zar(12.9))).toBeInTheDocument()
    expect(bodyOf(callsTo(fetchMock, 'POST', '/api/ordering/quote')[0]?.[1])).toEqual([
      vanillaThickOreo,
      { flavourId: 4, consistencyId: 30, toppingId: 20 },
    ])
  })

  it('keeps the price table\u2019s space while the estimate loads, so the Continue button does not move (DEF-04)', async () => {
    storeDraft({ drinks: [vanillaThickOreo] })
    const pending: { release?: () => void } = {}
    renderOrderingAt('/order/drinks', {
      [QUOTE]: (init) =>
        new Promise<Response>((resolve) => {
          pending.release = () => resolve(jsonResponse(quoteFor(bodyOf(init))))
        }),
    })

    // Before the estimate: the same four rows, without amounts, hidden from assistive technology.
    const region = await screen.findByRole('region', { name: 'Estimated price' })
    const placeholder = await within(region).findByTestId('price-summary-placeholder')
    expect(placeholder).toHaveAttribute('aria-hidden', 'true')
    expect(placeholder.querySelectorAll('dt')).toHaveLength(4)
    await waitFor(() => expect(within(region).getByRole('status')).toHaveTextContent('Updating prices…'))

    // After it: the same four rows with the server's amounts, and the placeholder gone.
    await waitFor(() => expect(pending.release).toBeDefined())
    await act(async () => {
      pending.release?.()
    })
    expect(await within(region).findByText(`Estimated total ${zar(52.9)}.`)).toBeInTheDocument()
    expect(within(region).queryByTestId('price-summary-placeholder')).not.toBeInTheDocument()
    expect(region.querySelectorAll('dt')).toHaveLength(4)
  })

  it('asks the server again after a change and shows the new price', async () => {
    storeDraft({ drinks: [vanillaThickOreo] })
    renderOrderingAt('/order/drinks')
    expect(await screen.findByText(`Estimated total ${zar(52.9)}.`)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Edit milkshake 1' }))
    await userEvent.selectOptions(screen.getByLabelText('Flavour'), '4')

    // Chocolate 32 + 6 + 10 = 48.00; VAT 7.20; total 55.20.
    expect(await screen.findByText(`Estimated total ${zar(55.2)}.`)).toBeInTheDocument()
  })

  it('never lets a late answer to an older request overwrite a newer price', async () => {
    let releaseFirst: (() => void) | undefined
    let calls = 0
    storeDraft({ drinks: [vanillaThickOreo] })
    renderOrderingAt('/order/drinks', {
      [QUOTE]: (init) => {
        calls += 1
        const quote = quoteFor(bodyOf(init))
        if (calls > 1) {
          return jsonResponse(quote)
        }
        // The first answer is slow and (to make it obvious) carries a different total.
        return new Promise<Response>((resolve) => {
          releaseFirst = () => resolve(jsonResponse({ ...quote, totals: { ...quote.totals!, total: 999 } }))
        })
      },
    })
    await waitFor(() => expect(calls).toBe(1))

    await userEvent.click(await screen.findByRole('button', { name: 'Edit milkshake 1' }))
    await userEvent.selectOptions(screen.getByLabelText('Flavour'), '4')
    expect(await screen.findByText(`Estimated total ${zar(55.2)}.`)).toBeInTheDocument()

    await act(async () => {
      releaseFirst?.()
      await new Promise((resolve) => setTimeout(resolve, 50))
    })

    expect(screen.getByText(`Estimated total ${zar(55.2)}.`)).toBeInTheDocument()
    expect(screen.queryByText((content) => content.includes(zar(999)))).not.toBeInTheDocument()
  })

  it('explains a failed quote without blocking the order', async () => {
    storeDraft({ drinks: [vanillaThickOreo] })
    renderOrderingAt('/order/drinks', {
      [QUOTE]: () =>
        jsonResponse({ status: 503, title: 'Ordering is temporarily unavailable.', detail: 'Please try again later.' }, 503),
    })

    expect(await screen.findByText('Ordering is temporarily unavailable. Please try again later.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue to pickup' })).toBeEnabled()
  })
})

describe('placing an order', () => {
  const ready: Partial<OrderDraft> = { restaurantId: 35, pickupLocal: '2026-11-20T10:00', drinks: [vanillaThickOreo] }
  const VALIDATE = 'POST /api/ordering/draft/validate'
  const PLACE = 'POST /api/ordering/orders'
  const ORDER = 'GET /api/ordering/orders/MS-7KQ2-X9TD'
  const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
  const keyOf = (init: RequestInit | undefined) => new Headers(init?.headers).get('Idempotency-Key')
  const bodyOf = (init: RequestInit | undefined) => JSON.parse(String(init?.body)) as Record<string, unknown>

  async function confirmPlacement() {
    await userEvent.click(await screen.findByRole('button', { name: /^(Place order|Try again)$/ }))
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Place order' }))
  }

  it('asks to confirm the total, places the order with an idempotency key, clears the draft and shows the order', async () => {
    storeDraft(ready)
    const fetchMock = renderOrderingAt('/order/review', {
      [VALIDATE]: () => jsonResponse(draftSummary()),
      [PLACE]: () => jsonResponse(placedOrder(), 201),
      [ORDER]: () => jsonResponse(placedOrder()),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Place order' }))
    expect(within(screen.getByRole('alertdialog')).getByText(new RegExp(zar(52.9)))).toBeInTheDocument()
    await userEvent.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Place order' }))

    expect(await screen.findByRole('heading', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    expect(screen.getByText('Awaiting payment')).toBeInTheDocument()
    expect(screen.getByText('Not paid')).toBeInTheDocument()
    const [call] = callsTo(fetchMock, 'POST', '/api/ordering/orders')
    expect(bodyOf(call?.[1])).toEqual({ ...ready, expectedTotal: 52.9, expectedCurrencyCode: 'ZAR' })
    expect(keyOf(call?.[1])).toMatch(UUID)
    expect(sessionStorage.getItem(draftStorageKey(patronUser.id))).toBeNull()
    expect(Object.keys(sessionStorage).filter((key) => key.startsWith('milkshake.'))).toEqual([])
  })

  it('sends one request however often the patron confirms', async () => {
    storeDraft(ready)
    let release: (() => void) | undefined
    const fetchMock = renderOrderingAt('/order/review', {
      [VALIDATE]: () => jsonResponse(draftSummary()),
      [PLACE]: () => new Promise<Response>((resolve) => (release = () => resolve(jsonResponse(placedOrder(), 201)))),
      [ORDER]: () => jsonResponse(placedOrder()),
    })

    await userEvent.click(await screen.findByRole('button', { name: 'Place order' }))
    const confirm = within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Place order' })
    fireEvent.click(confirm)
    fireEvent.click(confirm)
    fireEvent.click(confirm)

    expect(await screen.findByRole('button', { name: 'Placing your order…' })).toBeDisabled()
    await waitFor(() => expect(callsTo(fetchMock, 'POST', '/api/ordering/orders')).toHaveLength(1))
    await act(async () => release?.())
    expect(await screen.findByRole('heading', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/api/ordering/orders')).toHaveLength(1)
  })

  it('keeps the draft and the same key after a network failure, so trying again cannot create a second order', async () => {
    storeDraft(ready)
    let attempts = 0
    const fetchMock = renderOrderingAt('/order/review', {
      [VALIDATE]: () => jsonResponse(draftSummary()),
      [PLACE]: () => {
        attempts += 1
        return attempts === 1 ? Promise.reject(new TypeError('Failed to fetch')) : jsonResponse(placedOrder())
      },
      [ORDER]: () => jsonResponse(placedOrder()),
    })

    await confirmPlacement()
    expect(await screen.findByText(PLACEMENT_MESSAGES.uncertain)).toBeInTheDocument()
    expect(sessionStorage.getItem(draftStorageKey(patronUser.id))).not.toBeNull()

    await confirmPlacement()
    expect(await screen.findByRole('heading', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    const keys = callsTo(fetchMock, 'POST', '/api/ordering/orders').map(([, init]) => keyOf(init))
    expect(keys).toHaveLength(2)
    expect(keys[0]).toMatch(UUID)
    expect(keys[1]).toBe(keys[0])
  })

  it('shows a changed price and places the order only once the patron accepts the new total', async () => {
    storeDraft(ready)
    const newTotals = { ...draftSummary().totals, subtotal: 48, taxable: 48, vat: 7.2, total: 55.2 }
    let attempts = 0
    const fetchMock = renderOrderingAt('/order/review', {
      [VALIDATE]: () => jsonResponse(draftSummary()),
      [PLACE]: () => {
        attempts += 1
        return attempts === 1
          ? jsonResponse({ status: 409, title: 'The price has changed.', currentTotals: newTotals }, 409)
          : jsonResponse(placedOrder(), 201)
      },
      [ORDER]: () => jsonResponse(placedOrder()),
    })

    await confirmPlacement()
    const notice = await screen.findByRole('alert')
    expect(notice).toHaveTextContent('The price has changed')
    expect(notice).toHaveTextContent(`The new total is ${zar(55.2)}`)
    expect(callsTo(fetchMock, 'POST', '/api/ordering/orders')).toHaveLength(1)

    // The accessible name keeps Intl's non-breaking space, so compare it with spaces normalised.
    await userEvent.click(screen.getByRole('button', { name: (name) => name.replace(/\s/g, ' ') === `Place order at ${zar(55.2)}` }))

    expect(await screen.findByRole('heading', { name: 'Order MS-7KQ2-X9TD' })).toBeInTheDocument()
    const [first, second] = callsTo(fetchMock, 'POST', '/api/ordering/orders')
    expect(bodyOf(second?.[1])).toMatchObject({ expectedTotal: 55.2, expectedCurrencyCode: 'ZAR' })
    expect(keyOf(second?.[1])).toBe(keyOf(first?.[1]))
  })

  it('shows what the server rejected at placement and keeps the draft', async () => {
    storeDraft(ready)
    renderOrderingAt('/order/review', {
      [VALIDATE]: () => jsonResponse(draftSummary()),
      [PLACE]: () =>
        jsonResponse(
          { status: 400, title: 'One or more validation errors occurred.', errors: { pickupLocal: ['That pickup time has already passed.'] } },
          400,
        ),
    })

    await confirmPlacement()

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('That pickup time has already passed.')
    expect(within(alert).getByRole('link', { name: 'Change pickup' })).toBeInTheDocument()
    expect(sessionStorage.getItem(draftStorageKey(patronUser.id))).not.toBeNull()
  })
})

describe('order page', () => {
  it('shows the order as stored, with its own names and prices, and makes clear it is not paid', async () => {
    renderOrderingAt('/orders/MS-7KQ2-X9TD', { 'GET /api/ordering/orders/MS-7KQ2-X9TD': () => jsonResponse(placedOrder()) })

    // Focus moves in an effect just after the heading renders, so wait for it (M14 DEF-07: checking at once was flaky).
    const heading = await screen.findByRole('heading', { name: 'Order MS-7KQ2-X9TD' })
    await waitFor(() => expect(heading).toHaveFocus())
    expect(screen.getByText(/^Vanilla \(old recipe\) ·/)).toBeInTheDocument()
    expect(screen.getByText('Awaiting payment')).toBeInTheDocument()
    expect(screen.getByText('Not paid')).toBeInTheDocument()
    expect(screen.getByText(/has not been paid/)).toBeInTheDocument()
    const price = screen.getByRole('region', { name: 'Price' })
    expect(within(price).getByText('Total')).toBeInTheDocument()
    expect(within(price).getByText(zar(52.9))).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pay now' })).toBeEnabled()
  })

  it('says when the order is not the patron’s or does not exist', async () => {
    renderOrderingAt('/orders/MS-0000-0000', {
      'GET /api/ordering/orders/MS-0000-0000': () => jsonResponse({ status: 404, title: 'Order not found.' }, 404),
    })

    expect(await screen.findByRole('heading', { name: 'Order not found' })).toBeInTheDocument()
  })
})
