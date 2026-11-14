import { useCallback, useEffect, useState } from 'react'
import { fetchHealth } from './api'
import type { HealthResponse } from './types'

export type HealthState =
  | { kind: 'loading' }
  | { kind: 'loaded'; data: HealthResponse }
  | { kind: 'error'; message: string }

/** Loads the API health once on mount and whenever refresh() is called. */
export function useSystemHealth() {
  const [state, setState] = useState<HealthState>({ kind: 'loading' })
  const [reloadToken, setReloadToken] = useState(0)

  useEffect(() => {
    const controller = new AbortController()

    fetchHealth(controller.signal)
      .then((data) => setState({ kind: 'loaded', data }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        setState({ kind: 'error', message: error instanceof Error ? error.message : 'Unknown error.' })
      })

    // Cancels the request if the component unmounts (or React StrictMode re-runs the effect).
    return () => controller.abort()
  }, [reloadToken])

  const refresh = useCallback(() => {
    setState({ kind: 'loading' })
    setReloadToken((token) => token + 1)
  }, [])

  return { state, refresh }
}