import { useEffect, useId, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { exportOrderReport, exportOrderTrends, getOrderReport, getOrderTrends } from '../../features/management/api'
import { AuditPanel } from '../../features/management/AuditPanel'
import { ExportButton } from '../../features/management/ExportButton'
import { useDisplayFormat } from '../../features/management/displayFormat'
import { describeReportError, pageNumbers, shortMonth, shortWeekday, slashed, todayIn } from '../../features/management/reports'
import { TrendChart } from '../../features/management/TrendChart'
import type { OrderReport, OrderReportRow, OrderTrends, ReportStatusFilter, TrendPoint } from '../../features/management/types'
import { paymentText } from '../../features/ordering/history'
import { toTabSeparated } from '../../lib/download'

type Tab = 'orders' | 'trends' | 'audit'

type Params = Record<'tab' | 'from' | 'to' | 'q' | 'status' | 'page', string | null>

const STATUS_FILTERS: { value: ReportStatusFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'paid', label: 'Paid' },
  { value: 'unpaid', label: 'Unpaid' },
  { value: 'cancelled', label: 'Cancelled' },
]

const BADGE_CLASS: Record<OrderReportRow['paymentStatus'], string> = {
  Paid: 'bg-emerald-50 text-emerald-800 ring-emerald-200',
  Unpaid: 'bg-amber-50 text-amber-800 ring-amber-200',
  Cancelled: 'bg-rose-50 text-rose-800 ring-rose-200',
}

const COPY_HEADINGS = ['Date', 'Time', 'Order', 'Milkshake', 'Flavour', 'Topping', 'Consistency', 'Pickup', 'Payment status']

const INPUT_CLASS = 'mt-1 rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-brand-600 focus:outline-none'
const BUTTON_CLASS = 'rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50'
const SECONDARY_CLASS =
  'rounded-lg px-4 py-2 text-sm font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50'

function toggleClass(active: boolean) {
  return `rounded-full px-3 py-1 text-sm font-medium ring-1 ${
    active ? 'bg-brand-600 text-white ring-brand-600' : 'bg-white text-slate-700 ring-slate-300 hover:bg-brand-50'
  }`
}

function tabClass(active: boolean) {
  return `-mb-px border-b-2 px-3 py-2 text-sm font-medium ${
    active ? 'border-brand-600 text-brand-700' : 'border-transparent text-slate-600 hover:text-brand-700'
  }`
}

/**
 * /manager/reports — the management dashboard of URS §3.4.4: a date filter (single date or range, in the business time
 * zone) shared by the Orders tab (AC-12.2), the Trends tab (AC-12.3) and the Audit Lookup tab (AC-12.4, AC-12.5). The tab and filters live in the address, so a
 * view can be refreshed, bookmarked or shared with another manager.
 */
