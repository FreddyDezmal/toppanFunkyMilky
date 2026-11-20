import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fetchPendingCancellationRequests } from './api'
import type { PendingCancellationRequest } from './types'

/**
 * Home page notice (Q3): the patron's orders with a manager request waiting for an answer. Shows nothing when there
 * are none or they cannot be loaded (the order page still shows each request).
 */
export function CancellationRequestNotice() {
  const [requests, setRequests] = useState<PendingCancellationRequest[]>([])

  useEffect(() => {
    const controller = new AbortController()
    fetchPendingCancellationRequests(controller.signal).then(
      (loaded) => {
        if (!controller.signal.aborted) {
          setRequests(loaded)
        }
      },
      () => undefined,
    )
    return () => controller.abort()
  }, [])

  if (requests.length === 0) {
    return null
  }

  return (
    <section aria-labelledby="requests-heading" className="rounded-2xl border border-amber-300 bg-amber-50 p-6">
      <h2 id="requests-heading" className="text-xl font-semibold text-amber-900">
        {requests.length === 1 ? 'The restaurant has asked to cancel an order' : 'The restaurant has asked to cancel some orders'}
      </h2>
      <ul className="mt-2 space-y-2 text-sm text-amber-900">
        {requests.map((request) => (
          <li key={request.orderReference}>
            Order {request.orderReference}: <q>{request.reason}</q>{' '}
            <Link to={`/orders/${request.orderReference}`} className="font-medium underline">
              Review order {request.orderReference}
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}
