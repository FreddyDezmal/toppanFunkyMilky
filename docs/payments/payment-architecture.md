# Payment Architecture (Milestone 8)

**Provider:** Paystack (decision A1), hosted checkout, South African rand (ZAR) only.
**Principle:** an order becomes **Confirmed** only after the server has verified the payment with Paystack. The
browser, the return address and the webhook's own JSON are never trusted as proof of payment.

## 1. Components

```
React /orders/:reference
  "Pay now" ──POST /api/ordering/orders/{ref}/payments (Patron, CSRF, Idempotency-Key, body {})
                │
                ▼
  PaymentService ── 1. ownership + eligibility (awaits payment, pickup not passed)
                    2. open attempt? check it with Paystack first (resume, or settle it)
                    3. INSERT payments (Initiating, our reference MSP-…) and COMMIT
                    4. Paystack transaction/initialize (amount = order total in cents)  ← no DB transaction held
                    5. UPDATE payment → Pending + checkout URL (must be https://checkout.paystack.com)
                │
                ▼
  Browser → checkout.paystack.com → back to /orders/{ref}?payment=return
                                         │
                                         ▼
  POST /api/ordering/orders/{ref}/payments/refresh (Patron, CSRF) ─┐
  Background job every 5 minutes (open attempts, oldest first) ────┤
  Webhook POST /api/payments/paystack/webhook (signed) ────────────┤
                                                                   ▼
                     Paystack transaction/verify/{reference} (the only source of truth)
                                                                   ▼
                     Payment.ApplyVerifiedOutcome(order, …) → one SaveChanges:
                     payment row + order status + payment_events receipt + audit rows
```

| Layer | Type | Responsibility |
| --- | --- | --- |
| Domain | `Payment`, `PaymentEvent`, `PaymentStatus`, `MinorUnits` | Attempt state machine, amount/currency/order checks, refund flags |
| Application | `IPaymentGateway`, `IPaymentService`, `IPaymentNotificationService`, `IPaymentStatusService`, `IPaymentReconciler` | Use-case contracts; no HTTP or EF |
| Infrastructure | `PaystackGateway`, `PaymentService`, `PaymentNotificationService`, `PaystackWebhookAuthenticator`, `PaymentSettler`, `PaymentReconciler`, `PaymentReconciliationWorker`, `PaymentStatusService` | Paystack HTTP client, persistence, verification, recovery |
| API | `OrderingController` (start, refresh), `PaystackWebhookController` | Authorisation, CSRF, rate limits, body limits, ProblemDetails |
| React | `PaymentPanel`, `payment.ts` | Pay now, redirect guard, return polling, patron messages |

## 2. Payment attempt states

| Status | Meaning | Order effect |
| --- | --- | --- |
| `Initiating` | Row committed, Paystack not yet answered | None |
| `Pending` | Checkout created; the patron may be paying | None |
| `Succeeded` | Verified paid, amount and currency match, order payable | PendingPayment / PaymentFailed → **Confirmed** |
| `Failed` | Verified declined or reversed | PendingPayment → **PaymentFailed** (retry allowed) |
| `Abandoned` | Not paid: not found at Paystack, left unpaid past 30 minutes, or open past 72 hours | None (order stays payable) |
| `Superseded` | Replaced by a newer attempt | None |
| `RequiresRefund` | Money arrived but cannot be accepted: order no longer payable (e.g. cancelled), already paid, or amount/currency mismatch | None; kept for refund handling (M8b) |

Open = `Initiating` or `Pending`. A failed, cancelled and abandoned attempt are distinguished: only a verified decline
sets the order to PaymentFailed; an abandoned checkout leaves the order as it was.

## 3. Database (migration `20261120062531_AddPayments`, applied to Supabase 20 November 2026)

| Table | Contents | Constraints |
| --- | --- | --- |
| `payments` | order_id, reference `MSP-XXXXXXXXXXXX`, provider, idempotency_key, amount_minor (cents), currency, status, checkout_url, provider_transaction_id, provider_message, refund_reason, timestamps, xmin | checks: status list, reference format, amount > 0, currency format, refund reason only with RequiresRefund, https checkout URL; unique reference; unique (order_id, idempotency_key); unique (provider, provider_transaction_id) where present; **`ux_payments_one_success_per_order`** and **`ux_payments_one_open_per_order`** (partial unique); index (status, created_at); FK to orders ON DELETE RESTRICT |
| `payment_events` | provider, event_type, provider_transaction_id, reference, received_at | unique (provider, event_type, provider_transaction_id) – webhook deduplication; never the raw payload |

