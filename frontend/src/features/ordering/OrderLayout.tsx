import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { NavLink, Outlet } from 'react-router'
import { fetchOrderingOptions } from './api'
import { describeOrderingError } from './errors'
import { OrderDraftProvider } from './OrderDraftProvider'
import { useOrderDraft } from './orderDraftContext'
import { reconcileDraft } from './reconcileDraft'
import type { OrderingOptions } from './types'
import type { OrderingContextValue } from './useOrdering'

const STEPS = [
  { to: '/order', label: 'Quantity', end: true },
  { to: '/order/drinks', label: 'Milkshakes', end: false },
  { to: '/order/pickup', label: 'Pickup', end: false },
  { to: '/order/review', label: 'Review', end: false },
] as const

function stepClass({ isActive }: { isActive: boolean }) {
  return `block rounded-md px-3 py-1.5 text-sm font-medium ${
    isActive ? 'bg-brand-100 text-brand-900' : 'text-slate-600 hover:text-brand-700'
  }`
}

/** /order/* — holds the patron's draft and the options in force, and shows the ordering steps. */
export function OrderLayout() {
  return (
    <OrderDraftProvider>
      <OrderingShell />
    </OrderDraftProvider>
  )
}

function OrderingShell() {
  const { draft, dispatch } = useOrderDraft()
  const draftRef = useRef(draft)
  const [options, setOptions] = useState<OrderingOptions | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [notices, setNotices] = useState<string[]>([])
  const [loadCount, setLoadCount] = useState(0)

  useEffect(() => {
    draftRef.current = draft
  }, [draft])

  useEffect(() => {
    const controller = new AbortController()
    fetchOrderingOptions(controller.signal)
      .then((loaded) => {
        if (controller.signal.aborted) {
          return
        }
        // Stored selections may be stale: clear what is no longer offered and say so (decision E6).
        const result = reconcileDraft(draftRef.current, loaded)
        if (result.draft !== draftRef.current) {
          dispatch({ type: 'replace', draft: result.draft })
        }
        if (result.notices.length > 0) {
          setNotices(result.notices)
        }
        setOptions(loaded)
        setLoadError(null)
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describeOrderingError(error))
        }
      })
    return () => controller.abort()
  }, [loadCount, dispatch])

  const reloadOptions = useCallback(() => setLoadCount((count) => count + 1), [])

  const context = useMemo<OrderingContextValue | null>(
    () => (options ? { options, reloadOptions } : null),
    [options, reloadOptions],
  )

  return (
    <div className="space-y-6">
      <nav aria-label="Order steps">
        <ol className="flex flex-wrap gap-1">
          {STEPS.map((step, index) => (
            <li key={step.to}>
              <NavLink to={step.to} end={step.end} className={stepClass}>
                {index + 1}. {step.label}
              </NavLink>
            </li>
          ))}
        </ol>
      </nav>

      {notices.length > 0 && (
        <div role="status" className="rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
          <p className="font-medium">Your order has changed since you last looked:</p>
          <ul className="mt-1 list-disc pl-5">
            {notices.map((notice) => (
              <li key={notice}>{notice}</li>
            ))}
          </ul>
          <button
            type="button"
            onClick={() => setNotices([])}
            className="mt-2 rounded-md border border-amber-300 bg-white px-3 py-1 font-medium hover:bg-amber-100"
          >
            Dismiss
          </button>
        </div>
      )}

      {loadError && (
        <div role="alert" className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          <span>{loadError}</span>
          <button
            type="button"
            onClick={reloadOptions}
            className="rounded-md border border-rose-300 bg-white px-3 py-1.5 font-medium hover:bg-rose-100"
          >
            Try again
          </button>
        </div>
      )}

      {context ? (
        <Outlet context={context} />
      ) : (
        !loadError && (
          <p role="status" className="text-sm text-slate-600">
            Loading the menu…
          </p>
        )
      )}
    </div>
  )
}