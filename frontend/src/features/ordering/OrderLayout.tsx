import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { Card } from '../../components/ui/Card'
import { Icon } from '../../components/ui/Icon'
import { Skeleton } from '../../components/ui/Skeleton'
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
    <div className="space-y-8">
      <OrderSteps />

      {notices.length > 0 && (
        <Alert tone="warning" role="status" title="Your order has changed since you last looked:">
          <ul className="list-disc space-y-0.5 pl-5">
            {notices.map((notice) => (
              <li key={notice}>{notice}</li>
            ))}
          </ul>
          <Button variant="secondary" size="sm" onClick={() => setNotices([])} className="mt-3">
            Dismiss
          </Button>
        </Alert>
      )}

      {loadError && (
        <Alert tone="danger" role="alert">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <span>{loadError}</span>
            <Button variant="secondary" size="sm" onClick={reloadOptions}>
              Try again
            </Button>
          </div>
        </Alert>
      )}

      {context ? (
        <Outlet context={context} />
      ) : (
        !loadError && (
          <div>
            <p role="status" className="sr-only">
              Loading the menu…
            </p>
            <Skeleton className="h-10 w-72 max-w-full" />
            <Card className="mt-6 grid grid-cols-2 gap-3 lg:grid-cols-3">
              {[0, 1, 2, 3, 4, 5].map((key) => (
                <Skeleton key={key} className="h-16 rounded-control" />
              ))}
            </Card>
          </div>
        )
      )}
    </div>
  )
}

/**
 * Where the patron is in the four steps. Every step is a link (as before), shown as numbered circles with a check for
 * the steps before the current one; phones show "Step n of 4: name" with a progress bar instead.
 */
function OrderSteps() {
  const { pathname } = useLocation()
  const current = Math.max(
    0,
    STEPS.findIndex((step) => (step.end ? pathname === step.to || pathname === `${step.to}/` : pathname.startsWith(step.to))),
  )
  const currentStep = STEPS[current]

  return (
    <nav aria-label="Order steps">
      <p className="text-sm font-semibold text-chocolate sm:hidden">
        Step {current + 1} of {STEPS.length}: {currentStep?.label}
      </p>
      <div aria-hidden="true" className="mt-2 flex gap-1.5 sm:hidden">
        {STEPS.map((step, index) => (
          <span key={step.to} className={`h-1.5 flex-1 rounded-full ${index <= current ? 'bg-raspberry' : 'bg-line'}`} />
        ))}
      </div>
      <ol className="hidden flex-wrap items-center gap-2 sm:flex">
        {STEPS.map((step, index) => {
          const done = index < current
          const active = index === current
          return (
            <li key={step.to} className="flex items-center gap-2">
              <NavLink
                to={step.to}
                end={step.end}
                aria-current={active ? 'step' : undefined}
                className="flex min-h-10 items-center gap-2 rounded-full pr-3 text-sm hover:bg-strawberry-wash"
              >
                <span
                  aria-hidden="true"
                  className={`flex size-8 items-center justify-center rounded-full font-bold ${
                    done
                      ? 'bg-raspberry text-white'
                      : active
                        ? 'border-2 border-raspberry bg-surface text-raspberry'
                        : 'border border-line-strong bg-surface text-warm-grey'
                  }`}
                >
                  {done ? <Icon name="check" className="size-4" /> : index + 1}
                </span>
                <span className={active ? 'font-semibold text-chocolate' : 'text-warm-grey'}>
                  <span className="sr-only">Step {index + 1}: </span>
                  {step.label}
                  {done && <span className="sr-only"> (done)</span>}
                </span>
              </NavLink>
              {index < STEPS.length - 1 && <span aria-hidden="true" className="h-px w-6 bg-line-strong lg:w-10" />}
            </li>
          )
        })}
      </ol>
    </nav>
  )
}
