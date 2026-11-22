import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Link } from 'react-router'
import { Card } from '../components/ui/Card'
import { Icon, type IconName } from '../components/ui/Icon'
import { PageHeader } from '../components/ui/PageHeader'
import { Skeleton } from '../components/ui/Skeleton'
import { getOrderReport, listEmails, listRefundsNeedingAttention } from '../features/management/api'
import { useDisplayFormat } from '../features/management/displayFormat'
import { slashed, todayIn } from '../features/management/reports'
import { fetchManagerPing } from '../features/manager/api'
import { SystemStatusCard } from '../features/system-status/SystemStatusCard'
import { describeError } from '../lib/api/describeError'

type PingState = { kind: 'loading' } | { kind: 'ok'; message: string } | { kind: 'error'; message: string }
type Loaded<T> = { kind: 'loading' } | { kind: 'ready'; value: T } | { kind: 'error' }

/** Loads one overview figure; a failure only affects its own card. */
function useOverviewValue<T>(load: (signal: AbortSignal) => Promise<T>, key: string): Loaded<T> {
  const [state, setState] = useState<{ key: string; value: Loaded<T> }>({ key, value: { kind: 'loading' } })
  // The latest loader; `key` says when the request really changes.
  const loadRef = useRef(load)
  useEffect(() => {
    loadRef.current = load
  })

  useEffect(() => {
    const controller = new AbortController()
    loadRef.current(controller.signal).then(
      (value) => setState({ key, value: { kind: 'ready', value } }),
      () => {
        if (!controller.signal.aborted) {
          setState({ key, value: { kind: 'error' } })
        }
      },
    )
    return () => controller.abort()
  }, [key])

  return state.key === key ? state.value : { kind: 'loading' }
}

/**
 * Management overview: what needs attention today, taken only from figures the API already provides (the orders
 * report for today, the refunds needing attention, the email delivery counts) plus the system status. Nothing is
 * calculated here; each card links to the section with the details.
 */
export function ManagerPage() {
  const [ping, setPing] = useState<PingState>({ kind: 'loading' })
  const { timeZone } = useDisplayFormat()
  const today = todayIn(timeZone)

  useEffect(() => {
    const controller = new AbortController()
    fetchManagerPing(controller.signal)
      .then((body) => setPing({ kind: 'ok', message: body.message }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setPing({ kind: 'error', message: describeError(error) })
        }
      })
    return () => controller.abort()
  }, [])

  const report = useOverviewValue((signal) => getOrderReport({ from: today }, signal), `report:${today}`)
  const refunds = useOverviewValue((signal) => listRefundsNeedingAttention(signal), 'refunds')
  const emails = useOverviewValue((signal) => listEmails({}, signal), 'emails')

  return (
    <div className="space-y-6">
      <PageHeader title="Management" description="Choose a section to manage the menu, find orders or see reports." />
      <p className="text-sm text-warm-grey" role="status">
        {ping.kind === 'loading' ? 'Checking manager access…' : ping.message}
      </p>

      <div className="grid gap-4 md:grid-cols-3">
        <OverviewCard title="Today’s orders" icon="receipt" link={{ to: '/manager/reports', label: 'Open reports' }} state={report}>
          {(value) => (
            <>
              <Figure value={value.totalOrders} unit={value.totalOrders === 1 ? 'order' : 'orders'} />
              <p className="mt-1 text-sm text-warm-grey">
                {value.totalDrinks === 1 ? '1 milkshake' : `${value.totalDrinks} milkshakes`} placed on {slashed(today)}
              </p>
            </>
          )}
        </OverviewCard>

        <OverviewCard title="Refunds needing attention" icon="alert" link={{ to: '/manager/orders', label: 'Open orders' }} state={refunds}>
          {(value) => (
            <>
              <Figure value={value.length} unit={value.length === 1 ? 'refund' : 'refunds'} />
              <p className="mt-1 text-sm text-warm-grey">
                {value.length === 0 ? 'Nothing needs attention.' : 'Handle them in the Paystack dashboard and contact the patron.'}
              </p>
            </>
          )}
        </OverviewCard>

        <OverviewCard title="Emails" icon="mail" link={{ to: '/manager/emails', label: 'Open emails' }} state={emails}>
          {(value) => (
            <>
              <Figure value={value.counts.Failed} unit="failed" />
              <p className="mt-1 text-sm text-warm-grey">
                {value.counts.Pending} waiting · {value.counts.Sent} sent
              </p>
            </>
          )}
        </OverviewCard>
      </div>

      {/* Moved from the public home page (redesign D-R7). */}
      <SystemStatusCard />
    </div>
  )
}

function Figure({ value, unit }: { value: number; unit: string }) {
  return (
    <p className="mt-3 flex items-baseline gap-2">
      <span className="font-display text-4xl font-semibold text-chocolate tabular-nums">{value}</span>
      <span className="text-sm font-semibold text-warm-grey">{unit}</span>
    </p>
  )
}

function OverviewCard<T>({
  title,
  icon,
  link,
  state,
  children,
}: {
  title: string
  icon: IconName
  link: { to: string; label: string }
  state: Loaded<T>
  children: (value: T) => ReactNode
}) {
  return (
    <Card as="section" aria-label={title} padding="md" className="flex flex-col">
      <h2 className="flex items-center gap-2 text-sm font-semibold text-chocolate">
        <span className="flex size-8 items-center justify-center rounded-full bg-strawberry-tint text-raspberry">
          <Icon name={icon} className="size-4" />
        </span>
        {title}
      </h2>
      <div className="flex-1" aria-busy={state.kind === 'loading'}>
        {state.kind === 'loading' && (
          <div className="mt-3 space-y-2">
            <Skeleton className="h-9 w-20" />
            <Skeleton className="h-4 w-40" />
          </div>
        )}
        {state.kind === 'error' && <p className="mt-3 text-sm text-warm-grey">Could not be loaded just now.</p>}
        {state.kind === 'ready' && children(state.value)}
      </div>
      <Link to={link.to} className="mt-4 inline-flex min-h-10 items-center gap-1 text-sm font-semibold text-raspberry hover:underline">
        {link.label}
        <Icon name="chevron-right" className="size-4" />
      </Link>
    </Card>
  )
}