export function ReportsPage() {
  const { timeZone } = useDisplayFormat()
  const [params, setParams] = useSearchParams()
  const requested = params.get('tab')
  const tab: Tab = requested === 'trends' || requested === 'audit' ? requested : 'orders'
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const [rangeMode, setRangeMode] = useState(to !== '' && to !== from)
  const tabsId = useId()

  // The Orders tab shows today until a date is chosen; Trends and Audit Lookup show everything ("None").
  const ordersFrom = from || todayIn(timeZone)

  function update(changes: Partial<Params>) {
    setParams(
      (current) => {
        const next = new URLSearchParams(current)
        for (const [key, value] of Object.entries(changes)) {
          if (value === null || value === undefined || value === '') {
            next.delete(key)
          } else {
            next.set(key, value)
          }
        }
        return next
      },
      { replace: true },
    )
  }

  function clear() {
    setRangeMode(false)
    update({ from: null, to: null, q: null, status: null, page: null })
  }

  const shownFrom = tab === 'orders' ? ordersFrom : from

  return (
    <div className="space-y-6">
      <section aria-labelledby="reports-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h1 id="reports-heading" className="text-2xl font-bold tracking-tight">
          Reporting history
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">
          Orders and order volumes by the date they were placed, in the business time zone{timeZone ? ` (${timeZone})` : ''}. Figures come
          from the orders as they were placed, never from today&apos;s prices.
        </p>

        <div className="mt-4 flex flex-wrap items-end gap-3">
          <div role="group" aria-label="Filter by" className="flex gap-1">
            <button
              type="button"
              aria-pressed={!rangeMode}
              onClick={() => {
                setRangeMode(false)
                update({ to: null, page: null })
              }}
              className={toggleClass(!rangeMode)}
            >
              Single date
            </button>
            <button type="button" aria-pressed={rangeMode} onClick={() => setRangeMode(true)} className={toggleClass(rangeMode)}>
              Date range
            </button>
          </div>

          <label className="text-sm font-medium text-slate-700">
            {rangeMode ? 'From' : 'Date'}
            <input
              type="date"
              value={shownFrom}
              onChange={(event) => update(rangeMode ? { from: event.target.value, page: null } : { from: event.target.value, to: null, page: null })}
              className={`${INPUT_CLASS} block`}
            />
          </label>
          {rangeMode && (
            <label className="text-sm font-medium text-slate-700">
              To
              <input
                type="date"
                value={to}
                min={shownFrom || undefined}
                onChange={(event) => update({ from: shownFrom || null, to: event.target.value, page: null })}
                className={`${INPUT_CLASS} block`}
              />
            </label>
          )}
          <button type="button" onClick={clear} className={SECONDARY_CLASS}>
            Clear
          </button>
        </div>
        {tab === 'trends' && !from && <p className="mt-2 text-sm text-slate-600">No date chosen: showing all orders.</p>}
        {tab === 'audit' && !from && <p className="mt-2 text-sm text-slate-600">No date chosen: showing all changes, newest first.</p>}

        <div role="tablist" aria-label="Reports" className="mt-6 flex gap-2 border-b border-slate-200">
          <button
            type="button"
            role="tab"
            id={`${tabsId}-orders`}
            aria-selected={tab === 'orders'}
            aria-controls={`${tabsId}-panel`}
            onClick={() => update({ tab: null, page: null })}
            className={tabClass(tab === 'orders')}
          >
            Orders
          </button>
          <button
            type="button"
            role="tab"
            id={`${tabsId}-trends`}
            aria-selected={tab === 'trends'}
            aria-controls={`${tabsId}-panel`}
            onClick={() => update({ tab: 'trends', page: null })}
            className={tabClass(tab === 'trends')}
          >
            Trends
          </button>
          <button
            type="button"
            role="tab"
            id={`${tabsId}-audit`}
            aria-selected={tab === 'audit'}
            aria-controls={`${tabsId}-panel`}
            onClick={() => update({ tab: 'audit', page: null })}
            className={tabClass(tab === 'audit')}
          >
            Audit Lookup
          </button>
        </div>
      </section>

      <section
        id={`${tabsId}-panel`}
        role="tabpanel"
        aria-labelledby={`${tabsId}-${tab}`}
        className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200"
      >
        {tab === 'orders' ? (
          <OrdersPanel
            from={ordersFrom}
            to={rangeMode ? to : ''}
            q={params.get('q') ?? ''}
            status={(STATUS_FILTERS.find((f) => f.value === params.get('status'))?.value ?? 'all')}
            page={Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)}
            update={update}
          />
        ) : tab === 'trends' ? (
          <TrendsPanel from={from} to={rangeMode ? to : ''} />
        ) : (
          <AuditPanel from={from} to={rangeMode ? to : ''} />
        )}
      </section>
    </div>
  )
}

interface OrdersPanelProps {
  from: string
  to: string
  q: string
  status: ReportStatusFilter
  page: number
  update: (changes: Partial<Params>) => void
}

