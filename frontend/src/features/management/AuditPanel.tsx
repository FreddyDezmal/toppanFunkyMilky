import { useEffect, useState, type FormEvent } from 'react'
import { exportAuditChanges, exportSecurityEvents, listAuditChanges, listSecurityEvents, type AuditFilter } from './api'
import { useDisplayFormat } from './displayFormat'
import { ExportButton } from './ExportButton'
import { ACTION_LABELS, ACTORS, describeActor, describeRecord, describeValue, EVENT_LABELS, LOOKUP_TABLES, OTHER_TABLES } from './audit'
import { describeReportError, nextDay, startOfDayUtc } from './reports'
import type { AuditChange, AuditPage, SecurityEvent, SecurityEventType } from './types'

type View = 'lookups' | 'changes' | 'security'

const INPUT_CLASS = 'mt-1 block rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-brand-600 focus:outline-none'
const BUTTON_CLASS = 'rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50'
const SECONDARY_CLASS = 'rounded-lg px-4 py-2 text-sm font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:opacity-50'

function toggleClass(active: boolean) {
  return `rounded-full px-3 py-1 text-sm font-medium ring-1 ${
    active ? 'bg-brand-600 text-white ring-brand-600' : 'bg-white text-slate-700 ring-slate-300 hover:bg-brand-50'
  }`
}

function formatWhen(iso: string, locale: string | undefined, timeZone: string | undefined): string {
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium', timeZone }).format(new Date(iso))
  } catch {
    return iso
  }
}

interface AuditPanelProps {
  /** Business-local dates from the shared filter; empty means no date limit. */
  from: string
  to: string
}

/**
 * The Audit Lookup tab (URS §3.4.4, AC-12.4, AC-12.5): lookup changes (who changed what from what to what, and when),
 * every change with the AC-11.4 filters, and sign-in events. Read-only, newest first, a page at a time. The history
 * comes from the M11 audit API, which masks personal data; nothing here can change it.
 */
export function AuditPanel({ from, to }: AuditPanelProps) {
  const [view, setView] = useState<View>('lookups')

  return (
    <>
      <h2 className="text-lg font-semibold">Audit history</h2>
      <p className="mt-1 max-w-2xl text-sm text-slate-600">
        Every change is recorded with who made it, the old and new values, and when. Personal details are hidden. The date filter above
        applies here too.
      </p>
      <div role="group" aria-label="Audit view" className="mt-4 flex flex-wrap gap-2">
        {(
          [
            ['lookups', 'Lookup changes'],
            ['changes', 'All changes'],
            ['security', 'Sign-in events'],
          ] as const
        ).map(([value, label]) => (
          <button key={value} type="button" aria-pressed={view === value} onClick={() => setView(value)} className={toggleClass(view === value)}>
            {label}
          </button>
        ))}
      </div>
      {view === 'security' ? <SecurityEvents from={from} to={to} /> : <Changes key={view} lookupsOnly={view === 'lookups'} from={from} to={to} />}
    </>
  )
}

/** The shared business-local dates as the audit API's UTC instants; to is exclusive (the start of the next day). */
function useRange(from: string, to: string): { from?: string; to?: string } {
  const { timeZone } = useDisplayFormat()
  if (!from) {
    return {}
  }
  return { from: startOfDayUtc(from, timeZone), to: startOfDayUtc(nextDay(to || from), timeZone) }
}

type Source = 'changes' | 'security'

/** One page of either history; the filter arrives as JSON so the effect below depends on plain values only. */
function fetchPage<T>(source: Source, filterJson: string, cursor: string | null, signal?: AbortSignal): Promise<AuditPage<T>> {
  const filter = JSON.parse(filterJson) as AuditFilter & { eventType?: SecurityEventType }
  const page = source === 'changes' ? listAuditChanges(filter, cursor, signal) : listSecurityEvents(filter, cursor, signal)
  return page as unknown as Promise<AuditPage<T>>
}

function usePages<T>(source: Source, filterJson: string) {
  const [items, setItems] = useState<T[] | null>(null)
  const [next, setNext] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchPage<T>(source, filterJson, null, controller.signal).then(
      (page) => {
        if (!controller.signal.aborted) {
          setItems(page.items)
          setNext(page.nextCursor)
          setError(null)
        }
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) {
          setItems(null)
          setNext(null)
          setError(describeReportError(failure))
        }
      },
    )
    return () => controller.abort()
  }, [source, filterJson])

  function more() {
    if (!next || loadingMore) {
      return
    }
    setLoadingMore(true)
    fetchPage<T>(source, filterJson, next).then(
      (page) => {
        setItems((current) => [...(current ?? []), ...page.items])
        setNext(page.nextCursor)
        setError(null)
        setLoadingMore(false)
      },
      (failure: unknown) => {
        setError(describeReportError(failure))
        setLoadingMore(false)
      },
    )
  }

  return { items, next, error, loadingMore, more }
}

