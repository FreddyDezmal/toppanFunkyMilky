import { useEffect, useRef, useState } from 'react'
import { ConfirmDialog } from '../../components/ui/ConfirmDialog'
import { listRestaurants, setRestaurantActive } from '../../features/management/api'
import { CONFLICT_MESSAGE, isConflict } from '../../features/management/errors'
import { RestaurantForm } from '../../features/management/RestaurantForm'
import type { Restaurant, StatusFilter } from '../../features/management/types'
import { describeError } from '../../lib/api/describeError'

type Problem = { message: string; canReload: boolean }

/** /manager/restaurants — pickup locations. Deactivated, never deleted. */
export function RestaurantsPage() {
  const [status, setStatus] = useState<StatusFilter>('all')
  const [restaurants, setRestaurants] = useState<Restaurant[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [reloadCount, setReloadCount] = useState(0)
  const [editing, setEditing] = useState<Restaurant | 'new' | null>(null)
  const [confirming, setConfirming] = useState<Restaurant | null>(null)
  const [busyId, setBusyId] = useState<number | null>(null)
  const busyRef = useRef(false)
  const [notice, setNotice] = useState<string | null>(null)
  const [problem, setProblem] = useState<Problem | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listRestaurants(status, controller.signal)
      .then(setRestaurants)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describeError(error))
        }
      })
    return () => controller.abort()
  }, [status, reloadCount])

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

  function handleSaved(saved: Restaurant, wasNew: boolean) {
    setEditing(null)
    setNotice(wasNew ? `Added “${saved.name}”.` : `Saved changes to “${saved.name}”.`)
    reload()
  }

  async function changeActive(restaurant: Restaurant, active: boolean) {
    if (busyRef.current) {
      return
    }
    busyRef.current = true
    setBusyId(restaurant.id)
    setNotice(null)
    setProblem(null)
    try {
      const saved = await setRestaurantActive(restaurant.id, active, restaurant.version)
      setNotice(
        active
          ? `“${saved.name}” is available for new orders again.`
          : `“${saved.name}” can no longer be chosen for new orders. Its history is kept.`,
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

  const actionsLocked = editing !== null || busyId !== null

  return (
    <section aria-labelledby="restaurants-heading" className="rounded-card border border-line bg-surface p-5 shadow-soft sm:p-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 id="restaurants-heading" className="text-2xl font-semibold text-chocolate">
            Restaurants
          </h1>
          <p className="mt-1 text-sm text-warm-grey">Inactive restaurants cannot be chosen for new orders but are kept for history.</p>
        </div>
        <div className="flex items-end gap-3">
          <div>
            <label htmlFor="restaurant-status-filter" className="block text-sm font-medium text-chocolate">
              Show
            </label>
            <select
              id="restaurant-status-filter"
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
            Add restaurant
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
          <RestaurantForm
            restaurant={editing === 'new' ? undefined : editing}
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
        ) : restaurants === null ? (
          <p role="status" className="text-sm text-warm-grey">
            Loading restaurants…
          </p>
        ) : restaurants.length === 0 ? (
          <p className="text-sm text-warm-grey">No restaurants to show.</p>
        ) : (
          <table className="stack-table min-w-full text-left text-sm">
            <caption className="sr-only">Restaurants</caption>
            <thead className="text-xs uppercase tracking-wide text-warm-grey">
              <tr>
                <th scope="col" className="py-2 pr-4">Name</th>
                <th scope="col" className="py-2 pr-4">Address</th>
                <th scope="col" className="py-2 pr-4">Status</th>
                <th scope="col" className="py-2">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {restaurants.map((restaurant) => (
                <tr key={restaurant.id} className="border-t border-line">
                  <th scope="row" data-label="Name" className="py-2 pr-4 font-medium text-chocolate">
                    {restaurant.name}
                  </th>
                  <td data-label="Address" className="py-2 pr-4 text-warm-grey">{restaurant.address ?? '—'}</td>
                  <td data-label="Status" className="py-2 pr-4">
                    <span
                      className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                        restaurant.isActive ? 'bg-pistachio-tint text-pistachio-deep' : 'bg-line text-chocolate'
                      }`}
                    >
                      {restaurant.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td className="space-x-2 whitespace-nowrap py-2 text-right">
                    <button
                      type="button"
                      aria-label={`Edit ${restaurant.name}`}
                      onClick={() => {
                        setNotice(null)
                        setEditing(restaurant)
                      }}
                      disabled={actionsLocked}
                      className="rounded-md px-2 py-1 font-medium text-raspberry-dark hover:bg-strawberry-wash disabled:opacity-40"
                    >
                      Edit
                    </button>
                    {restaurant.isActive ? (
                      <button
                        type="button"
                        aria-label={`Deactivate ${restaurant.name}`}
                        onClick={() => setConfirming(restaurant)}
                        disabled={actionsLocked}
                        className="rounded-md px-2 py-1 font-medium text-danger hover:bg-danger-tint disabled:opacity-40"
                      >
                        Deactivate
                      </button>
                    ) : (
                      <button
                        type="button"
                        aria-label={`Activate ${restaurant.name}`}
                        onClick={() => void changeActive(restaurant, true)}
                        disabled={actionsLocked}
                        className="rounded-md px-2 py-1 font-medium text-pistachio-deep hover:bg-pistachio-tint disabled:opacity-40"
                      >
                        {busyId === restaurant.id ? 'Activating…' : 'Activate'}
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
        message="Patrons will no longer be able to choose it for new orders. Existing orders are kept, and you can reactivate it at any time."
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