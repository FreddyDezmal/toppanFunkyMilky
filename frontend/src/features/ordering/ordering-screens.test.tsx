import { fireEvent, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { jsonResponse } from '../../test/mockApi'
import { draftSummary, patronUser } from '../../test/orderingFixtures'
import { callsTo, renderOrderingAt } from '../../test/renderOrdering'
import type { CurrentUser } from '../auth/types'
import { saveDraft } from './draftStorage'
import { RECONCILE_MESSAGES } from './reconcileDraft'
import type { OrderDraft } from './types'

const manager: CurrentUser = { ...patronUser, id: 'manager-1', firstName: 'Mandla', roles: ['Manager'] }
const vanillaThickOreo = { flavourId: 3, consistencyId: 29, toppingId: 19 }
const empty = { flavourId: null, consistencyId: null, toppingId: null }

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
  it('offers patrons an Order link and a Start an order button', async () => {
    renderOrderingAt('/')

    await userEvent.click(await screen.findByRole('link', { name: 'Start an order' }))

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
    expect(screen.getByText(/Fees shown are the current fees/)).toBeInTheDocument()

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

  it('shows the server-checked draft with its fees and no total, and payment is not offered yet', async () => {
    storeDraft(ready)
    const fetchMock = renderOrderingAt('/order/review', { [VALIDATE]: () => jsonResponse(draftSummary()) })

    expect(await screen.findByText(/^Draft – not yet ordered or paid/)).toBeInTheDocument()
    expect(screen.getByText('Branch A')).toBeInTheDocument()
    expect(screen.getByText(/^Vanilla ·/)).toBeInTheDocument()
    expect(screen.queryByText(/total/i)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Continue to payment (coming soon)' })).toBeDisabled()

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