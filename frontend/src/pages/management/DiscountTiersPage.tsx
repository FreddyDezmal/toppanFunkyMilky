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
    <section aria-labelledby="tiers-heading" className="rounded-card border border-line bg-surface p-5 shadow-soft sm:p-6">
      <h1 id="tiers-heading" className="text-2xl font-semibold text-chocolate">
        Discount tiers
      </h1>
      <p className="mt-1 text-sm text-warm-grey">
        Frequent-customer discounts. Each tier needs a number of earlier paid orders and a number of drinks in the order.
      </p>

      {blockedReason && (
        <div id={BLOCKED_NOTICE_ID} className="mt-4 rounded-lg bg-strawberry-wash p-4 text-sm text-chocolate">
          <p className="font-medium">Discounts are switched off for now</p>
          <p className="mt-1">{blockedReason}</p>
          <p className="mt-1">You can prepare the thresholds and rates below; they have no effect until activation is allowed.</p>
        </div>
      )}

      {notice && (
        <p role="status" className="mt-4 rounded-lg bg-pistachio-tint p-3 text-sm text-pistachio-deep">
          {notice}
        </p>
      )}
      {problem && (
        <div role="alert" className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-lg bg-warning-tint p-3 text-sm text-warning">
          <span>{problem}</span>
          <button
            type="button"
            onClick={reload}
            className="rounded-md border border-warning/30 bg-white px-3 py-1.5 font-medium hover:bg-warning-tint"
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
          <p role="alert" className="rounded-lg bg-danger-tint p-3 text-sm text-danger">
            {loadError}
          </p>
        ) : tiers === null ? (
          <p role="status" className="text-sm text-warm-grey">
            Loading discount tiers…
          </p>
        ) : (
          <table className="stack-table min-w-full text-left text-sm">
            <caption className="sr-only">Discount tiers</caption>
            <thead className="text-xs uppercase tracking-wide text-warm-grey">
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
                <tr key={tier.tierNumber} className="border-t border-line">
                  <th scope="row" data-label="Tier" className="py-2 pr-4 font-medium text-chocolate">
                    Tier {tier.tierNumber}
                  </th>
                  <td data-label="Paid orders needed" className="py-2 pr-4">{tier.minPreviousOrders}</td>
                  <td data-label="Drinks per order" className="py-2 pr-4">{tier.minDrinksPerOrder}</td>
                  <td data-label="Discount" className="py-2 pr-4">{formatPercent(tier.discountRate, locale)}</td>
                  <td data-label="Status" className="py-2 pr-4">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        tier.isActive ? 'bg-pistachio-tint text-pistachio-deep' : 'bg-line text-chocolate'
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
                      className="rounded-md px-2 py-1 font-medium text-raspberry-dark hover:bg-strawberry-wash disabled:opacity-40"
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
                        className="rounded-md px-2 py-1 font-medium text-pistachio-deep hover:bg-pistachio-tint disabled:cursor-not-allowed disabled:opacity-40"
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