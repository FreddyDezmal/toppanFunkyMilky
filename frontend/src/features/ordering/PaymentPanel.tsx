import { useEffect, useRef, useState } from 'react'
import { ApiError } from '../../lib/api/httpClient'
import { refreshPayment, startPayment } from './api'
import {
  checkoutNavigation,
  isTrustedCheckoutUrl,
  isUncertainStartFailure,
  PAYMENT_MESSAGES,
  PAYMENT_POLLING,
  paymentOutcome,
  START_PAYMENT_TIMEOUT_MS,
  startPaymentFailureMessage,
  type PaymentOutcome,
} from './payment'

type View =
  | { kind: 'ready' }
  | { kind: 'starting' }
  | { kind: 'redirecting' }
  | { kind: 'checking' }
  | { kind: 'outcome'; outcome: PaymentOutcome; message: string | null }
  | { kind: 'stillChecking' }
  | { kind: 'checkFailed' }
  | { kind: 'startFailed'; message: string }

interface PaymentPanelProps {
  reference: string
  /** From the stored order: whether it can still be paid (awaiting payment, or a payment failed). */
  awaitsPayment: boolean
  /** The amount to pay, formatted for display. Shown only; the server charges the stored order total. */
  amountLabel: string
  /** True when the patron has just come back from checkout. The return address proves nothing, so the server is asked. */
  returning: boolean
  /** Reload the stored order after the server reports a change. Must be stable. */
  onOrderChanged: () => void
}

const BUTTON_CLASS = 'rounded-lg bg-brand-600 px-4 py-2 text-sm font-medium text-white hover:bg-brand-700 disabled:opacity-50'
const SECONDARY_CLASS = 'rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50'

/**
 * Paying for an order (M8). "Pay now" asks the server to open a checkout and sends the browser only to Paystack's
 * hosted page. On return the server is asked to verify the payment; this component never decides that an order is paid.
 */