function Changes({ lookupsOnly, from, to }: { lookupsOnly: boolean; from: string; to: string }) {
  const { locale, timeZone } = useDisplayFormat()
  const range = useRange(from, to)
  const [draft, setDraft] = useState<AuditFilter>(lookupsOnly ? { entity: 'flavours' } : {})
  const [filter, setFilter] = useState<AuditFilter>(draft)
  const query: AuditFilter = { ...filter, ...range }
  const { items, next, error, loadingMore, more } = usePages<AuditChange>('changes', JSON.stringify(query))

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    setFilter(draft)
  }

  const tables = lookupsOnly ? LOOKUP_TABLES : [...LOOKUP_TABLES, ...OTHER_TABLES]

  return (
    <>
      <form onSubmit={onSubmit} noValidate aria-label={lookupsOnly ? 'Filter lookup changes' : 'Filter all changes'} className="mt-4 flex flex-wrap items-end gap-3">
        <label className="text-sm font-medium text-slate-700">
          {lookupsOnly ? 'Lookup table' : 'Table'}
          <select value={draft.entity ?? ''} onChange={(event) => setDraft({ ...draft, entity: event.target.value || undefined })} className={INPUT_CLASS}>
            {!lookupsOnly && <option value="">All tables</option>}
            {tables.map((table) => (
              <option key={table.value} value={table.value}>
                {table.label}
              </option>
            ))}
          </select>
        </label>
        {!lookupsOnly && (
          <>
            <label className="text-sm font-medium text-slate-700">
              Action
              <select
                value={draft.action ?? ''}
                onChange={(event) => setDraft({ ...draft, action: (event.target.value || undefined) as AuditFilter['action'] })}
                className={INPUT_CLASS}
              >
                <option value="">Any</option>
                {(Object.keys(ACTION_LABELS) as AuditChange['action'][]).map((action) => (
                  <option key={action} value={action}>
                    {ACTION_LABELS[action]}
                  </option>
                ))}
              </select>
            </label>
            <label className="text-sm font-medium text-slate-700">
              Made by
              <select value={draft.actor ?? ''} onChange={(event) => setDraft({ ...draft, actor: event.target.value || undefined })} className={INPUT_CLASS}>
                <option value="">Anyone</option>
                {ACTORS.map((actor) => (
                  <option key={actor.value} value={actor.value}>
                    {actor.label}
                  </option>
                ))}
              </select>
            </label>
          </>
        )}
        <label className="text-sm font-medium text-slate-700">
          User email
          <input
            type="email"
            value={draft.user ?? ''}
            maxLength={256}
            onChange={(event) => setDraft({ ...draft, user: event.target.value || undefined })}
            className={INPUT_CLASS}
          />
        </label>
        <label className="text-sm font-medium text-slate-700">
          Search changes
          <input
            type="search"
            value={draft.q ?? ''}
            maxLength={100}
            onChange={(event) => setDraft({ ...draft, q: event.target.value || undefined })}
            placeholder="Field or value"
            className={INPUT_CLASS}
          />
        </label>
        <button type="submit" className={BUTTON_CLASS}>
          Show
        </button>
      </form>
      <div className="mt-3">
        <ExportButton
          label={lookupsOnly ? 'Export these lookup changes as CSV' : 'Export these changes as CSV'}
          fallbackName="milkshake-audit-changes.csv"
          run={() => exportAuditChanges(query)}
        />
      </div>

      {error && (
        <p role="alert" className="mt-4 text-sm text-rose-700">
          {error}
        </p>
      )}
      {!items && !error && (
        <p role="status" className="mt-4 text-sm text-slate-500">
          Loading changes…
        </p>
      )}
      {items && items.length === 0 && <p className="mt-4 text-sm text-slate-500">No changes match.</p>}
      {items && items.length > 0 && (
        <ol aria-label={lookupsOnly ? 'Lookup changes' : 'Changes'} className="mt-4 divide-y divide-slate-100">
          {items.map((change) => (
            <li key={change.id} className="py-3 text-sm">
              <p className="font-medium">
                {ACTION_LABELS[change.action]} · {describeRecord(change)}
              </p>
              <p className="text-slate-600">
                {formatWhen(change.occurredAt, locale, timeZone)} · by {describeActor(change)}
              </p>
              {change.changes.length > 0 && (
                <table className="mt-2 w-full max-w-3xl text-left text-sm">
                  <caption className="sr-only">Fields changed</caption>
                  <thead>
                    <tr className="text-slate-600">
                      <th scope="col" className="py-1 pr-3 font-medium">
                        Field
                      </th>
                      <th scope="col" className="py-1 pr-3 font-medium">
                        From
                      </th>
                      <th scope="col" className="py-1 font-medium">
                        To
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {change.changes.map((field) => (
                      <tr key={field.field} className="border-t border-slate-100 align-top">
                        <th scope="row" className="py-1 pr-3 font-normal text-slate-700">
                          {field.field}
                        </th>
                        <td className="break-all py-1 pr-3">{describeValue(field.old)}</td>
                        <td className="break-all py-1">{describeValue(field.new)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </li>
          ))}
        </ol>
      )}
      {next && (
        <button type="button" disabled={loadingMore} aria-busy={loadingMore} onClick={more} className={`${SECONDARY_CLASS} mt-4`}>
          {loadingMore ? 'Loading more changes…' : 'Load more changes'}
        </button>
      )}
    </>
  )
}

function SecurityEvents({ from, to }: { from: string; to: string }) {
  const { locale, timeZone } = useDisplayFormat()
  const range = useRange(from, to)
  const [draft, setDraft] = useState<{ user?: string; eventType?: SecurityEventType }>({})
  const [filter, setFilter] = useState(draft)
  const query = { ...filter, ...range }
  const { items, next, error, loadingMore, more } = usePages<SecurityEvent>('security', JSON.stringify(query))

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    setFilter(draft)
  }

  return (
    <>
      <form onSubmit={onSubmit} noValidate aria-label="Filter sign-in events" className="mt-4 flex flex-wrap items-end gap-3">
        <label className="text-sm font-medium text-slate-700">
          Event
          <select
            value={draft.eventType ?? ''}
            onChange={(event) => setDraft({ ...draft, eventType: (event.target.value || undefined) as SecurityEventType | undefined })}
            className={INPUT_CLASS}
          >
            <option value="">Any</option>
            {(Object.keys(EVENT_LABELS) as SecurityEventType[]).map((type) => (
              <option key={type} value={type}>
                {EVENT_LABELS[type]}
              </option>
            ))}
          </select>
        </label>
        <label className="text-sm font-medium text-slate-700">
          User email
          <input
            type="email"
            value={draft.user ?? ''}
            maxLength={256}
            onChange={(event) => setDraft({ ...draft, user: event.target.value || undefined })}
            className={INPUT_CLASS}
          />
        </label>
        <button type="submit" className={BUTTON_CLASS}>
          Show
        </button>
      </form>
      <div className="mt-3">
        <ExportButton label="Export these sign-in events as CSV" fallbackName="milkshake-sign-in-events.csv" run={() => exportSecurityEvents(query)} />
      </div>

      {error && (
        <p role="alert" className="mt-4 text-sm text-rose-700">
          {error}
        </p>
      )}
      {!items && !error && (
        <p role="status" className="mt-4 text-sm text-slate-500">
          Loading sign-in events…
        </p>
      )}
      {items && items.length === 0 && <p className="mt-4 text-sm text-slate-500">No sign-in events match.</p>}
      {items && items.length > 0 && (
        <div className="mt-4 overflow-x-auto">
          <table className="w-full text-left text-sm">
            <caption className="sr-only">Sign-in events, newest first</caption>
            <thead>
              <tr className="text-slate-600">
                {['When', 'Event', 'User', 'Address'].map((heading) => (
                  <th key={heading} scope="col" className="px-2 py-2 font-medium">
                    {heading}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {items.map((event) => (
                <tr key={event.id} className="border-t border-slate-100">
                  <td className="px-2 py-2">{formatWhen(event.occurredAt, locale, timeZone)}</td>
                  <td className="px-2 py-2">{EVENT_LABELS[event.eventType]}</td>
                  <td className="break-all px-2 py-2">{event.user ?? 'Unknown'}</td>
                  <td className="px-2 py-2 tabular-nums">{event.ipAddress ?? 'Not recorded'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {next && (
        <button type="button" disabled={loadingMore} aria-busy={loadingMore} onClick={more} className={`${SECONDARY_CLASS} mt-4`}>
          {loadingMore ? 'Loading more events…' : 'Load more events'}
        </button>
      )}
    </>
  )
}
