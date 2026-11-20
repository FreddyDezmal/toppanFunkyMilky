# Progress Report – Milestone 8: Payment Gateway Integration

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 20 November 2026
**Period covered:** 20 November 2026
**Status:** Milestone 8 (payments, "M8a") complete in test mode. Patrons pay through Paystack's hosted checkout; an
order is confirmed only after the server verifies the payment with Paystack. Cancellation and refunds (B1, "M8b")
followed in M8b (separate report). The store stays in Paystack test mode (demonstration store, decided 20 November 2026).
**Requirements traced:** roadmap AC-8.1 to AC-8.6, T8.1 to T8.6, MR-06, FR-06 (URS §2.2, §3.1, §4 as cited by the roadmap)

Related: [payment architecture](../payments/payment-architecture.md) ·
[Paystack setup and webhook guide](../payments/paystack-setup-guide.md) ·
[pre-M8 decisions](../decisions/2026-11-20_pre-m8-decisions.md)

---

## 1. Summary

- **Pay now** on the order page opens a Paystack checkout for exactly the stored order total; the browser sends no
  amount and is sent only to `https://checkout.paystack.com`.
- Every attempt is stored before Paystack is called, so an interruption at any point is recoverable.
- Confirmation comes only from Paystack's Verify Transaction API, reached three ways: the signed webhook, the
  patron's return to the order page, and a background job every 5 minutes (works with the browser closed and
  without webhooks).
- The database allows one successful and at most one open payment per order; duplicate webhooks are recognised by a
  unique receipt index.
- Money that cannot be accepted (cancelled or already paid order, wrong amount or currency) is kept as
  `RequiresRefund`, never discarded and never confirming the order.
- A real sandbox payment with Paystack's test card succeeded end to end on 20 November 2026.

## 2. Decision register

Design decisions A1–A4 and P1–P10 were approved before implementation (see the pre-M8 register). Decisions made
during implementation:

| ID | Decision | Status | Basis |
| --- | --- | --- | --- |
| A1 | Paystack, ZAR, hosted checkout, test mode | Approved | Pre-M8 register |
| A2 | No payment once the pickup time has passed (422) | Provisional | Pre-M8 register |
| P1–P10 | Tables, statuses, partial indexes, commit-before-call initiation, signed and re-verified webhook, one-transaction outcome, recovery, redirect guard, late verification still confirms, simulated provider in tests | Approved | Pre-M8 register |
| I1 | Amounts sent to Paystack in cents (`MinorUnits`), ZAR only; a non-ZAR order cannot be paid | Approved | Paystack API uses the lowest unit |
| I2 | Only `charge.success` notifications are acted on; failures and abandonment are found by verification | Approved | Verification is authoritative either way |
| I3 | An `Initiating` attempt younger than 1 minute is not verified | Approved | Its Paystack call may still be in flight |
| I4 | Paystack "abandoned" counts as still open for 30 minutes after checkout creation, then `Abandoned` (order unchanged) | Provisional | Patrons can leave and return to the checkout page |
| I5 | Attempts still open after 72 hours are ended (`Abandoned`, "Checkout expired.") | Approved | Paystack's notification retry window |
| I6 | Background reconciliation every 5 minutes, 50 oldest open attempts per run, stops early if Paystack is down, skipped without a key; configurable | Approved | P7; no extra infrastructure |
| I7 | Patron refresh is `POST …/payments/refresh` (CSRF, ordering rate limit) because it may change stored state | Approved | Safe-method rule |
| I8 | Return page polls every 3 s up to 10 times, then shows "still waiting" with **Check again** | Approved | Within the 60/min limit; the job continues server-side |
| I9 | One idempotency key per click, reused only after an uncertain failure (network, timeout, 5xx other than 503) | Approved | Same rule as M7 placement |
| I10 | Webhook limits: 64 KB body, 300 requests per minute per IP | Approved | Generous enough for Paystack's retries |
| DV-2 | The Playwright sandbox journey (prompt §14, end-to-end steps 4–10) is checked manually, not automated | **Accepted deviation** (developer, 20 November 2026) | Third-party hosted checkout; webhooks cannot reach localhost. The same flow is automated with a simulated provider (integration and Vitest tests) |

## 3. Architecture

See [payment-architecture.md](../payments/payment-architecture.md) for the component diagram, state table, database
design, guarantees and recovery timings.