export function PaymentPanel({ reference, awaitsPayment, amountLabel, returning, onOrderChanged }: PaymentPanelProps) {
  const [view, setView] = useState<View>(returning ? { kind: 'checking' } : { kind: 'ready' })
  // Each increment runs one round of checking with the server; 0 means none yet.
  const [checkRound, setCheckRound] = useState(returning ? 1 : 0)
  const busy = useRef(false)
  // One key per payment click, kept only when the outcome is unknown so trying again cannot open a second attempt.
  const pendingKey = useRef<string | null>(null)

  useEffect(() => {
    if (checkRound === 0) {
      return
    }
    const controller = new AbortController()
    const { signal } = controller

    void (async () => {
      let answered = false
      for (let attempt = 0; attempt < PAYMENT_POLLING.attempts; attempt += 1) {
        if (attempt > 0) {
          await pause(PAYMENT_POLLING.intervalMs, signal)
        }
        if (signal.aborted) {
          return
        }
        try {
          const status = await refreshPayment(reference, signal)
          if (signal.aborted) {
            return
          }
          answered = true
          if (!status.checking) {
            const outcome = paymentOutcome(status)
            setView(outcome === 'none' ? { kind: 'ready' } : { kind: 'outcome', outcome, message: status.payment?.message ?? null })
            onOrderChanged()
            return
          }
        } catch (error) {
          if (signal.aborted) {
            return
          }
          if (error instanceof ApiError && error.status === 404) {
            break
          }
        }
      }
      setView(answered ? { kind: 'stillChecking' } : { kind: 'checkFailed' })
    })()

    return () => controller.abort()
  }, [checkRound, reference, onOrderChanged])

  // Coming back with the browser's Back button can restore this page as it was left ("Redirecting…"): reset it.
  useEffect(() => {
    const onPageShow = (event: PageTransitionEvent) => {
      if (event.persisted) {
        busy.current = false
        setView({ kind: 'ready' })
      }
    }
    window.addEventListener('pageshow', onPageShow)
    return () => window.removeEventListener('pageshow', onPageShow)
  }, [])

  function checkAgain() {
    setView({ kind: 'checking' })
    setCheckRound((round) => round + 1)
  }

  async function pay() {
    if (busy.current) {
      return
    }
    busy.current = true
    setView({ kind: 'starting' })
    const idempotencyKey = (pendingKey.current ??= crypto.randomUUID())

    const controller = new AbortController()
    let timedOut = false
    const timer = setTimeout(() => {
      timedOut = true
      controller.abort()
    }, START_PAYMENT_TIMEOUT_MS)

    try {
      const checkout = await startPayment(reference, idempotencyKey, controller.signal)
      pendingKey.current = null
      if (!isTrustedCheckoutUrl(checkout.checkoutUrl)) {
        busy.current = false
        setView({ kind: 'startFailed', message: PAYMENT_MESSAGES.untrusted })
        return
      }
      // busy stays set: the page is leaving for checkout.
      setView({ kind: 'redirecting' })
      checkoutNavigation.go(checkout.checkoutUrl)
    } catch (error) {
      if (!isUncertainStartFailure(error, timedOut)) {
        pendingKey.current = null
      }
      busy.current = false
      setView({ kind: 'startFailed', message: startPaymentFailureMessage(error, timedOut) })
      if (error instanceof ApiError && error.status === 409) {
        // Already paid, no longer payable, or a payment being prepared: show the order as now stored.
        onOrderChanged()
      }
    } finally {
      clearTimeout(timer)
    }
  }

  const waiting = view.kind === 'checking' || view.kind === 'redirecting'
  const showPay = awaitsPayment && !waiting && !(view.kind === 'outcome' && view.outcome === 'paid')

  if (!showPay && view.kind === 'ready') {
    return null
  }

  return (
    <section aria-labelledby="payment-heading" className="mt-6 max-w-xl rounded-xl border border-slate-200 p-4">
      <h2 id="payment-heading" className="text-lg font-semibold">
        Payment
      </h2>

      {view.kind === 'checking' && (
        <p role="status" className="mt-2 text-sm text-slate-700">
          {PAYMENT_MESSAGES.checking}
        </p>
      )}
      {view.kind === 'outcome' && <OutcomeMessage outcome={view.outcome} message={view.message} />}
      {view.kind === 'stillChecking' && (
        <Notice tone="info" message={PAYMENT_MESSAGES.stillChecking} action={{ label: 'Check again', onClick: checkAgain }} />
      )}
      {view.kind === 'checkFailed' && (
        <Notice tone="error" message={PAYMENT_MESSAGES.checkFailed} action={{ label: 'Check again', onClick: checkAgain }} />
      )}
      {view.kind === 'startFailed' && <Notice tone="error" message={view.message} />}

      {showPay && (
        <div className="mt-3">
          <button
            type="button"
            onClick={() => void pay()}
            disabled={view.kind === 'starting'}
            aria-describedby="payment-help"
            className={BUTTON_CLASS}
          >
            {view.kind === 'starting' ? 'Opening secure checkout…' : 'Pay now'}
          </button>
          <p id="payment-help" className="mt-2 text-xs text-slate-500">
            You will pay {amountLabel} on Paystack&rsquo;s secure page. We never see or store your card details. Your order is confirmed
            only once we have verified the payment.
          </p>
        </div>
      )}
      {view.kind === 'redirecting' && (
        <p role="status" className="mt-2 text-sm text-slate-700">
          Taking you to Paystack&rsquo;s secure checkout…
        </p>
      )}
    </section>
  )
}

function OutcomeMessage({ outcome, message }: { outcome: PaymentOutcome; message: string | null }) {
  switch (outcome) {
    case 'paid':
      return (
        <p role="status" className="mt-2 rounded-lg bg-emerald-50 p-3 text-sm font-medium text-emerald-900">
          {PAYMENT_MESSAGES.paid}
        </p>
      )
    case 'failed':
      return (
        <div role="alert" className="mt-2 rounded-lg bg-rose-50 p-3 text-sm text-rose-800">
          <p>{PAYMENT_MESSAGES.failed}</p>
          {message && <p className="mt-1 text-xs">Paystack said: {message}</p>}
        </div>
      )
    case 'cancelled':
      return <Notice tone="info" message={PAYMENT_MESSAGES.cancelled} />
    case 'needsRefund':
      return <Notice tone="info" message={PAYMENT_MESSAGES.needsRefund} />
    default:
      return null
  }
}

function Notice({ tone, message, action }: { tone: 'info' | 'error'; message: string; action?: { label: string; onClick: () => void } }) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={`mt-2 rounded-lg p-3 text-sm ${tone === 'error' ? 'bg-rose-50 text-rose-800' : 'bg-amber-50 text-amber-900'}`}
    >
      <p>{message}</p>
      {action && (
        <button type="button" onClick={action.onClick} className={`${SECONDARY_CLASS} mt-2`}>
          {action.label}
        </button>
      )}
    </div>
  )
}

/** Waits, ending early (without error) if the page is left. */
function pause(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const timer = setTimeout(resolve, ms)
    signal.addEventListener(
      'abort',
      () => {
        clearTimeout(timer)
        resolve()
      },
      { once: true },
    )
  })
}
