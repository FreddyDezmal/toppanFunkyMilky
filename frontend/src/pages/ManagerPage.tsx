import { useEffect, useState } from 'react'
import { fetchManagerPing } from '../features/manager/api'
import { describeError } from '../lib/api/describeError'

type PingState = { kind: 'loading' } | { kind: 'ok'; message: string } | { kind: 'error'; message: string }

/** Management area: lookup management, orders, emails and reports are in the sections above. */
export function ManagerPage() {
  const [state, setState] = useState<PingState>({ kind: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    fetchManagerPing(controller.signal)
      .then((body) => setState({ kind: 'ok', message: body.message }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setState({ kind: 'error', message: describeError(error) })
        }
      })
    return () => controller.abort()
  }, [])

  return (
    <section className="rounded-2xl bg-white p-8 shadow-sm ring-1 ring-slate-200">
      <h1 className="text-2xl font-bold tracking-tight">Management</h1>
      <p className="mt-1 text-slate-600">Choose a section above to manage the menu, find orders or see reports.</p>
      <p className="mt-6 text-sm" role="status">
        {state.kind === 'loading' ? 'Checking manager access…' : state.message}
      </p>
    </section>
  )
}