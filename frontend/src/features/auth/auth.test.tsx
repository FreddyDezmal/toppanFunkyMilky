import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRoutes } from '../../app/AppRoutes'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { jsonResponse, mockApi } from '../../test/mockApi'
import { AuthProvider } from './AuthProvider'
import type { CurrentUser } from './types'

const patron: CurrentUser = {
  id: '01a1ea38-0000-7000-8000-000000000001',
  firstName: 'Thandi',
  email: 'thandi@example.test',
  mobileNumber: '+27821234567',
  roles: ['Patron'],
}
const manager: CurrentUser = { ...patron, id: '01a1ea38-0000-7000-8000-000000000002', firstName: 'Mandla', roles: ['Manager'] }
const SIGN_IN_FAILED = 'Incorrect email or password, or the account is temporarily locked. Please try again later.'
const unauthorized = () => jsonResponse({ status: 401, title: 'Unauthorized' }, 401)

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider>
        <AppRoutes />
      </AuthProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('route protection', () => {
  it('sends anonymous visitors from a protected page to the login page', async () => {
    mockApi({ 'GET /api/auth/me': unauthorized })

    renderAt('/account')

    expect(await screen.findByRole('heading', { name: 'Log in to your account' })).toBeInTheDocument()
  })

  it('shows "access denied" to a patron on a manager page', async () => {
    mockApi({ 'GET /api/auth/me': () => jsonResponse(patron) })

    renderAt('/manager')

    expect(await screen.findByRole('heading', { name: 'Access denied' })).toBeInTheDocument()
  })

  it('lets a manager open the manager page', async () => {
    mockApi({
      'GET /api/auth/me': () => jsonResponse(manager),
      'GET /api/manager/ping': () => jsonResponse({ message: 'Manager access confirmed.' }),
      'GET /api/health': () => jsonResponse({ status: 'Healthy', checks: [] }),
    })

    renderAt('/manager')

    expect(await screen.findByText('Manager access confirmed.')).toBeInTheDocument()
  })
})

describe('login', () => {
  it('signs in with the anti-forgery token and returns to the requested page', async () => {
    const fetchMock = mockApi({
      'GET /api/auth/me': unauthorized,
      'POST /api/auth/login': () => jsonResponse(patron),
    })
    const user = userEvent.setup()

    renderAt('/account')
    await screen.findByRole('heading', { name: 'Log in to your account' })
    await user.type(screen.getByLabelText('Email address'), patron.email)
    await user.type(screen.getByLabelText('Password'), 'Milkshake1Test')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByRole('heading', { name: 'My account' })).toBeInTheDocument()
    const loginCall = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')
    expect(new Headers(loginCall?.[1]?.headers).get('X-XSRF-TOKEN')).toBe('test-token')
  })

  it("shows the server's message when sign-in fails", async () => {
    mockApi({
      'GET /api/auth/me': unauthorized,
      'POST /api/auth/login': () => jsonResponse({ status: 401, title: 'Sign-in failed.', detail: SIGN_IN_FAILED }, 401),
    })
    const user = userEvent.setup()

    renderAt('/login')
    await screen.findByRole('heading', { name: 'Log in to your account' })
    await user.type(screen.getByLabelText('Email address'), patron.email)
    await user.type(screen.getByLabelText('Password'), 'Wrong1Password')
    await user.click(screen.getByRole('button', { name: 'Log in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(SIGN_IN_FAILED)
  })
})

describe('registration', () => {
  it('validates every field before calling the API', async () => {
    const fetchMock = mockApi({ 'GET /api/auth/me': unauthorized })
    const user = userEvent.setup()

    renderAt('/register')
    await screen.findByRole('heading', { name: 'Create your account' })
    await user.click(screen.getByRole('button', { name: 'Create account' }))

    expect(screen.getByText('First name is required.')).toBeInTheDocument()
    expect(screen.getByText('Email address is required.')).toBeInTheDocument()
    expect(screen.getByText('Mobile number is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
    expect(screen.getByText('Please confirm your password.')).toBeInTheDocument()
    expect(screen.getByLabelText('First name')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('First name')).toHaveFocus()
    expect(fetchMock.mock.calls.some(([input]) => String(input).includes('/api/auth/register'))).toBe(false)
  })

  it('enforces the password rules and a matching confirmation', async () => {
    mockApi({ 'GET /api/auth/me': unauthorized })
    const user = userEvent.setup()

    renderAt('/register')
    await screen.findByRole('heading', { name: 'Create your account' })
    await user.type(screen.getByLabelText('First name'), 'Thandi')
    await user.type(screen.getByLabelText('Email address'), 'thandi@example.test')
    await user.type(screen.getByLabelText('Mobile number'), '+27 82 123 4567')
    await user.type(screen.getByLabelText('Password'), 'lowercase1')
    await user.type(screen.getByLabelText('Confirm password'), 'lowercase2')
    await user.click(screen.getByRole('button', { name: 'Create account' }))

    expect(screen.getByText('This password is too weak.')).toBeInTheDocument()
    expect(screen.getByText('Passwords do not match.')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true')
  })

  it("shows the server's duplicate-email message on the email field", async () => {
    const duplicate = 'This email address is already registered.'
    mockApi({
      'GET /api/auth/me': unauthorized,
      'POST /api/auth/register': () =>
        jsonResponse({ status: 400, title: 'One or more validation errors occurred.', errors: { email: [duplicate] } }, 400),
    })
    const user = userEvent.setup()

    renderAt('/register')
    await screen.findByRole('heading', { name: 'Create your account' })
    await user.type(screen.getByLabelText('First name'), 'Thandi')
    await user.type(screen.getByLabelText('Email address'), 'thandi@example.test')
    await user.type(screen.getByLabelText('Mobile number'), '+27 82 123 4567')
    await user.type(screen.getByLabelText('Password'), 'Milkshake1Test')
    await user.type(screen.getByLabelText('Confirm password'), 'Milkshake1Test')
    await user.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByText(duplicate)).toBeInTheDocument()
    expect(screen.getByLabelText('Email address')).toHaveAttribute('aria-invalid', 'true')
  })
})

describe('logout', () => {
  it('ends the session and returns to the login page', async () => {
    mockApi({
      'GET /api/auth/me': () => jsonResponse(patron),
      'POST /api/auth/logout': () => jsonResponse(null, 204),
    })
    const user = userEvent.setup()

    renderAt('/account')
    await screen.findByRole('heading', { name: 'My account' })
    await user.click(screen.getByRole('button', { name: 'Log out' }))

    expect(await screen.findByRole('heading', { name: 'Log in to your account' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Log out' })).not.toBeInTheDocument()
  })
})