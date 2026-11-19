import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { AppRoutes } from '../app/AppRoutes'
import { AuthProvider } from '../features/auth/AuthProvider'
import type { CurrentUser } from '../features/auth/types'
import type { CatalogItem, PricePeriod, Setting } from '../features/management/types'
import { jsonResponse, mockApi, type ApiRoutes } from './mockApi'

export const managerUser: CurrentUser = {
  id: '01a1ea38-0000-7000-8000-000000000002',
  firstName: 'Mandla',
  email: 'mandla@example.test',
  mobileNumber: '+27821234567',
  roles: ['Manager'],
}

export const displaySettings: Setting[] = [
  { key: 'Pricing.DisplayLocale', value: 'en-ZA', valueType: 'Text', description: null, isEditable: true, hint: '', version: 1 },
  { key: 'Pricing.CurrencyCode', value: 'ZAR', valueType: 'Text', description: null, isEditable: false, hint: '', version: 1 },
]

export function flavour(overrides: Partial<CatalogItem> = {}): CatalogItem {
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

export function pricePeriod(overrides: Partial<PricePeriod> = {}): PricePeriod {
  return {
    id: 11,
    amount: 30,
    currencyCode: 'ZAR',
    effectiveFrom: '2026-01-01T00:00:00+00:00',
    effectiveTo: null,
    status: 'Current',
    canCancel: false,
    ...overrides,
  }
}

/** Renders the whole app at a manager path with a signed-in manager and the display settings. */
export function renderManagerAt(path: string, routes: ApiRoutes) {
  const fetchMock = mockApi({
    'GET /api/auth/me': () => jsonResponse(managerUser),
    'GET /api/manager/settings': () => jsonResponse(displaySettings),
    ...routes,
  })
  render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
  return fetchMock
}

/** The recorded fetch calls for one method and path (query string ignored). */
export function callsTo(fetchMock: ReturnType<typeof mockApi>, method: string, path: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) => (init?.method ?? 'GET') === method && new URL(String(input), 'http://localhost').pathname === path,
  )
}

/** The parsed JSON body of a recorded call. */
export function bodyOf(call: unknown[] | undefined): unknown {
  const init = (call?.[1] ?? {}) as RequestInit
  return JSON.parse(String(init.body))
}