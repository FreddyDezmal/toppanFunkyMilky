import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { cancelScheduledPrice, getPriceHistory } from '../../features/management/api'
import { useDisplayFormat } from '../../features/management/displayFormat'
import { CONFLICT_MESSAGE, isConflict } from '../../features/management/errors'
import { PriceForm } from '../../features/management/PriceForm'
import {
  CATALOG_LABELS,
  isCatalogKind,
  type CatalogKind,
  type PriceHistory,
  type PricePeriod,
} from '../../features/management/types'
import { describeError } from '../../lib/api/describeError'
import { ApiError } from '../../lib/api/httpClient'
import { formatDateTime, formatMoney } from '../../lib/format/money'
import { NotFoundPage } from '../NotFoundPage'

/** /manager/catalog/:kind/:id/prices — an item's price history and price changes. */
export function PricesPage() {
  const { kind, id } = useParams()
  const itemId = Number(id)
  if (!isCatalogKind(kind) || !Number.isInteger(itemId) || itemId <= 0) {
    return <NotFoundPage />
  }
  return <PriceManager key={`${kind}-${itemId}`} kind={kind} itemId={itemId} />
}

type Problem = { message: string; canReload: boolean }

const STATUS_STYLES: Record<PricePeriod['status'], string> = {
  Past: 'bg-line text-chocolate',
  Current: 'bg-pistachio-tint text-pistachio-deep',
  Scheduled: 'bg-strawberry-tint text-chocolate',
}

