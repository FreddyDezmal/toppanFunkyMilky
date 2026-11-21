import { useEffect, useState, type FormEvent } from 'react'
import { listEmails, resendEmail } from '../../features/management/api'
import { useDisplayFormat } from '../../features/management/displayFormat'
import { EMAIL_STATUSES, type EmailDelivery, type EmailDeliveryList, type EmailStatus } from '../../features/management/types'
import { describeError } from '../../lib/api/describeError'

const KIND: Record<EmailDelivery['kind'], string> = {
  OrderConfirmed: 'Order confirmation and receipt',
  CancellationRequested: 'Cancellation request',
  OrderCancelled: 'Order cancelled',
  RefundProcessed: 'Refund completed',
  RefundFailed: 'Refund not completed',
}

const STATUS: Record<EmailStatus, string> = {
  Pending: 'Waiting to send',
  Sending: 'Sending',
  Sent: 'Sent',
  Failed: 'Failed',
  Suppressed: 'Not sent (not on the allow-list)',
}

const STATUS_FILTER: Record<EmailStatus, string> = {
  Pending: 'Waiting',
  Sending: 'Sending',
  Sent: 'Sent',
  Failed: 'Failed',
  Suppressed: 'Suppressed',
}

const ERROR: Record<NonNullable<EmailDelivery['lastError']>, string> = {
  Timeout: 'the email service did not answer in time',
  ProviderUnavailable: 'the email service was unavailable',
  RateLimited: 'the email service asked us to slow down',
  Rejected: 'the email service rejected it, or it could not be prepared',
  InvalidRecipient: 'the address cannot receive email',
}

const STATUS_CLASS: Record<EmailStatus, string> = {
  Pending: 'bg-amber-50 text-amber-800 ring-amber-200',
  Sending: 'bg-sky-50 text-sky-800 ring-sky-200',
  Sent: 'bg-emerald-50 text-emerald-800 ring-emerald-200',
  Failed: 'bg-rose-50 text-rose-800 ring-rose-200',
  Suppressed: 'bg-slate-100 text-slate-700 ring-slate-200',
}

const INPUT_CLASS = 'mt-1 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-brand-600 focus:outline-none'
const BUTTON_CLASS = 'rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50'
const SECONDARY_CLASS = 'rounded-lg px-4 py-2 text-sm font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50'

function formatDateTime(iso: string, locale?: string): string {
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))
  } catch {
    return iso
  }
}

function filterClass(active: boolean) {
  return `rounded-full px-3 py-1 text-sm font-medium ring-1 ${
    active ? 'bg-brand-600 text-white ring-brand-600' : 'bg-white text-slate-700 ring-slate-300 hover:bg-brand-50'
  }`
}

/**
 * /manager/emails — delivery status of the order and refund emails (M9, AC-9.5), and sending a sent or failed one
 * again. Emails are sent automatically and retried while the email service is unavailable; managers never see or
 * edit an email's content.
 */
