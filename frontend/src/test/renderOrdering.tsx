import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { AppRoutes } from '../app/AppRoutes'
import { AuthProvider } from '../features/auth/AuthProvider'
import type { CurrentUser } from '../features/auth/types'
import { jsonResponse, mockApi, type ApiRoutes } from './mockApi'
import { orderingOptions, patronUser } from './orderingFixtures'

/**
 * Renders the whole app at an ordering path with a signed-in user (a patron by default) and the fixture options.
 * Pass routes to override or add API responses.
 */
export function renderOrderingAt(path: string, routes: ApiRoutes = {}, user: CurrentUser = patronUser) {
  const fetchMock = mockApi({
    'GET /api/auth/me': () => jsonResponse(user),
    'GET /api/ordering/options': () => jsonResponse(orderingOptions()),
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

/** The calls made to one "METHOD /path" route. */
export function callsTo(fetchMock: ReturnType<typeof mockApi>, method: string, path: string) {
  return fetchMock.mock.calls.filter(
    ([input, init]) => (init?.method ?? 'GET') === method && new URL(String(input), 'http://localhost').pathname === path,
  )
}