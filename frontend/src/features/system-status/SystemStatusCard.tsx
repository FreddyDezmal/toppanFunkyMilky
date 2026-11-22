import { describeApiTarget } from '../../lib/api/config'
import type { HealthStatus } from './types'
import { useSystemHealth } from './useSystemHealth'

const badgeStyles: Record<HealthStatus, string> = {
  Healthy: 'bg-pistachio-tint text-pistachio-deep ring-pistachio-deep/20',
  Degraded: 'bg-warning-tint text-warning ring-warning/20',
  Unhealthy: 'bg-danger-tint text-danger ring-danger/20',
}

function StatusBadge({ status }: { status: HealthStatus }) {
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold ring-1 ring-inset ${badgeStyles[status]}`}>
      {status}
    </span>
  )
}

/** Shows whether the API and its dependencies (database) are reachable. */
export function SystemStatusCard() {
  const { state, refresh } = useSystemHealth()

  return (
    <section aria-labelledby="system-status-heading" className="rounded-card border border-line bg-surface p-5 shadow-soft sm:p-6">
      <div className="flex items-center justify-between gap-4">
        <h2 id="system-status-heading" className="text-lg font-semibold text-chocolate">
          System status
        </h2>
        <button
          type="button"
          onClick={refresh}
          disabled={state.kind === 'loading'}
          className="rounded-lg bg-brand-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {state.kind === 'loading' ? 'Checking…' : 'Refresh'}
        </button>
      </div>

      {state.kind === 'loading' && <p className="mt-4 text-sm text-warm-grey">Contacting the API…</p>}

      {state.kind === 'error' && (
        <div role="alert" className="mt-4 rounded-lg bg-danger-tint p-4 text-sm text-danger">
          <p className="font-semibold">API not reachable</p>
          <p className="mt-1">{state.message}</p>
        </div>
      )}

      {state.kind === 'loaded' && (
        <>
          <dl className="mt-4 divide-y divide-line">
            <div className="flex items-center justify-between py-3">
              <dt className="text-sm font-medium text-chocolate">Overall</dt>
              <dd>
                <StatusBadge status={state.data.status} />
              </dd>
            </div>
            {state.data.checks.map((check) => (
              <div key={check.name} className="flex items-center justify-between gap-4 py-3">
                <dt className="text-sm">
                  <span className="font-medium capitalize text-chocolate">{check.name}</span>
                  {check.description && <span className="block text-xs text-warm-grey">{check.description}</span>}
                </dt>
                <dd className="flex items-center gap-3">
                  <span className="text-xs tabular-nums text-warm-grey">{Math.round(check.durationMs)} ms</span>
                  <StatusBadge status={check.status} />
                </dd>
              </div>
            ))}
          </dl>
          <p className="mt-4 text-xs text-warm-grey">
            Connected to {describeApiTarget()} · checked {new Date(state.data.checkedAtUtc).toLocaleString()}
          </p>
        </>
      )}
    </section>
  )
}