export function EmailsPage() {
  const { locale } = useDisplayFormat()
  const [status, setStatus] = useState<EmailStatus | undefined>()
  const [orderInput, setOrderInput] = useState('')
  const [order, setOrder] = useState('')
  const [reload, setReload] = useState(0)
  const [list, setList] = useState<EmailDeliveryList | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null)
  const [busyId, setBusyId] = useState<number | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listEmails({ status, order }, controller.signal).then(
      (loaded) => {
        if (!controller.signal.aborted) {
          setList(loaded)
          setLoadError(null)
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setLoadError(describeError(error))
        }
      },
    )
    return () => controller.abort()
  }, [status, order, reload])

  function onFilter(event: FormEvent) {
    event.preventDefault()
    setOrder(orderInput.trim())
  }

  async function resend(email: EmailDelivery) {
    if (busyId !== null) {
      return
    }
    setBusyId(email.id)
    setMessage(null)
    try {
      await resendEmail(email.id)
      setMessage({ text: `The ${KIND[email.kind].toLowerCase()} email for order ${email.orderReference} will be sent again shortly.`, isError: false })
    } catch (error: unknown) {
      setMessage({ text: describeError(error), isError: true })
    } finally {
      setBusyId(null)
      setReload((value) => value + 1)
    }
  }

  const total = list ? EMAIL_STATUSES.reduce((sum, each) => sum + (list.counts[each] ?? 0), 0) : 0

  return (
    <div className="space-y-6">
      <section aria-labelledby="emails-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h1 id="emails-heading" className="text-2xl font-bold tracking-tight">
          Emails
        </h1>
        <p className="mt-1 max-w-2xl text-sm text-slate-600">
          Order and refund emails are sent automatically. If the email service is unavailable they are retried for a few hours, and
          payments, cancellations and refunds are never affected. You can send a sent or failed email again.
        </p>

        <div role="group" aria-label="Show emails by status" className="mt-4 flex flex-wrap gap-2">
          <button type="button" aria-pressed={status === undefined} onClick={() => setStatus(undefined)} className={filterClass(status === undefined)}>
            All{list ? ` (${total})` : ''}
          </button>
          {EMAIL_STATUSES.map((each) => (
            <button key={each} type="button" aria-pressed={status === each} onClick={() => setStatus(each)} className={filterClass(status === each)}>
              {STATUS_FILTER[each]}
              {list ? ` (${list.counts[each] ?? 0})` : ''}
            </button>
          ))}
        </div>

        <form onSubmit={onFilter} noValidate className="mt-4 flex max-w-md flex-wrap items-end gap-2">
          <label className="grow text-sm font-medium text-slate-700">
            Order reference
            <input value={orderInput} onChange={(event) => setOrderInput(event.target.value)} placeholder="MS-XXXX-XXXX" className={INPUT_CLASS} />
          </label>
          <button type="submit" className={BUTTON_CLASS}>
            Show
          </button>
          {order && (
            <button
              type="button"
              onClick={() => {
                setOrderInput('')
                setOrder('')
              }}
              className={SECONDARY_CLASS}
            >
              All orders
            </button>
          )}
        </form>
      </section>

      <section aria-labelledby="list-heading" className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
        <h2 id="list-heading" className="text-lg font-semibold">
          {order ? `Emails for order ${order.toUpperCase()}` : 'Latest emails'}
        </h2>
        {message && (
          <p role={message.isError ? 'alert' : 'status'} className={`mt-3 text-sm ${message.isError ? 'text-rose-700' : 'text-emerald-800'}`}>
            {message.text}
          </p>
        )}
        {loadError && (
          <p role="alert" className="mt-3 text-sm text-rose-700">
            {loadError}
          </p>
        )}
        {!list && !loadError && <p className="mt-3 text-sm text-slate-600">Loading emails…</p>}
        {list && list.items.length === 0 && <p className="mt-3 text-sm text-slate-600">No emails match.</p>}
        {list && list.items.length > 0 && (
          <ul className="mt-3 divide-y divide-slate-100">
            {list.items.map((email) => (
              <li key={email.id} aria-label={`${KIND[email.kind]} for order ${email.orderReference}`} className="flex flex-wrap items-start justify-between gap-3 py-3 text-sm">
                <div className="min-w-0 space-y-0.5">
                  <p className="font-medium">
                    {KIND[email.kind]} · Order {email.orderReference}
                  </p>
                  <p className="break-all text-slate-600">To {email.recipientEmail}</p>
                  <p className="text-slate-600">
                    {describeDelivery(email, locale)}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-3">
                  <span className={`rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ${STATUS_CLASS[email.status]}`}>{STATUS[email.status]}</span>
                  {email.canResend && (
                    <button
                      type="button"
                      disabled={busyId !== null}
                      onClick={() => void resend(email)}
                      aria-label={`Send the ${KIND[email.kind].toLowerCase()} email for order ${email.orderReference} again`}
                      className={SECONDARY_CLASS}
                    >
                      {busyId === email.id ? 'Sending again…' : 'Send again'}
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

function describeDelivery(email: EmailDelivery, locale: string | undefined): string {
  const attempts = email.attemptCount === 1 ? '1 attempt' : `${email.attemptCount} attempts`
  const resent = email.resendCount > 0 ? ` · sent again ${email.resendCount === 1 ? 'once' : `${email.resendCount} times`}` : ''
  switch (email.status) {
    case 'Sent':
      return `Sent ${formatDateTime(email.sentAt ?? email.createdAt, locale)}${email.provider ? ` via ${email.provider}` : ''} · ${attempts}${resent}`
    case 'Failed':
      return `Gave up ${formatDateTime(email.lastAttemptAt ?? email.createdAt, locale)}${email.lastError ? `: ${ERROR[email.lastError]}` : ''} · ${attempts}${resent}`
    case 'Pending':
      return email.attemptCount === 0
        ? `Queued ${formatDateTime(email.createdAt, locale)}${resent}`
        : `Last try failed${email.lastError ? ` (${ERROR[email.lastError]})` : ''}; next try ${formatDateTime(email.nextAttemptAt ?? email.createdAt, locale)} · ${attempts}${resent}`
    case 'Sending':
      return `Being sent now · ${attempts}${resent}`
    case 'Suppressed':
      return `Not sent: the recipient is not on the allow-list for the live email service · queued ${formatDateTime(email.createdAt, locale)}`
  }
}
