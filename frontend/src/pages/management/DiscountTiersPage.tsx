import { useEffect, useState } from 'react'
import { listDiscountTiers } from '../../features/management/api'
import { DiscountTierForm } from '../../features/management/DiscountTierForm'
import { useDisplayFormat } from '../../features/management/displayFormat'
import { CONFLICT_MESSAGE } from '../../features/management/errors'
import type { DiscountTier } from '../../features/management/types'
import { describeError } from '../../lib/api/describeError'
import { formatPercent } from '../../lib/format/money'

const BLOCKED_NOTICE_ID = 'activation-blocked'

/** /manager/discount-tiers — frequent-customer discount tiers (URS §3.4.5). */
export function DiscountTiersPage() {
  const { locale } = useDisplayFormat()
  const [tiers, setTiers] = useState<DiscountTier[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadCount, setReloadCount] = useState(0)
  const [editing, setEditing] = useState<DiscountTier | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [problem, setProblem] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listDiscountTiers(controller.signal)
      .then(setTiers)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describeError(error))
        }
      })
    return () => controller.abort()
  }, [reloadCount])

  function reload() {
    setProblem(null)
    setLoadError(null)
    setReloadCount((count) => count + 1)
  }

  function handleSaved(saved: DiscountTier) {
    setTiers((current) => current?.map((tier) => (tier.tierNumber === saved.tierNumber ? saved : tier)) ?? null)
    setEditing(null)
    setProblem(null)
    setNotice(`Saved tier ${saved.tierNumber}.`)
  }

  function showConflict() {
    setEditing(null)
    setNotice(null)
    setProblem(CONFLICT_MESSAGE)
  }

  const blockedReason = tiers?.find((tier) => tier.activationBlockedReason)?.activationBlockedReason ?? null

  return (
    <section aria-labelledby="tiers-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
      <h1 id="tiers-heading" className="text-2xl font-bold tracking-tight">
        Discount tiers
      </h1>
      <p className="mt-1 text-sm text-slate-600">
        Frequent-customer discounts. Each tier needs a number of earlier paid orders and a number of drinks in the order.
      </p>

      {blockedReason && (
        <div id={BLOCKED_NOTICE_ID} className="mt-4 rounded-lg bg-sky-50 p-4 text-sm text-sky-900">
          <p className="font-medium">Discounts are switched off for now</p>
          <p className="mt-1">{blockedReason}</p>
          <p className="mt-1">You can prepare the thresholds and rates below; they have no effect until activation is allowed.</p>
        </div>
      )}

      {notice && (
        <p role="status" className="mt-4 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-800">
          {notice}
        </p>
      )}
      {problem && (
        <div role="alert" className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-lg bg-amber-50 p-3 text-sm text-amber-900">
          <span>{problem}</span>
          <button
            type="button"
            onClick={reload}
            className="rounded-md border border-amber-300 bg-white px-3 py-1.5 font-medium hover:bg-amber-100"
          >
            Reload
          </button>
        </div>
      )}

      {editing && (
        <div className="mt-6">
          <DiscountTierForm
            key={editing.tierNumber}
            tier={editing}
            onSaved={handleSaved}
            onCancel={() => setEditing(null)}
            onConflict={showConflict}
          />
        </div>
      )}

      <div className="mt-6 overflow-x-auto">
        {loadError ? (
          <p role="alert" className="rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
            {loadError}
          </p>
        ) : tiers === null ? (
          <p role="status" className="text-sm text-slate-600">
            Loading discount tiers…
          </p>
        ) : (
          <table className="min-w-full text-left text-sm">
            <caption className="sr-only">Discount tiers</caption>
            <thead className="text-xs uppercase tracking-wide text-slate-600">
              <tr>
                <th scope="col" className="py-2 pr-4">Tier</th>
                <th scope="col" className="py-2 pr-4">Paid orders needed</th>
                <th scope="col" className="py-2 pr-4">Drinks per order</th>
                <th scope="col" className="py-2 pr-4">Discount</th>
                <th scope="col" className="py-2 pr-4">Status</th>
                <th scope="col" className="py-2">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {tiers.map((tier) => (
                <tr key={tier.tierNumber} className="border-t border-slate-100">
                  <th scope="row" className="py-2 pr-4 font-medium text-slate-900">
                    Tier {tier.tierNumber}
                  </th>
                  <td className="py-2 pr-4">{tier.minPreviousOrders}</td>
                  <td className="py-2 pr-4">{tier.minDrinksPerOrder}</td>
                  <td className="py-2 pr-4">{formatPercent(tier.discountRate, locale)}</td>
                  <td className="py-2 pr-4">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        tier.isActive ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-200 text-slate-700'
                      }`}
                    >
                      {tier.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="space-x-2 whitespace-nowrap py-2 text-right">
                    <button
                      type="button"
                      aria-label={`Edit tier ${tier.tierNumber}`}
                      onClick={() => {
                        setNotice(null)
                        setEditing(tier)
                      }}
                      disabled={editing !== null}
                      className="rounded-md px-2 py-1 font-medium text-brand-700 hover:bg-brand-50 disabled:opacity-40"
                    >
                      Edit
                    </button>
                    {!tier.isActive && (
                      <button
                        type="button"
                        aria-label={`Activate tier ${tier.tierNumber}`}
                        aria-describedby={tier.activationBlockedReason ? BLOCKED_NOTICE_ID : undefined}
                        disabled={!tier.canActivate}
                        title={tier.activationBlockedReason ?? undefined}
                        className="rounded-md px-2 py-1 font-medium text-emerald-700 hover:bg-emerald-50 disabled:cursor-not-allowed disabled:opacity-40"
                      >
                        Activate
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </section>
  )
}