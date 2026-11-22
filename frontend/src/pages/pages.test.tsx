import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../app/AppRoutes'
import { AuthProvider } from '../features/auth/AuthProvider'
import type { CurrentUser } from '../features/auth/types'
import { clearCsrfToken } from '../lib/api/httpClient'
import { jsonResponse, mockApi, type ApiRoutes } from '../test/mockApi'
import { orderingOptions, patronUser } from '../test/orderingFixtures'

const manager: CurrentUser = { ...patronUser, id: '01a1ea38-0000-7000-8000-000000000009', firstName: 'Mandla', roles: ['Manager'] }
const unauthorized = () => jsonResponse({ status: 401, title: 'Unauthorized' }, 401)

function renderAt(path: string, routes: ApiRoutes) {
  const fetchMock = mockApi(routes)
  render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
  return fetchMock
}

beforeEach(() => {
  clearCsrfToken()
  sessionStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('landing page', () => {
  it('greets visitors with the hero and ways in, without build notes or system status', async () => {
    renderAt('/', { 'GET /api/auth/me': unauthorized })

    expect(screen.getByRole('heading', { level: 1, name: 'Your Milkshake. Your Rules.' })).toBeInTheDocument()
    expect(screen.getByText(/Pick your flavour, choose your consistency/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Build Your Milkshake' })).toHaveAttribute('href', '/order')
    const main = screen.getByRole('main')
    expect(await within(main).findByRole('link', { name: 'Log in' })).toHaveAttribute('href', '/login')
    const ways = within(main).getAllByRole('link', { name: 'Create an account' })
    expect(ways.every((link) => link.getAttribute('href') === '/register')).toBe(true)
    expect(screen.getByRole('heading', { name: 'How it works' })).toBeInTheDocument()
    expect(screen.queryByText(/Foundation build/)).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: /System status/i })).not.toBeInTheDocument()
  })

  it('shows patrons the live menu with current prices before VAT', async () => {
    renderAt('/', {
      'GET /api/auth/me': () => jsonResponse(patronUser),
      'GET /api/ordering/options': () => jsonResponse(orderingOptions()),
      'GET /api/ordering/cancellation-requests': () => jsonResponse([]),
    })

    const menu = await screen.findByRole('region', { name: 'The menu' })
    const flavours = await within(menu).findByRole('heading', { name: 'Flavours' })
    const flavourList = flavours.parentElement as HTMLElement
    expect(within(flavourList).getByText('Vanilla')).toBeInTheDocument()
    expect(within(flavourList).getByText(/R\s?30,00/)).toBeInTheDocument()
    expect(within(menu).getByText('Included')).toBeInTheDocument()
    expect(within(menu).getByText(/before VAT/)).toBeInTheDocument()
    expect(within(menu).getByText('Branch A')).toBeInTheDocument()
    expect(within(screen.getByRole('main')).getByRole('link', { name: 'My orders' })).toHaveAttribute('href', '/orders')
  })

  it('leaves the menu out when it cannot be loaded', async () => {
    renderAt('/', {
      'GET /api/auth/me': () => jsonResponse(patronUser),
      'GET /api/ordering/options': () => jsonResponse({ status: 500 }, 500),
      'GET /api/ordering/cancellation-requests': () => jsonResponse([]),
    })

    expect(await screen.findByRole('heading', { name: 'How it works' })).toBeInTheDocument()
    await vi.waitFor(() => expect(screen.queryByRole('region', { name: 'The menu' })).not.toBeInTheDocument())
  })

  it('sends managers to management instead of ordering', async () => {
    renderAt('/', { 'GET /api/auth/me': () => jsonResponse(manager) })

    expect(await screen.findByRole('link', { name: 'Go to management' })).toHaveAttribute('href', '/manager')
    expect(screen.queryByRole('link', { name: 'Build Your Milkshake' })).not.toBeInTheDocument()
  })
})

describe('account and not-found pages', () => {
  it('shows the account details with next steps for a patron', async () => {
    renderAt('/account', { 'GET /api/auth/me': () => jsonResponse(patronUser) })

    expect(await screen.findByRole('heading', { level: 1, name: 'My account' })).toBeInTheDocument()
    expect(screen.getByText(patronUser.email)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Build Your Milkshake' })).toHaveAttribute('href', '/order')
  })

  it('offers a way home from an unknown address', async () => {
    renderAt('/no-such-page', { 'GET /api/auth/me': unauthorized })

    expect(await screen.findByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Back to the home page' })).toHaveAttribute('href', '/')
  })
})
