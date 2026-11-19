import { fireEvent, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { bodyOf, callsTo, flavour, pricePeriod, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import { CONFLICT_MESSAGE } from './errors'

const PATH = '/manager/catalog/flavours/1/prices'
const HISTORY = 'GET /api/manager/catalog/flavours/1/prices'
const SCHEDULE = 'POST /api/manager/catalog/flavours/1/prices'
const FUTURE_UTC = '2099-01-01T07:00:00+00:00'

const scheduled = pricePeriod({ id: 12, amount: 35, effectiveFrom: FUTURE_UTC, status: 'Scheduled', canCancel: true })
const currentUntilScheduled = pricePeriod({ effectiveTo: FUTURE_UTC })

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('price management', () => {
  it('shows the history and offers Cancel only for the scheduled price', async () => {
    renderManagerAt(PATH, {
      [HISTORY]: () => jsonResponse({ item: flavour({ version: 8 }), prices: [scheduled, currentUntilScheduled] }),
    })

    expect(await screen.findByRole('heading', { name: 'Prices: Vanilla' })).toBeInTheDocument()
    expect(screen.getByText('Scheduled')).toBeInTheDocument()
    expect(screen.getByText('Current')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Cancel the scheduled price/ })).toHaveLength(1)
  })

  it('schedules a future price, sending UTC time and the item version', async () => {
    const fetchMock = renderManagerAt(PATH, {
      [HISTORY]: () => jsonResponse({ item: flavour(), prices: [pricePeriod()] }),
      [SCHEDULE]: () => jsonResponse({ item: flavour({ version: 8 }), prices: [scheduled, currentUntilScheduled] }),
    })
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('New price'), '35')
    await user.click(screen.getByLabelText('At a later date and time'))
    fireEvent.change(screen.getByLabelText('Starts at'), { target: { value: '2099-01-01T09:00' } })
    await user.click(screen.getByRole('button', { name: 'Schedule price' }))

    expect(await screen.findByText(/will apply from/)).toBeInTheDocument()
    const [post] = callsTo(fetchMock, 'POST', '/api/manager/catalog/flavours/1/prices')
    expect(bodyOf(post)).toEqual({ amount: 35, effectiveFrom: new Date('2099-01-01T09:00').toISOString(), version: 7 })
  })

  it('refuses a start time in the past without calling the API', async () => {
    const fetchMock = renderManagerAt(PATH, {
      [HISTORY]: () => jsonResponse({ item: flavour(), prices: [pricePeriod()] }),
    })
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('New price'), '35')
    await user.click(screen.getByLabelText('At a later date and time'))
    fireEvent.change(screen.getByLabelText('Starts at'), { target: { value: '2000-01-01T09:00' } })
    await user.click(screen.getByRole('button', { name: 'Schedule price' }))

    expect(
      await screen.findByText('Choose a date and time in the future. Prices that have taken effect cannot be changed.'),
    ).toBeInTheDocument()

    expect(screen.getByLabelText('Starts at')).toHaveFocus()
    expect(callsTo(fetchMock, 'POST', '/api/manager/catalog/flavours/1/prices')).toHaveLength(0)
  })

  it('cancels a scheduled price after confirmation', async () => {
    const fetchMock = renderManagerAt(PATH, {
      [HISTORY]: () => jsonResponse({ item: flavour({ version: 8 }), prices: [scheduled, currentUntilScheduled] }),
      'POST /api/manager/catalog/flavours/1/prices/12/cancel': () =>
        jsonResponse({ item: flavour({ version: 9 }), prices: [pricePeriod()] }),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /Cancel the scheduled price/ }))
    const dialog = screen.getByRole('alertdialog', { name: 'Cancel this scheduled price?' })
    expect(within(dialog).getByRole('button', { name: 'Keep it' })).toHaveFocus()
    await user.click(within(dialog).getByRole('button', { name: 'Cancel scheduled price' }))

    expect(await screen.findByText(/was cancelled/)).toBeInTheDocument()
    expect(screen.queryByText('Scheduled')).not.toBeInTheDocument()
    const [post] = callsTo(fetchMock, 'POST', '/api/manager/catalog/flavours/1/prices/12/cancel')
    expect(bodyOf(post)).toEqual({ version: 8 })
  })

  it('explains a conflict when someone else changed the price first', async () => {
    renderManagerAt(PATH, {
      [HISTORY]: () => jsonResponse({ item: flavour(), prices: [pricePeriod()] }),
      [SCHEDULE]: () => jsonResponse({ status: 409, title: 'This record was changed by someone else.' }, 409),
    })
    const user = userEvent.setup()

    await user.type(await screen.findByLabelText('New price'), '40')
    await user.click(screen.getByRole('button', { name: 'Change price now' }))

    expect(await screen.findByText(CONFLICT_MESSAGE)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })
})