function OrdersPanel({ from, to, q, status, page, update }: OrdersPanelProps) {
  const [report, setReport] = useState<OrderReport | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState(q)
  const [copied, setCopied] = useState<{ text: string; isError: boolean } | null>(null)
  const [shownQ, setShownQ] = useState(q)
  if (q !== shownQ) {
    // The address changed (e.g. Clear): show the new search text.
    setShownQ(q)
    setSearch(q)
  }

  useEffect(() => {
    const controller = new AbortController()
    getOrderReport({ from, to, status, q, page }, controller.signal).then(
      (loaded) => {
        if (!controller.signal.aborted) {
          setReport(loaded)
          setError(null)
        }
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) {
          setReport(null)
          setError(describeReportError(failure))
        }
      },
    )
    return () => controller.abort()
  }, [from, to, status, q, page])

  function onSearch(event: FormEvent) {
    event.preventDefault()
    update({ q: search.trim() || null, page: null })
  }

  const period = to && to !== from ? `${slashed(from)} to ${slashed(to)}` : slashed(from)

  /** Copy (URS §3.4.4): the rows on screen as tab-separated text, which pastes into a spreadsheet as cells. */
  async function copyPage(rows: OrderReportRow[]) {
    const text = toTabSeparated([
      COPY_HEADINGS,
      ...rows.map((row) => [
        slashed(row.date),
        row.time,
        row.reference,
        String(row.drinkNumber),
        row.flavour,
        row.topping,
        row.consistency,
        slashed(row.pickupLocal.replace('T', ' ')),
        row.paymentStatus === 'Cancelled' ? `Cancelled (${paymentText({ paymentStatus: 'Cancelled', refundStatus: row.refundStatus })})` : row.paymentStatus,
      ]),
    ])
    try {
      if (!navigator.clipboard) {
        throw new Error('No clipboard.')
      }
      await navigator.clipboard.writeText(text)
      setCopied({ text: `Copied ${rows.length === 1 ? '1 row' : `${rows.length} rows`} (this page).`, isError: false })
    } catch {
      setCopied({ text: 'This browser did not allow copying. Use Export instead.', isError: true })
    }
  }

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">
          Orders for {period}
          {report && (
            <span className="ml-2 rounded-full bg-slate-100 px-2.5 py-0.5 align-middle text-xs font-medium text-slate-700">
              {report.totalOrders === 1 ? '1 order' : `${report.totalOrders} orders`} ·{' '}
              {report.totalDrinks === 1 ? '1 milkshake' : `${report.totalDrinks} milkshakes`}
            </span>
          )}
        </h2>
        <form role="search" onSubmit={onSearch} noValidate className="flex items-end gap-2">
          <label className="text-sm font-medium text-slate-700">
            Search
            <input
              type="search"
              value={search}
              maxLength={100}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Flavour, topping, reference…"
              className={`${INPUT_CLASS} block`}
            />
          </label>
          <button type="submit" className={BUTTON_CLASS}>
            Search
          </button>
        </form>
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <button
          type="button"
          aria-label="Copy the rows on this page"
          disabled={!report || report.rows.length === 0}
          onClick={() => report && void copyPage(report.rows)}
          className="rounded-lg px-3 py-1.5 text-sm font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50 disabled:opacity-50"
        >
          Copy
        </button>
        <ExportButton
          label="Export all matching orders as CSV"
          fallbackName="milkshake-orders.csv"
          run={() => exportOrderReport({ from, to, status, q })}
        />
        {copied && (
          <span role={copied.isError ? 'alert' : 'status'} className={`text-sm ${copied.isError ? 'text-rose-700' : 'text-emerald-800'}`}>
            {copied.text}
          </span>
        )}
      </div>

      <div role="group" aria-label="Show orders by payment status" className="mt-4 flex flex-wrap gap-2">
        {STATUS_FILTERS.map((filter) => (
          <button
            key={filter.value}
            type="button"
            aria-pressed={status === filter.value}
            onClick={() => update({ status: filter.value === 'all' ? null : filter.value, page: null })}
            className={toggleClass(status === filter.value)}
          >
            {filter.label}
          </button>
        ))}
      </div>

      {error && (
        <p role="alert" className="mt-4 text-sm text-rose-700">
          {error}
        </p>
      )}
      {!report && !error && (
        <p role="status" className="mt-4 text-sm text-slate-500">
          Loading orders…
        </p>
      )}
      {report && report.rows.length === 0 && <p className="mt-4 text-sm text-slate-500">No orders match.</p>}

      {report && report.rows.length > 0 && (
        <>
          <div className="mt-4 overflow-x-auto">
            <table className="w-full min-w-[48rem] text-left text-sm">
              <caption className="sr-only">
                Orders for {period}, one row per milkshake, newest first. Page {report.page} of {report.totalPages}.
              </caption>
              <thead>
                <tr className="text-slate-600">
                  {['Date', 'Time', 'Order', 'Flavour', 'Topping', 'Consistency', 'Pickup', 'Payment status'].map((heading) => (
                    <th key={heading} scope="col" className="px-2 py-2 font-medium">
                      {heading}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {report.rows.map((row) => (
                  <tr key={`${row.reference}-${row.drinkNumber}`} className="border-t border-slate-100">
                    <td className="px-2 py-2 tabular-nums">{slashed(row.date)}</td>
                    <td className="px-2 py-2 tabular-nums">{row.time}</td>
                    <td className="px-2 py-2">
                      <Link to={`/manager/orders?reference=${encodeURIComponent(row.reference)}`} className="text-brand-700 underline">
                        {row.reference}
                      </Link>
                      {row.drinkNumber > 1 && <span className="ml-1 text-xs text-slate-500">(milkshake {row.drinkNumber})</span>}
                    </td>
                    <td className="px-2 py-2">{row.flavour}</td>
                    <td className="px-2 py-2">{row.topping}</td>
                    <td className="px-2 py-2">{row.consistency}</td>
                    <td className="px-2 py-2 tabular-nums">{slashed(row.pickupLocal.replace('T', ' '))}</td>
                    <td className="px-2 py-2">
                      <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ${BADGE_CLASS[row.paymentStatus]}`}>{row.paymentStatus}</span>
                      {row.paymentStatus === 'Cancelled' && (
                        <span className="ml-1 text-xs text-slate-600">{paymentText({ paymentStatus: 'Cancelled', refundStatus: row.refundStatus })}</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {report.totalPages > 1 && (
            <nav aria-label="Pages" className="mt-4 flex flex-wrap items-center justify-between gap-2">
              <button type="button" disabled={report.page <= 1} onClick={() => update({ page: String(report.page - 1) })} className={SECONDARY_CLASS}>
                ← Previous
              </button>
              <ol className="flex flex-wrap items-center gap-1">
                {pageNumbers(report.page, report.totalPages).map((number, index) =>
                  number === null ? (
                    <li key={`gap-${index}`} aria-hidden="true" className="px-1 text-slate-500">
                      …
                    </li>
                  ) : (
                    <li key={number}>
                      <button
                        type="button"
                        aria-label={`Page ${number}`}
                        aria-current={number === report.page ? 'page' : undefined}
                        onClick={() => update({ page: number === 1 ? null : String(number) })}
                        className={`min-w-8 rounded-md px-2 py-1 text-sm tabular-nums ${
                          number === report.page ? 'bg-brand-600 text-white' : 'text-slate-700 hover:bg-brand-50'
                        }`}
                      >
                        {number}
                      </button>
                    </li>
                  ),
                )}
              </ol>
              <button
                type="button"
                disabled={report.page >= report.totalPages}
                onClick={() => update({ page: String(report.page + 1) })}
                className={SECONDARY_CLASS}
              >
                Next →
              </button>
            </nav>
          )}
        </>
      )}
    </>
  )
}

function TrendsPanel({ from, to }: { from: string; to: string }) {
  const { locale } = useDisplayFormat()
  const [trends, setTrends] = useState<OrderTrends | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    getOrderTrends({ from: from || undefined, to: to || undefined }, controller.signal).then(
      (loaded) => {
        if (!controller.signal.aborted) {
          setTrends(loaded)
          setError(null)
        }
      },
      (failure: unknown) => {
        if (!controller.signal.aborted) {
          setTrends(null)
          setError(describeReportError(failure))
        }
      },
    )
    return () => controller.abort()
  }, [from, to])

  const weekday = (_: TrendPoint, index: number) => shortWeekday(index, locale)
  const month = (point: TrendPoint) => shortMonth(point.label, locale)
  const year = (point: TrendPoint) => point.label

  const period = !from ? 'all orders' : to && to !== from ? `${slashed(from)} to ${slashed(to)}` : slashed(from)

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-lg font-semibold">Trends for {period}</h2>
        <ExportButton
          label="Export the trends as CSV"
          fallbackName="milkshake-trends.csv"
          run={() => exportOrderTrends({ from: from || undefined, to: to || undefined })}
        />
      </div>
      {error && (
        <p role="alert" className="mt-4 text-sm text-rose-700">
          {error}
        </p>
      )}
      {!trends && !error && (
        <p role="status" className="mt-4 text-sm text-slate-500">
          Loading trends…
        </p>
      )}
      {trends && (
        <>
          <p className="mt-1 text-sm text-slate-600">
            {trends.totalOrders === 1 ? '1 order' : `${trends.totalOrders} orders`}, {trends.totalPaidOrders} paid. Light bars are all orders; the
            dark part is the orders that were paid.
          </p>
          <div className="mt-4 grid gap-4 lg:grid-cols-2">
            <TrendChart title="Weekly" description="Orders by day of the week" points={trends.weekdays} label={weekday} />
            <TrendChart title="Monthly" description="Orders by month" points={trends.months} label={month} />
            <TrendChart title="Growth" description="Orders by year" points={trends.years} label={year} />
          </div>
        </>
      )}
    </>
  )
}
