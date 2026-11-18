import { describeApiTarget } from '../../lib/api/config'
import type { HealthStatus } from './types'
import { useSystemHealth } from './useSystemHealth'

const badgeStyles: Record<HealthStatus, string> = {
  Healthy: 'bg-emerald-100 text-emerald-800 ring-emerald-600/20',
  Degraded: 'bg-amber-100 text-amber-800 ring-amber-600/20',
  Unhealthy: 'bg-rose-100 text-rose-800 ring-rose-600/20',
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
    <section aria-labelledby="system-status-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <div className="flex items-center justify-between gap-4">
        <h2 id="system-status-heading" className="text-lg font-semibold text-slate-900">
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

      {state.kind === 'loading' && <p className="mt-4 text-sm text-slate-500">Contacting the API…</p>}

      {state.kind === 'error' && (
        <div role="alert" className="mt-4 rounded-lg bg-rose-50 p-4 text-sm text-rose-800">
          <p className="font-semibold">API not reachable</p>
          <p className="mt-1">{state.message}</p>
        </div>
      )}

      {state.kind === 'loaded' && (
        <>
          <dl className="mt-4 divide-y divide-slate-100">
            <div className="flex items-center justify-between py-3">
              <dt className="text-sm font-medium text-slate-900">Overall</dt>
              <dd>
                <StatusBadge status={state.data.status} />
              </dd>
            </div>
            {state.data.checks.map((check) => (
              <div key={check.name} className="flex items-center justify-between gap-4 py-3">
                <dt className="text-sm">
                  <span className="font-medium capitalize text-slate-900">{check.name}</span>
                  {check.description && <span className="block text-xs text-slate-500">{check.description}</span>}
                </dt>
                <dd className="flex items-center gap-3">
                  <span className="text-xs tabular-nums text-slate-400">{Math.round(check.durationMs)} ms</span>
                  <StatusBadge status={check.status} />
                </dd>
              </div>
            ))}
          </dl>
          <p className="mt-4 text-xs text-slate-500">
            Connected to {describeApiTarget()} · checked {new Date(state.data.checkedAtUtc).toLocaleString()}
          </p>
        </>
      )}
    </section>
  )
}