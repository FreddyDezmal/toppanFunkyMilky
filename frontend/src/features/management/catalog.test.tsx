import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../../app/AppRoutes'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { jsonResponse, mockApi, type ApiRoutes } from '../../test/mockApi'
import { AuthProvider } from '../auth/AuthProvider'
import type { CurrentUser } from '../auth/types'
import { CONFLICT_MESSAGE } from './errors'
import type { CatalogItem, Setting } from './types'

const manager: CurrentUser = {
  id: '01a1ea38-0000-7000-8000-000000000002',
  firstName: 'Mandla',
  email: 'mandla@example.test',
  mobileNumber: '+27821234567',
  roles: ['Manager'],
}

const settings: Setting[] = [
  { key: 'Pricing.DisplayLocale', value: 'en-ZA', valueType: 'Text', description: null, isEditable: true, hint: '', version: 1 },
  { key: 'Pricing.CurrencyCode', value: 'ZAR', valueType: 'Text', description: null, isEditable: false, hint: '', version: 1 },
]

const LIST = 'GET /api/manager/catalog/flavours'

function flavour(overrides: Partial<CatalogItem> = {}): CatalogItem {
  return {
    id: 1,
    name: 'Vanilla',
    displayOrder: 1,
    isActive: true,
    currentPrice: 30,
    currencyCode: 'ZAR',
    scheduledPrice: null,
    scheduledFrom: null,
    version: 7,
    ...overrides,
  }
}

function renderFlavours(routes: ApiRoutes) {
  const fetchMock = mockApi({
    'GET /api/auth/me': () => jsonResponse(manager),
    'GET /api/manager/settings': () => jsonResponse(settings),
    ...routes,
  })
  render(
    <MemoryRouter initialEntries={['/manager/catalog/flavours']}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
  return fetchMock
}

function callsTo(fetchMock: ReturnType<typeof mockApi>, method: string, path: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) => (init?.method ?? 'GET') === method && new URL(String(input), 'http://localhost').pathname === path,
  )
}

const normalise = (text: string) => text.replace(/\s+/g, ' ')

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('catalogue management', () => {
  it('lists items with prices formatted for the configured locale and currency', async () => {
    renderFlavours({ [LIST]: () => jsonResponse([flavour()]) })

    const row = await screen.findByRole('row', { name: /Vanilla/ })
    const expected = normalise(new Intl.NumberFormat('en-ZA', { style: 'currency', currency: 'ZAR' }).format(30))
    await waitFor(() => expect(within(row).getByText(expected)).toBeInTheDocument())
    expect(within(row).getByText('Active')).toBeInTheDocument()
  })

  it('adds an item with its starting price, sending the anti-forgery token', async () => {
    let created = false
    const mint = flavour({ id: 2, name: 'Mint', displayOrder: 2, currentPrice: 25.5 })
    const fetchMock = renderFlavours({
      [LIST]: () => jsonResponse(created ? [flavour(), mint] : [flavour()]),
      'POST /api/manager/catalog/flavours': () => {
        created = true
        return jsonResponse(mint, 201)
      },
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Add flavour' }))
    await user.type(screen.getByLabelText('Name'), 'Mint')
    await user.clear(screen.getByLabelText('Display order'))
    await user.type(screen.getByLabelText('Display order'), '2')
    await user.type(screen.getByLabelText('Price'), '25,50')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Added “Mint”.')).toBeInTheDocument()
    expect(await screen.findByRole('row', { name: /Mint/ })).toBeInTheDocument()
    const [post] = callsTo(fetchMock, 'POST', '/api/manager/catalog/flavours')
    expect(post).toBeDefined()
    const init: RequestInit = post?.[1] ?? {}
    expect(JSON.parse(String(init.body))).toEqual({ name: 'Mint', displayOrder: 2, initialPrice: 25.5 })
    expect((init.headers as Record<string, string>)['X-XSRF-TOKEN']).toBe('test-token')
  })

  it('shows the server message next to the field and focuses it', async () => {
    const duplicate = 'A flavour with this name already exists. If it is inactive, reactivate it instead.'
    renderFlavours({
      [LIST]: () => jsonResponse([flavour()]),
      'POST /api/manager/catalog/flavours': () =>
        jsonResponse({ status: 400, title: 'One or more validation errors occurred.', errors: { name: [duplicate] } }, 400),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Add flavour' }))
    await user.type(screen.getByLabelText('Name'), 'vanilla')
    await user.type(screen.getByLabelText('Price'), '30')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(duplicate)).toBeInTheDocument()
    const name = screen.getByLabelText('Name')
    expect(name).toHaveAttribute('aria-invalid', 'true')
    expect(name).toHaveFocus()
  })

  it('explains a conflict and reloads when asked', async () => {
    const fetchMock = renderFlavours({
      [LIST]: () => jsonResponse([flavour()]),
      'PUT /api/manager/catalog/flavours/1': () =>
        jsonResponse({ status: 409, title: 'This record was changed by someone else.' }, 409),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit Vanilla' }))
    await user.type(screen.getByLabelText('Name'), ' Bean')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(CONFLICT_MESSAGE)).toBeInTheDocument()
    const listCallsBefore = callsTo(fetchMock, 'GET', '/api/manager/catalog/flavours').length
    await user.click(screen.getByRole('button', { name: 'Reload' }))
    await waitFor(() =>
      expect(callsTo(fetchMock, 'GET', '/api/manager/catalog/flavours').length).toBeGreaterThan(listCallsBefore),
    )
  })

  it('asks for confirmation before deactivating', async () => {
    const fetchMock = renderFlavours({
      [LIST]: () => jsonResponse([flavour()]),
      'POST /api/manager/catalog/flavours/1/deactivate': () => jsonResponse(flavour({ isActive: false, version: 8 })),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Deactivate Vanilla' }))
    const dialog = screen.getByRole('alertdialog', { name: 'Deactivate Vanilla?' })
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/api/manager/catalog/flavours/1/deactivate')).toHaveLength(0)

    await user.click(screen.getByRole('button', { name: 'Deactivate Vanilla' }))
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Deactivate' }))

    expect(await screen.findByText('“Vanilla” is hidden from new orders. Its history is kept.')).toBeInTheDocument()
    const [post] = callsTo(fetchMock, 'POST', '/api/manager/catalog/flavours/1/deactivate')
    expect(JSON.parse(String(post?.[1]?.body))).toEqual({ version: 7 })
  })

  it('sends only one request when Save is double-clicked', async () => {
    const fetchMock = renderFlavours({
      [LIST]: () => jsonResponse([flavour()]),
      'POST /api/manager/catalog/flavours': () =>
        new Promise((resolve) => setTimeout(() => resolve(jsonResponse(flavour({ id: 3, name: 'Lime' }), 201)), 50)),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Add flavour' }))
    await user.type(screen.getByLabelText('Name'), 'Lime')
    await user.type(screen.getByLabelText('Price'), '20')
    await user.dblClick(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Added “Lime”.')).toBeInTheDocument()
    expect(callsTo(fetchMock, 'POST', '/api/manager/catalog/flavours')).toHaveLength(1)
  })
})