## 4. Database changes (migration `20261120062531_AddPayments`)

Purely additive: tables `payments` and `payment_events`, their indexes and checks, and a restrict-only foreign key to
`orders`. Reviewed as SQL (no DROP/ALTER of existing objects) and applied to Supabase on 20 November 2026 with the
developer's approval.

## 5. Requirements traceability

| AC | Status | Evidence |
| --- | --- | --- |
| AC-8.1 To the gateway and back; amount = server total | **Met** | `PaymentInitiationTests` (2990 cents for R29.90, return URL); manual sandbox payment (dashboard amount matched); Vitest redirect tests |
| AC-8.2 Confirmed only after server-side verification (signature and amount) | **Met** | `PaystackWebhookTests` (unsigned/forged → 401; event's own claims ignored; Verify API decides); `PaymentTests` (amount/currency mismatch → RequiresRefund); refresh never marks paid while checking |
| AC-8.3 Failed, cancelled, abandoned → unconfirmed, clear message, retry | **Met** | `PaymentRecoveryTests` (failed → PaymentFailed, retry allowed; abandoned → order unchanged); Vitest failed/cancelled/still-waiting states with Pay now |
| AC-8.4 Duplicate or replayed callbacks never duplicate | **Met** | Repeated and simultaneous duplicate webhooks → one receipt, one confirmation; one-success and one-open partial indexes tested even with the domain bypassed |
| AC-8.5 Every attempt stored with reference, amount, status, timestamps; linked; audited | **Met** | `PaymentPersistenceTests` (saved with the order change and audited; FK restrict) |
| AC-8.6 No card data stored or logged | **Met** | Hosted checkout; no card fields in schema or code; log messages reviewed (references, statuses, reasons only) |

## 6. Security controls with evidence

| Control | Evidence |
| --- | --- |
| Patron endpoints: PatronOnly, ownership, CSRF | Anonymous 401, manager 403, other patron's order 404, missing CSRF 400 (start and refresh) |
| No browser-supplied amount, currency or status | Start body must be empty/`{}`; any field → 400 (theory) |
| Idempotency key validated | Missing, malformed or empty GUID → 400 |
| Webhook isolated from patron auth | Anonymous, CSRF-exempt only via explicit metadata; `SecurityPipelineTests` lists it as the only anonymous write besides login/register |
| Webhook authenticity | HMAC-SHA512 over raw body, constant time; missing/invalid → 401; re-verification with Paystack |
| Size limits | Start/refresh 1 KB → 413; webhook 64 KB → 413 |
| Rate limits | Start and refresh share the per-patron ordering limit (429 tested); webhook per-IP limit (429 tested) |
| Safe errors | Forced provider errors containing a fake key and internal detail: no key, detail, "Exception" or stack trace in 503/200/401/400 responses |
| No open redirect | Server rejects a non-Paystack checkout address and ends the attempt; browser refuses non-https, look-alike hosts, ports and credentials (8-case test) |
| Secrets | Key in user secrets only; history scan clean (no `sk_`/`pk_` keys, Supabase secrets, connection-string passwords, private keys, `.env`/`secrets.json`) |
| Dependencies | `npm audit` 0 vulnerabilities; `dotnet list package --vulnerable` clean |

## 7. Test results (executed 20 November 2026)

Run by the developer on Windows 11 with Docker Desktop, except `npm audit` and the secret scan (run by the assistant
on an identical lockfile and the repository history).

| Suite | Count | Result |
| --- | --- | --- |
| Backend unit + integration (Testcontainers PostgreSQL 17) | 655 (549 at M7 + 106) | Passed |
| Vitest | 131 (110 at M7 + 21) | Passed |
| Playwright (with axe scans) | 9 | Passed |
| Build `-warnaserror`, `tsc -b`, oxlint | – | Passed |
| `npm audit`, `dotnet list package --vulnerable --include-transitive` | – | No vulnerabilities |
| Manual Paystack sandbox payment (test card) | 1 journey | Passed: verified, Confirmed/Paid, dashboard reference and amount matched. Decline-card and cancelled-checkout checks also passed (confirmed by the developer) |

New in M8: domain 23 (`PaymentTests`), gateway client 14, webhook signature 3, persistence 13, initiation API 20,
webhook API 14, recovery 12, hardening 7; Vitest 21 (pay, duplicate clicks, redirect guard, retries, return states,
outcome mapping).

## 8. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| A refund-reason check passed when the column was NULL | Check now requires `refund_reason IS NOT NULL` for RequiresRefund; migration regenerated before it was applied anywhere |
| Partial index names overridden by the snake-case convention | Explicit `HasDatabaseName`; migration regenerated |
| Simulated provider reused one transaction id, colliding with the unique index | Unique ids per simulated payment; one theory given distinct ids |
| New anonymous webhook tripped the "only login/register are anonymous writes" test | Expected list updated with the justification (signature-authenticated) |
| Refresh settled an untracked entity | It now settles a tracked copy, then re-reads |
| All 12 recovery tests failed in setup (400 placing the order) | The test clock was fixed before the menu's prices were created, so the prices were not yet in force; the clock is now set after the menu exists. Test-only fix |

## 9. Definition of Done

| Item | Status |
| --- | --- |
| Provider selected; test credentials stored securely | Done |
| Attempts stored in PostgreSQL; persisted total and currency used | Done |
| Patrons pay only their own eligible orders; no duplicate charges | Done |
| Checkout works in test mode | Done (manual sandbox) |
| Notifications authenticated; duplicates idempotent; out-of-order safe | Done (automated); live webhook delivery to a public URL **pending deployment** |
| Amount/currency mismatches rejected; verified success confirms; failure recorded | Done |
| Cancelled orders cannot be silently confirmed | Done (late success → RequiresRefund) |
| Atomic order/payment changes; recovery without an open browser | Done |
| Accurate pending/success/failure UI; access control; existing protections | Done |
| Existing and new tests pass; accessibility checks | Done |
| No secrets or card details exposed; documentation | Done |
| Automated sandbox end-to-end journey | Not automated – **accepted deviation DV-2** (manual sandbox journey passed) |
| Production activation | **Not applicable** – decided 20 November 2026: the project is a demonstration store and stays in Paystack test mode (no real money). The checklist in the setup guide applies only if a real store goes live |

## 10. Open questions and deferred items

| Item | Needed from | Effect |
| --- | --- | --- |
| Production Paystack account and live keys | Not needed | Decided 20 November 2026: payments stay in Paystack test mode for this demonstration store |
| Cancellation and refunds (B1.1–B1.4) | – | Done in M8b, including automatic full refunds of `RequiresRefund` payments |
| A2, I4 (no payment after pickup; 30-minute abandon grace) | Stakeholder | Provisional; both configurable in code |
| Q-12 gateway confirmation | Stakeholder | Paystack chosen (A1) |
| Manager view of payments and refunds | M12 / M8b | Not in M8 acceptance criteria |
| AC-6.6 sign-off, Q-01–Q-04, Q-07, Q-10, Q-14 | Stakeholder | Unchanged |

## 11. Integration notes for Milestone 9 (emails)

- **Trigger:** the moment a payment becomes `Succeeded` and its order `Confirmed`, inside `Payment.ApplyVerifiedOutcome`
  (reached from the webhook, the refresh endpoint and the reconciliation job). Do not send from the browser path.
- **Never roll back a payment (AC-9.4):** write an "email to send" record (outbox) in the **same** `SaveChanges` as the
  confirmation, and send it from a background worker with retries. A sending failure must not affect the payment.
- **Exactly once:** the confirmation happens once per order (unique success index), so one outbox row per confirmation;
  give the outbox a unique (order, email kind) index so retries and duplicate notifications cannot double-send.
- **Content (AC-9.2, AC-9.3):** use the order snapshot (`orders`, `order_items`) and the payment's `reference`
  (`MSP-…`) and `provider_transaction_id`; never current catalogue prices.
- **One combined email** for order and payment confirmation (B7, provisional).
- `RequiresRefund` payments may warrant a manager notification once M8b defines the refund flow.

## 12. Commit history (Milestone 8)

| Commit | Description |
| --- | --- |
| `586a79e` | Decision register before M8 |
| `d0d5e1f` | Payment and payment-event entities, verified-outcome rules, partial indexes, `AddPayments` migration |
| `354f066` | Paystack checkout initiation and signed webhook |
| `0616303` | Payment recovery without webhooks: refresh on return, background reconciliation |
| `f0557ed` | React checkout |
| `36df1ab` | Payment security hardening tests |
| (this commit) | M8 documentation: report, architecture, Paystack setup and webhook guide, README |
