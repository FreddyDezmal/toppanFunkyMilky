import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { listCatalogItems, setCatalogItemActive } from '../../features/management/api'
import { CatalogItemForm } from '../../features/management/CatalogItemForm'
import { useDisplayFormat } from '../../features/management/displayFormat'
import { CONFLICT_MESSAGE, isConflict } from '../../features/management/errors'
import {
  CATALOG_LABELS,
  isCatalogKind,
  type CatalogItem,
  type CatalogKind,
  type StatusFilter,
} from '../../features/management/types'
import { describeError } from '../../lib/api/describeError'
import { formatDateTime, formatMoney } from '../../lib/format/money'
import { NotFoundPage } from '../NotFoundPage'

/** /manager/catalog/:kind — flavours, toppings or consistencies. */
export function CatalogPage() {
  const { kind } = useParams()
  if (!isCatalogKind(kind)) {
    return <NotFoundPage />
  }
  // key: switching between lists starts with a fresh page state.
  return <CatalogManager key={kind} kind={kind} />
}

type Problem = { message: string; canReload: boolean }

function CatalogManager({ kind }: { kind: CatalogKind }) {
  const { title, singular } = CATALOG_LABELS[kind]
  const { locale } = useDisplayFormat()
  const [status, setStatus] = useState<StatusFilter>('all')
  const [items, setItems] = useState<CatalogItem[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadCount, setReloadCount] = useState(0)
  const [editing, setEditing] = useState<CatalogItem | 'new' | null>(null)
  const [confirming, setConfirming] = useState<CatalogItem | null>(null)
  const [busyId, setBusyId] = useState<number | null>(null)
  const busyRef = useRef(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [problem, setProblem] = useState<Problem | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listCatalogItems(kind, status, controller.signal)
      .then(setItems)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describeError(error))
        }
      })
    return () => controller.abort()
  }, [kind, status, reloadCount])

  function reload() {
    setProblem(null)
    setLoadError(null)
    setReloadCount((count) => count + 1)
  }

  function showConflict() {
    setEditing(null)
    setConfirming(null)
    setNotice(null)
    setProblem({ message: CONFLICT_MESSAGE, canReload: true })
  }

  function handleSaved(saved: CatalogItem, wasNew: boolean) {
    setEditing(null)
    setNotice(wasNew ? `Added “${saved.name}”.` : `Saved changes to “${saved.name}”.`)
    reload()
  }

  async function changeActive(item: CatalogItem, active: boolean) {
    if (busyRef.current) {
      return
    }
    busyRef.current = true
    setBusyId(item.id)
    setNotice(null)
    setProblem(null)
    try {
      const saved = await setCatalogItemActive(kind, item.id, active, item.version)
      setNotice(
        active
          ? `“${saved.name}” is available for new orders again.`
          : `“${saved.name}” is hidden from new orders. Its history is kept.`,
      )
      reload()
    } catch (error) {
      setProblem(isConflict(error) ? { message: CONFLICT_MESSAGE, canReload: true } : { message: describeError(error), canReload: false })
    } finally {
      busyRef.current = false
      setBusyId(null)
      setConfirming(null)
    }
  }

  function priceText(item: CatalogItem): string {
    return item.currentPrice !== null && item.currencyCode ? formatMoney(item.currentPrice, item.currencyCode, locale) : '—'
  }

  function scheduledText(item: CatalogItem): string {
    return item.scheduledPrice !== null && item.scheduledFrom && item.currencyCode
      ? `${formatMoney(item.scheduledPrice, item.currencyCode, locale)} from ${formatDateTime(item.scheduledFrom, locale)}`
      : '—'
  }

  const actionsLocked = editing !== null || busyId !== null

  return (
    <section aria-labelledby="catalog-heading" className="rounded-card border border-line bg-surface p-5 shadow-soft sm:p-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 id="catalog-heading" className="text-2xl font-semibold text-chocolate">
            {title}
          </h1>
          <p className="mt-1 text-sm text-warm-grey">
            Inactive {title.toLowerCase()} are hidden from new orders but kept for order history.
          </p>
        </div>
        <div className="flex items-end gap-3">
          <div>
            <label htmlFor="status-filter" className="block text-sm font-medium text-chocolate">
              Show
            </label>
            <select
              id="status-filter"
              value={status}
              onChange={(event) => {
                setLoadError(null)
                setStatus(event.target.value as StatusFilter)
              }}
              className="mt-1 rounded-lg border border-line-strong px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-raspberry"
            >
              <option value="all">All</option>
              <option value="active">Active only</option>
              <option value="inactive">Inactive only</option>
            </select>
          </div>
          <button
            type="button"
            onClick={() => {
              setNotice(null)
              setEditing('new')
            }}
            disabled={actionsLocked}
            className="rounded-lg bg-raspberry px-4 py-2 text-sm font-medium text-white hover:bg-raspberry-dark disabled:opacity-50"
          >
            Add {singular}
          </button>
        </div>
      </div>

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

      {editing !== null && (
        <div className="mt-6">
          <CatalogItemForm
            kind={kind}
            item={editing === 'new' ? undefined : editing}
            onSaved={(saved) => handleSaved(saved, editing === 'new')}
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
        ) : items === null ? (
          <p role="status" className="text-sm text-warm-grey">
            Loading {title.toLowerCase()}…
          </p>
        ) : items.length === 0 ? (
          <p className="text-sm text-warm-grey">No {title.toLowerCase()} to show.</p>
        ) : (
          <table className="stack-table min-w-full text-left text-sm">
            <caption className="sr-only">{title}</caption>
            <thead className="text-xs uppercase tracking-wide text-warm-grey">
              <tr>
                <th scope="col" className="py-2 pr-4">Name</th>
                <th scope="col" className="py-2 pr-4">Order</th>
                <th scope="col" className="py-2 pr-4">Current price</th>
                <th scope="col" className="py-2 pr-4">Scheduled change</th>
                <th scope="col" className="py-2 pr-4">Status</th>
                <th scope="col" className="py-2">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id} className="border-t border-line">
                  <th scope="row" data-label="Name" className="py-2 pr-4 font-medium text-chocolate">
                    {item.name}
                  </th>
                  <td data-label="Order" className="py-2 pr-4">{item.displayOrder}</td>
                  <td data-label="Current price" className="py-2 pr-4">{priceText(item)}</td>
                  <td data-label="Scheduled change" className="py-2 pr-4 text-warm-grey">{scheduledText(item)}</td>
                  <td data-label="Status" className="py-2 pr-4">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        item.isActive ? 'bg-pistachio-tint text-pistachio-deep' : 'bg-line text-chocolate'
                      }`}
                    >
                      {item.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="space-x-2 whitespace-nowrap py-2 text-right">
                                        <Link
                      to={`/manager/catalog/${kind}/${item.id}/prices`}
                      aria-label={`Prices for ${item.name}`}
                      className="rounded-md px-2 py-1 font-medium text-raspberry-dark hover:bg-strawberry-wash"
                    >
                      Prices
                    </Link>
                    <button
                      type="button"
                      aria-label={`Edit ${item.name}`}
                      onClick={() => {
                        setNotice(null)
                        setEditing(item)
                      }}
                      disabled={actionsLocked}
                      className="rounded-md px-2 py-1 font-medium text-raspberry-dark hover:bg-strawberry-wash disabled:opacity-40"
                    >
                      Edit
                    </button>
                    {item.isActive ? (
                      <button
                        type="button"
                        aria-label={`Deactivate ${item.name}`}
                        onClick={() => setConfirming(item)}
                        disabled={actionsLocked}
                        className="rounded-md px-2 py-1 font-medium text-danger hover:bg-danger-tint disabled:opacity-40"
                      >
                        Deactivate
                      </button>
                    ) : (
                      <button
                        type="button"
                        aria-label={`Activate ${item.name}`}
                        onClick={() => void changeActive(item, true)}
                        disabled={actionsLocked}
                        className="rounded-md px-2 py-1 font-medium text-pistachio-deep hover:bg-pistachio-tint disabled:opacity-40"
                      >
                        {busyId === item.id ? 'Activating…' : 'Activate'}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <ConfirmDialog
        open={confirming !== null}
        title={`Deactivate ${confirming?.name ?? ''}?`}
        message="Patrons will no longer be able to choose it for new orders. Existing orders and its price history are kept, and you can reactivate it at any time."
        confirmLabel="Deactivate"
        busy={busyId !== null}
        onConfirm={() => {
          if (confirming) {
            void changeActive(confirming, false)
          }
        }}
        onCancel={() => setConfirming(null)}
      />
    </section>
  )
}