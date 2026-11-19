import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../../lib/api/httpClient'
import { quoteDrinks } from './api'
import { describeOrderingError } from './errors'
import type { DrinkSelection, Quote } from './types'

/** Wait after the last change before asking for prices, so a burst of selections sends one request. */
export const QUOTE_DELAY_MS = 400

/** After a 429 (per-patron rate limit), one retry after this long. */
export const QUOTE_RETRY_MS = 5000

export const QUOTE_MESSAGES = {
  busy: 'Prices will update in a moment.',
  stale: 'Some choices are no longer available. Please check your milkshakes.',
} as const

/** The latest server estimate for the current selections. `quote` is kept while a newer one is on its way. */
export type LiveQuote =
  | { status: 'idle'; quote: null }
  | { status: 'updating'; quote: Quote | null }
  | { status: 'ready'; quote: Quote }
  | { status: 'error'; quote: Quote | null; message: string }

/**
 * Keeps a server-calculated quote for the milkshakes being chosen (M6 decision: debounced, latest wins).
 * Each change waits {@link QUOTE_DELAY_MS}; the previous request is aborted, and a response is applied only if it
 * answers the most recent request, so a slow older response can never overwrite a newer price.
 * Nothing here calculates a price: every amount comes from the server.
 */
export function useLiveQuote(drinks: DrinkSelection[], onStaleChoices: () => void): LiveQuote {
  const key = JSON.stringify(drinks.map((drink) => [drink.flavourId, drink.consistencyId, drink.toppingId]))
  const [state, setState] = useState<LiveQuote>({ status: 'idle', quote: null })
  const latestRequest = useRef(0)
  const staleCallback = useRef(onStaleChoices)

  useEffect(() => {
    staleCallback.current = onStaleChoices
  })

  useEffect(() => {
    const selections = (JSON.parse(key) as (number | null)[][]).map(([flavourId, consistencyId, toppingId]) => ({
      flavourId: flavourId ?? null,
      consistencyId: consistencyId ?? null,
      toppingId: toppingId ?? null,
    }))
    if (selections.length === 0) {
      return undefined
    }

    const controller = new AbortController()
    let retryTimer: ReturnType<typeof setTimeout> | undefined

    const request = (attempt: number) => {
      latestRequest.current += 1
      const requestId = latestRequest.current
      const isCurrent = () => requestId === latestRequest.current && !controller.signal.aborted

      setState((current) => ({ status: 'updating', quote: current.quote }))
      quoteDrinks(selections, controller.signal)
        .then((quote) => {
          if (isCurrent()) {
            setState({ status: 'ready', quote })
          }
        })
        .catch((error: unknown) => {
          if (!isCurrent()) {
            return
          }
          if (error instanceof ApiError && error.status === 429 && attempt === 0) {
            setState((current) => ({ status: 'error', quote: current.quote, message: QUOTE_MESSAGES.busy }))
            retryTimer = setTimeout(() => request(1), QUOTE_RETRY_MS)
            return
          }
          const stale = error instanceof ApiError && error.status === 400
          if (stale) {
            // Something was deactivated or a limit changed: reload the options so stale choices are cleared (E6).
            staleCallback.current()
          }
          setState((current) => ({
            status: 'error',
            quote: current.quote,
            message: stale ? QUOTE_MESSAGES.stale : describeOrderingError(error),
          }))
        })
    }

    const debounce = setTimeout(() => request(0), QUOTE_DELAY_MS)
    return () => {
      clearTimeout(debounce)
      clearTimeout(retryTimer)
      controller.abort()
    }
  }, [key])

  return state
}