function PriceManager({ kind, itemId }: { kind: CatalogKind; itemId: number }) {
  const { title, singular } = CATALOG_LABELS[kind]
  const { locale } = useDisplayFormat()
  const [history, setHistory] = useState<PriceHistory | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadCount, setReloadCount] = useState(0)
  const [notice, setNotice] = useState<string | null>(null)
  const [problem, setProblem] = useState<Problem | null>(null)
  const [confirming, setConfirming] = useState<PricePeriod | null>(null)
  const [cancelling, setCancelling] = useState(false)
  const cancellingRef = useRef(false)

  useEffect(() => {
    const controller = new AbortController()
    getPriceHistory(kind, itemId, controller.signal)
      .then(setHistory)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(error instanceof ApiError && error.status === 404 ? `This ${singular} was not found.` : describeError(error))
        }
      })
    return () => controller.abort()
  }, [kind, itemId, singular, reloadCount])

  const money = (amount: number, currencyCode: string) => formatMoney(amount, currencyCode, locale)

  function reload() {
    setProblem(null)
    setLoadError(null)
    setReloadCount((count) => count + 1)
  }

  function showConflict() {
    setNotice(null)
    setConfirming(null)
    setProblem({ message: CONFLICT_MESSAGE, canReload: true })
  }

  function handleScheduled(updated: PriceHistory, immediate: boolean) {
    setHistory(updated)
    setProblem(null)
    const latest = updated.prices.at(0)
    setNotice(
      immediate || !latest
        ? 'The new price applies from now.'
        : `The new price of ${money(latest.amount, latest.currencyCode)} will apply from ${formatDateTime(latest.effectiveFrom, locale)}.`,
    )
  }

  async function cancelPrice(period: PricePeriod) {
    if (!history || cancellingRef.current) {
      return
    }
    cancellingRef.current = true
    setCancelling(true)
    setNotice(null)
    setProblem(null)
    try {
      const updated = await cancelScheduledPrice(kind, itemId, period.id, history.item.version)
      setHistory(updated)
      setNotice(`The scheduled price of ${money(period.amount, period.currencyCode)} was cancelled. The current price continues.`)
    } catch (error) {
      if (isConflict(error)) {
        showConflict()
      } else {
        // For example, the price took effect a moment ago and can no longer be cancelled.
        setProblem({ message: describeError(error), canReload: true })
      }
    } finally {
      cancellingRef.current = false
      setCancelling(false)
      setConfirming(null)
    }
  }

  return (
    <section aria-labelledby="prices-heading" className="rounded-card border border-line bg-surface p-5 shadow-soft sm:p-6">
      <Link to={`/manager/catalog/${kind}`} className="text-sm font-medium text-brand-700 hover:underline">
        ← Back to {title.toLowerCase()}
      </Link>
      <h1 id="prices-heading" className="mt-2 text-2xl font-semibold text-chocolate">
        {history ? `Prices: ${history.item.name}` : 'Prices'}
      </h1>
      <p className="mt-1 text-sm text-warm-grey">
        Prices that have taken effect cannot be changed, so every order keeps the price it was placed at. A new price can
        start now or later, and a future price can be cancelled before it starts.
      </p>

      {notice && (
        <p role="status" className="mt-4 rounded-lg bg-pistachio-tint p-3 text-sm text-pistachio-deep">
          {notice}
        </p>
      )}
      {problem && (
        <div role="alert" className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-lg bg-warning-tint p-3 text-sm text-warning">
          <span>{problem.message}</span>
          {problem.canReload && (
            <button
              type="button"
              onClick={reload}
              className="rounded-md border border-warning/30 bg-white px-3 py-1.5 font-medium hover:bg-warning-tint"
            >
              Reload
            </button>
          )}
        </div>
      )}

      {loadError ? (
        <p role="alert" className="mt-6 rounded-lg bg-danger-tint p-3 text-sm text-danger">
          {loadError}
        </p>
      ) : history === null ? (
        <p role="status" className="mt-6 text-sm text-warm-grey">
          Loading prices…
        </p>
      ) : (
        <>
          <div className="mt-6">
            {/* key: a fresh, empty form after every change (the item version changes too). */}
            <PriceForm
              key={history.item.version}
              kind={kind}
              history={history}
              onScheduled={handleScheduled}
              onConflict={showConflict}
            />
          </div>

          <div className="mt-6 overflow-x-auto">
            <table className="stack-table min-w-full text-left text-sm">
              <caption className="mb-2 text-left text-base font-semibold text-chocolate">Price history, newest first</caption>
              <thead className="text-xs uppercase tracking-wide text-warm-grey">
                <tr>
                  <th scope="col" className="py-2 pr-4">Price</th>
                  <th scope="col" className="py-2 pr-4">From</th>
                  <th scope="col" className="py-2 pr-4">Until</th>
                  <th scope="col" className="py-2 pr-4">Status</th>
                  <th scope="col" className="py-2">
                    <span className="sr-only">Actions</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {history.prices.map((period) => (
                  <tr key={period.id} className="border-t border-line">
                    <td data-label="Price" className="py-2 pr-4 font-medium">{money(period.amount, period.currencyCode)}</td>
                    <td data-label="From" className="py-2 pr-4">{formatDateTime(period.effectiveFrom, locale)}</td>
                    <td data-label="Until" className="py-2 pr-4">{period.effectiveTo ? formatDateTime(period.effectiveTo, locale) : '—'}</td>
                    <td data-label="Status" className="py-2 pr-4">
                      <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_STYLES[period.status]}`}>
                        {period.status}
                      </span>
                    </td>
                    <td className="py-2 text-right">
                      {period.canCancel && (
                        <button
                          type="button"
                          aria-label={`Cancel the scheduled price of ${money(period.amount, period.currencyCode)}`}
                          onClick={() => setConfirming(period)}
                          disabled={cancelling}
                          className="rounded-md px-2 py-1 font-medium text-danger hover:bg-danger-tint disabled:opacity-40"
                        >
                          Cancel
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      <ConfirmDialog
        open={confirming !== null}
        title="Cancel this scheduled price?"
        message={
          confirming
            ? `${money(confirming.amount, confirming.currencyCode)} from ${formatDateTime(confirming.effectiveFrom, locale)} will not take effect. The current price continues unchanged.`
            : ''
        }
        confirmLabel="Cancel scheduled price"
        cancelLabel="Keep it"
        busy={cancelling}
        onConfirm={() => {
          if (confirming) {
            void cancelPrice(confirming)
          }
        }}
        onCancel={() => setConfirming(null)}
      />
    </section>
  )
}