Both tables are audited by the existing audit interceptor in the same transaction as the change.

## 4. Guarantees and where they are enforced

| Guarantee | Enforcement |
| --- | --- |
| Charge exactly the stored order total | Amount taken from `orders.total` × 100 on the server; the start request accepts no fields |
| One checkout per click, none duplicated | `Idempotency-Key` per click; unique (order, key); at most one open attempt per order (partial index); an open attempt is resumed, not duplicated |
| One success per order, never confirmed twice | Partial unique index; extra successes become `RequiresRefund` |
| No confirmation without verification | Only `ApplyVerifiedOutcome`, called after Paystack's Verify API, can confirm |
| Amount, currency and reference checked | Verified amount in cents and currency compared with the attempt; mismatch → `RequiresRefund` |
| Cancelled orders never silently confirmed | Late success on a non-payable order → `RequiresRefund`, order untouched |
| Atomic order/payment change | One `SaveChanges` (one transaction) for payment, order, receipt and audit rows |
| Duplicate webhooks harmless | Unique receipt index; duplicates acknowledged (200) without changes |
| Works without the browser | Background reconciliation; webhook; refresh on return |
| Safe redirect | Server and browser both accept only `https://checkout.paystack.com` |

## 5. Recovery timings

| Rule | Value | Reason |
| --- | --- | --- |
| Leave a young `Initiating` attempt alone | 1 minute | Its Paystack call may still be in flight |
| Paystack "abandoned" still counts as open | 30 minutes after checkout creation | The patron may still be on the checkout page |
| End attempts still open | 72 hours | Paystack's notification retry window |
| Background job | every 5 minutes, 50 attempts per run (`Payments:Reconciliation`) | Webhooks can be lost; localhost receives none |
| Browser polling after return | every 3 s, 10 times (~30 s), then "still waiting" + Check again | Within the 60/min ordering limit |

## 6. What is not here (by design)

- No card data: Paystack's hosted page collects it; the API stores only references, amounts and statuses.
- No manager overrides (managers ask; patrons decide), no emails (M9), no reporting (M12).
- No frontend secrets: the browser never sees the Paystack key; there is no Paystack JavaScript in the app.

## 7. Cancellation and refunds (M8b)

```
Patron "Cancel order" ─ POST …/cancel {expectedRefund}        Manager ─ POST /api/manager/orders/{ref}/cancellation-requests {reason}
Patron "Accept"        ─ POST …/cancellation-request/accept      └─ cancellation_requests (Pending; one per order)
            │
            ▼
OrderCancellationService: own order, before pickup; unpaid → check open checkout with Paystack first;
paid → expectedRefund must equal paid − fee (patron) or paid (accepted request)
            │  ONE SaveChanges: order Cancelled + refunds row (Initiating, MSR-…) + request Accepted/Lapsed + audit
            ▼
RefundSubmitter → Paystack POST /refund {transaction: MSP-…, amount (cents), reference: MSR-…}  (409 = already exists → read it)
            ▼
Pending ──(refund.* webhook → GET /refund?reference=MSR-… | job every 5 min)──► Processed | Failed (→ managers, Q4)
```

| Rule | Where |
| --- | --- |
| Fee = `Refund.FeeRate` × order `taxable`, rounded once to cents; none for accepted requests and unaccepted payments | `Refund.CancellationFee`, `ck_refunds_fee_rate` |
| One refund per payment | `ux_refunds_one_per_payment` |
| One pending request per order; lapse at pickup | `ux_cancellation_requests_one_pending_per_order`; job step 5 |
| Refund complete only when Paystack says so | `Refund.ApplyVerifiedOutcome` (amount must match) |
| Late or duplicate payment → full refund automatically; wrong amount → manual | job step 2 (decision I4) |

Reconciliation job, every run: (1) settle open payment attempts; (2) refund unaccepted payments; (3) re-send refunds
Paystack has not accepted (over 1 minute old); (4) check pending refunds; (5) lapse requests at pickup.