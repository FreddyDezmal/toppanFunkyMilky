import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearCsrfToken } from '../../lib/api/httpClient'
import { bodyOf, callsTo, renderManagerAt } from '../../test/managementFixtures'
import { jsonResponse } from '../../test/mockApi'
import type { Restaurant } from './types'

const LIST = 'GET /api/manager/restaurants'
const central: Restaurant = { id: 5, name: 'Milky Shaky Central', address: '1 Main Road', isActive: true, version: 3 }

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('restaurant management', () => {
  it('adds a restaurant with an optional address', async () => {
    let created = false
    const harbour: Restaurant = { id: 6, name: 'Harbour', address: null, isActive: true, version: 1 }
    const fetchMock = renderManagerAt('/manager/restaurants', {
      [LIST]: () => jsonResponse(created ? [central, harbour] : [central]),
      'POST /api/manager/restaurants': () => {
        created = true
        return jsonResponse(harbour, 201)
      },
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Add restaurant' }))
    await user.type(screen.getByLabelText('Name'), 'Harbour')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Added “Harbour”.')).toBeInTheDocument()
    expect(await screen.findByRole('row', { name: /Harbour/ })).toBeInTheDocument()
    const [post] = callsTo(fetchMock, 'POST', '/api/manager/restaurants')
    expect(bodyOf(post)).toEqual({ name: 'Harbour', address: null })
  })

  it('asks for confirmation before deactivating', async () => {
    const fetchMock = renderManagerAt('/manager/restaurants', {
      [LIST]: () => jsonResponse([central]),
      'POST /api/manager/restaurants/5/deactivate': () => jsonResponse({ ...central, isActive: false, version: 4 }),
    })
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Deactivate Milky Shaky Central' }))
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Deactivate' }))

    expect(await screen.findByText(/can no longer be chosen for new orders/)).toBeInTheDocument()
    const [post] = callsTo(fetchMock, 'POST', '/api/manager/restaurants/5/deactivate')
    expect(bodyOf(post)).toEqual({ version: 3 })
  })
})