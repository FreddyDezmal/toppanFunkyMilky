import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, clearCsrfToken } from '../../lib/api/httpClient'
import { draftSummary, orderingOptions } from '../../test/orderingFixtures'
import { jsonResponse, mockApi } from '../../test/mockApi'
import { fetchOrderingOptions, validateDraft } from './api'
import { draftErrorsFrom, describeOrderingError, drinkErrors, ORDERING_UNAVAILABLE } from './errors'
import type { OrderDraft } from './types'

beforeEach(() => {
  clearCsrfToken()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ordering API', () => {
  it('loads the options', async () => {
    mockApi({ 'GET /api/ordering/options': () => jsonResponse(orderingOptions()) })

    const options = await fetchOrderingOptions()

    expect(options.pickup.timeZone).toBe('Africa/Johannesburg')
  })

  it('sends identifiers and the pickup time only, with the anti-forgery token', async () => {
    let sent: RequestInit | undefined
    mockApi({
      'POST /api/ordering/draft/validate': (init) => {
        sent = init
        return jsonResponse(draftSummary())
      },
    })
    // Anything extra on the object (here a tampered fee) must not reach the API.
    const draft = {
      restaurantId: 35,
      pickupLocal: '2026-11-20T10:00',
      price: 0.01,
      drinks: [{ flavourId: 3, consistencyId: 29, toppingId: 19, fee: 0 }],
    } as unknown as OrderDraft

    const summary = await validateDraft(draft)

    expect(summary.isDraft).toBe(true)
    expect(JSON.parse(String(sent?.body))).toEqual({
      restaurantId: 35,
      pickupLocal: '2026-11-20T10:00',
      drinks: [{ flavourId: 3, consistencyId: 29, toppingId: 19 }],
    })
    expect(new Headers(sent?.headers).get('X-XSRF-TOKEN')).toBe('test-token')
  })
})

describe('ordering errors', () => {
  it('maps validation errors to draft fields and anything else to the form', () => {
    const error = new ApiError('One or more validation errors occurred.', 400, {
      errors: {
        pickupLocal: ['That pickup time has already passed. Please choose a later time.'],
        'drinks[1].toppingId': ['Choose a topping for milkshake 2.'],
        $: ['The JSON value could not be converted.'],
      },
    })

    const { fields, form } = draftErrorsFrom(error)

    expect(fields).toEqual({
      pickupLocal: 'That pickup time has already passed. Please choose a later time.',
      'drinks[1].toppingId': 'Choose a topping for milkshake 2.',
    })
    expect(form).toBe('The JSON value could not be converted.')
  })

  it('collects the messages for one milkshake', () => {
    const fields = {
      'drinks[1].toppingId': 'Choose a topping for milkshake 2.',
      'drinks[1].flavourId': 'Choose a flavour for milkshake 2.',
      'drinks[10].flavourId': 'Choose a flavour for milkshake 11.',
      restaurantId: 'Choose a pickup restaurant.',
    }

    expect(drinkErrors(fields, 1)).toEqual(['Choose a flavour for milkshake 2.', 'Choose a topping for milkshake 2.'])
  })

  it('describes 503 as ordering being unavailable', () => {
    const withDetail = new ApiError('Please try again later.', 503, {
      title: 'Ordering is temporarily unavailable.',
      detail: 'Please try again later.',
    })

    expect(describeOrderingError(withDetail)).toBe('Ordering is temporarily unavailable. Please try again later.')
    expect(describeOrderingError(new ApiError('HTTP 503', 503))).toBe(ORDERING_UNAVAILABLE)
    expect(draftErrorsFrom(withDetail).form).toBe('Ordering is temporarily unavailable. Please try again later.')
  })
})