import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { jsonResponse, mockApi } from '../../test/mockApi'
import { patronUser } from '../../test/orderingFixtures'
import { AuthProvider } from '../auth/AuthProvider'
import { useAuth } from '../auth/authContext'
import { draftStorageKey, saveDraft } from './draftStorage'
import { OrderDraftProvider } from './OrderDraftProvider'
import { useOrderDraft } from './orderDraftContext'

/** A minimal consumer: shows the quantity and offers the actions the screens will use. */
function Probe() {
  const { user, logout } = useAuth()
  const { draft, dispatch } = useOrderDraft()
  return (
    <div>
      <p>User: {user?.firstName ?? 'none'}</p>
      <p>Milkshakes: {draft.drinks.length}</p>
      <button type="button" onClick={() => dispatch({ type: 'setQuantity', quantity: draft.drinks.length + 1 })}>
        Add
      </button>
      <button type="button" onClick={() => void logout()}>
        Log out
      </button>
    </div>
  )
}

function renderProbe() {
  return render(
    <AuthProvider>
      <OrderDraftProvider>
        <Probe />
      </OrderDraftProvider>
    </AuthProvider>,
  )
}

beforeEach(() => {
  sessionStorage.clear()
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('OrderDraftProvider', () => {
  it('restores the signed-in patron’s draft and keeps it in sessionStorage', async () => {
    mockApi({ 'GET /api/auth/me': () => jsonResponse(patronUser) })
    saveDraft(patronUser.id, { restaurantId: null, pickupLocal: null, drinks: [{ flavourId: 3, consistencyId: null, toppingId: null }] })

    renderProbe()
    expect(await screen.findByText('User: Thandi')).toBeInTheDocument()
    expect(screen.getByText('Milkshakes: 1')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Add' }))

    expect(screen.getByText('Milkshakes: 2')).toBeInTheDocument()
    const stored = JSON.parse(sessionStorage.getItem(draftStorageKey(patronUser.id)) ?? '{}') as { drinks: unknown[] }
    expect(stored.drinks).toHaveLength(2)
  })

  it('removes drafts left in the tab by another user', async () => {
    mockApi({ 'GET /api/auth/me': () => jsonResponse(patronUser) })
    saveDraft('someone-else', { restaurantId: 35, pickupLocal: null, drinks: [] })

    renderProbe()
    expect(await screen.findByText('User: Thandi')).toBeInTheDocument()

    expect(sessionStorage.getItem(draftStorageKey('someone-else'))).toBeNull()
  })

  it('clears the draft on sign-out', async () => {
    mockApi({
      'GET /api/auth/me': () => jsonResponse(patronUser),
      'POST /api/auth/logout': () => jsonResponse(null, 204),
    })
    saveDraft(patronUser.id, { restaurantId: 35, pickupLocal: null, drinks: [] })
    renderProbe()
    expect(await screen.findByText('User: Thandi')).toBeInTheDocument()

    await act(async () => {
      await userEvent.click(screen.getByRole('button', { name: 'Log out' }))
    })

    expect(await screen.findByText('User: none')).toBeInTheDocument()
    expect(screen.getByText('Milkshakes: 0')).toBeInTheDocument()
    expect(sessionStorage.getItem(draftStorageKey(patronUser.id))).toBeNull()
  })
})