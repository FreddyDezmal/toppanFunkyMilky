# Progress Report – Milestone 8b: Order Cancellation and Refunds

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 20 November 2026
**Period covered:** 20 November 2026
**Status:** M8b complete in test mode. Patrons cancel before pickup, free when unpaid and with a 10% fee when paid.
Managers ask patrons to cancel, and patrons who accept get a full refund. Refunds go through Paystack and count as
complete only when Paystack confirms them. Like payments, refunds stay in Paystack test mode (demonstration store).
**Requirements traced:** decision register B1.1–B1.5, R1–R10, Q1–Q4 (`docs/decisions/2026-11-20_pre-m8-decisions.md`);
roadmap AC-7.3 (lifecycle) and AC-7.5 (audit), extended to cancellation; open question Q-10 answered by B1.

Related: [payment architecture](../payments/payment-architecture.md) ·
[Paystack setup and webhook guide](../payments/paystack-setup-guide.md) · [M8 report](2026-11-20_milestone-8-payments-report.md)

---

## 1. Summary

- **Lifecycle:** a paid order can now be cancelled: Confirmed → Cancelled, before the pickup time only (R1, B1.1).
- **Unpaid orders are cancelled for free.** An open checkout is checked with Paystack first. If it has just been paid,
  nothing is cancelled and the patron sees the refund before confirming again (R6).
- **Paid orders:** the patron gets back what they paid less a fee. The fee is 10% of the VAT-exclusive amount
  (`Refund.FeeRate`, editable by managers from 0 to 0.5). Example: R59.80 paid gives a R5.20 fee and a R54.60 refund
  (B1.4, R5). The cancellation and its refund are stored in one transaction before Paystack is asked (R4).
- **Managers never cancel directly.** They ask, giving a reason of at most 50 characters. If the patron accepts, the
  order is cancelled and refunded in full with no fee (Q1). The patron may decline. An unanswered request lapses at
  the pickup time (B1.2, R7).
- **Payments that cannot be accepted** are refunded in full automatically (Q2): a payment arriving after cancellation,
  or a second payment for the same order. A payment of a different amount is logged and refunded by hand (decision I4).
- **Refunds are verified:** signed refund webhooks are re-checked with Paystack's API. The reconciliation job re-sends
  refunds Paystack has not accepted yet and checks pending ones until Processed or Failed. Failed refunds, and refunds
  needing attention, are listed for managers (Q4).

## 2. Decision register

B1.1–B1.5, R1–R10 and Q1–Q4 were approved before implementation. Decisions made during implementation:

| ID | Decision | Status | Basis |
| --- | --- | --- | --- |
| I1 | Cancel and accept carry no `Idempotency-Key` (differs from R8). Repeating them is already safe: a cancelled order stays cancelled, a repeat returns its state, and the database allows one refund per payment. Five simultaneous cancels produce one refund (tested) | Approved | Natural idempotency; an unused key would mislead |
| I2 | A refund sends our reference `MSR-…` to Paystack. Paystack refuses a reused reference with 409, which is treated as "already exists" and the existing refund is read, so retries never refund twice | Approved | Paystack refund API |
| I3 | Paystack's `needs-attention` (bank details needed) stays Pending, flagged "needs attention", and is listed for managers; it is not marked Failed because Paystack may still complete it | Approved | Q4; no bank details collected |
| I4 | A payment of a different amount is **not** refunded automatically: the amount actually taken is not stored, so an automatic refund would be a guess. It is logged for a manual refund at Paystack | Approved by the developer 20 Nov 2026 | Exception to Q2; Paystack charges exactly the amount sent |
| I5 | Refund notifications have no receipts table: Paystack's refund payloads carry no id, and applying a verified result twice changes nothing (Processed and Failed are final) | Approved | Idempotent re-verification |
| I6 | An unpaid order cannot be cancelled while its checkout is less than a minute old ("a payment is being prepared") or while Paystack is unreachable | Approved | Avoids a paid, cancelled order |
| I7 | A patron cannot cancel a paid order while the fee-rate setting is missing or invalid (logged); accepting a manager request still works because it charges no fee | Approved | Never guess the fee |
| I8 | Managers see the patron's first name and email on Management → Orders, to contact them about requests and failed refunds | Provisional | Minimum needed for Q3 and Q4 (POPIA, Q-14) |

## 3. Design

| Part | Contents |
| --- | --- |
| Domain | `Order.CanBeCancelledAt`, `Order.Cancel(now)`; `Refund` (fee calculation, three kinds, verified status changes); `CancellationRequest` (reason rules, accept, decline, lapse) |
| Database | Migration `20261120092915_AddCancellationsAndRefunds`, purely additive: `refunds` (one per payment, fee arithmetic checks) and `cancellation_requests` (one pending per order, reason checks). Restrict-only foreign keys; both tables audited. Setting `Refund.FeeRate` added by the seeder |
| Paystack | `CreateRefundAsync` (our reference; a 409 means it already exists, so read it) and `GetRefundAsync` (by our reference) |
| Services | `OrderCancellationService` (patron cancel, accept, decline, Home notice), `ManagerOrderService`, `RefundSubmitter`, refund webhook handling, reconciliation steps 2–5 |
| API | Patron: `GET …/cancellation`, `POST …/cancel`, `POST …/cancellation-request/accept`, `POST …/cancellation-request/decline`, `GET /api/ordering/cancellation-requests`. Manager: `GET /api/manager/orders/{ref}`, `POST /api/manager/orders/{ref}/cancellation-requests`, `GET /api/manager/refunds/attention` |
| React | `CancellationPanel` on the order page, `CancellationRequestNotice` on Home, `/manager/orders`, the "Cancellation fee rate" setting label, and **My orders** (`/orders`), part of M11 brought forward so patrons can return to an order to cancel it or answer a request |

## 4. Requirements traceability

| Rule | Status | Evidence |
| --- | --- | --- |
| B1.1 Any time before pickup | **Met** | `OrderTests` (until/at pickup), `OrderCancellationTests` (422 after pickup), UI pickup-passed message |
| B1.2 Manager requests with a reason (≤ 50); patron accepts or declines; lapse at pickup | **Met** | `CancellationRequestTests` (domain and API), `RefundCompletionTests` (job lapse), React banner and Orders page tests |
| B1.3 Unpaid cancellation free | **Met** | API and UI tests; no refund created |
| B1.4 Paid: refund less 10% of the VAT-exclusive amount | **Met** | `RefundTests` (rounding cases, rate limits), API test (R29.90 → R2.60 fee → R27.30), persistence checks |
| Q1 Full refund on manager request | **Met** | Accept test (R29.90 back, fee 0, kind RestaurantCancellation) |
| Q2 Unaccepted payments refunded in full | **Met** (except I4) | Late payment after cancellation refunded by the job; wrong amount skipped |
| Q3 Orders page and Home notice | **Met** | Manager and patron UI tests |
| Q4 Failed refunds handled by hand | **Met** | Attention list (API and UI); patron told to contact the restaurant |
| AC-7.3 lifecycle, AC-7.5 audit | **Met** | Transition matrix (6 allowed); refund and request rows audited with the order change |

## 5. Security controls with evidence

| Control | Evidence |
| --- | --- |
| Patron endpoints: PatronOnly, ownership, CSRF, strict JSON | Other patron 404, manager 403, anonymous 401, no CSRF 400; forged `refundAmount`, `feeRate`, `status` → 400 |
| Manager endpoints: ManagerOnly, CSRF, strict JSON | Patron 403, anonymous 401, no CSRF 400; unknown field `cancelNow` → 400 |
| Browser never decides an amount | `expectedRefund` compared only; a mismatch returns 409 with the server's figures and cancels nothing |
| One refund per payment | Unique index; simultaneous cancels give one refund and one Paystack request |
| Refund status only from Paystack | Webhook claims not trusted (test); a Processed amount different from ours is refused |
| Size and rate limits | 1 KB bodies (413 tests for cancel and manager request); per-patron limit on cancel, accept and decline (429 tests) |
| Safe errors | No secret, provider detail, "Exception" or stack trace in error responses (test) |
| Reason is plain text | Trimmed, ≤ 50 characters, no control characters (domain, database check, UI `maxLength`); React renders it as text |
| Logging | References and outcomes only: no reasons, emails, keys or payloads |
| Secrets | History scan clean; no new dependencies |

## 6. Test results (executed 20 November 2026)

Run by the developer on Windows 11 with Docker Desktop; Vitest also run by the assistant.

| Suite | Count | Result |
| --- | --- | --- |
| Backend unit + integration (Testcontainers PostgreSQL 17) | 777 (655 at M8 + 122) | Passed (777/777) after the fixes below |
| Vitest | 156 (131 at M8 + 25) | Passed |
| Playwright (with axe) | 9 | Passed |
| TypeScript, oxlint, build `-warnaserror` | – | Passed |

New in M8b (backend):

| Area | Tests |
| --- | --- |
| Domain | `RefundTests` 18, `CancellationRequestTests` 14, `OrderTests` +4 |
| Persistence | 20 |
| Settings | 7 |
| Paystack refund client | 10 |
| Patron cancellation API | 18 |
| Manager requests | 15 |
| Refund completion | 11 |
| Hardening | 5 |

New in M8b (Vitest): patron cancellation 13, manager Orders page 7, My orders 5. Playwright: the ordering journey now
also opens My orders and scans it with axe.

## 7. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| The Home-notice query sorted on a property of a record built inside the query, which EF Core cannot translate (4 tests failed, HTTP 500) | Sort before building the records (fixed in the refund-completion commit) |
| A recovery test expected a "Paystack down" run to change 0 rows, but the job now also closes other tests' expired requests | The test checks its own payment's state |
| Two frontend tests asserted too loosely ("completed" also matched "could not be completed"), or the mock did not reflect the cancelled state | Tests corrected; no code change |

## 8. Manual sandbox refund (20 November 2026)

The developer paid an order with Paystack's test card and cancelled it from the order page: the order was cancelled
and the refund was recorded and sent to Paystack. Completion is confirmed by Paystack later; the reconciliation job
checks it every 5 minutes (setup guide A6).

## 9. Open items

| Item | Needed from | Effect |
| --- | --- | --- |
| B1.1–B1.4, I4, I8 confirmation | Stakeholder | Provisional business rules, configurable or easily changed |
| Production Paystack account | Not needed | Refunds stay in test mode (demonstration store, decided 20 November 2026) |
| Emails for requests, cancellations and refunds | M9 | Patrons currently see requests on the order page and Home |
| Q-14 retention (POPIA) | Stakeholder | Requests and refunds are kept with their orders |

## 10. Integration notes for Milestone 9 (emails)

- **Triggers to add through an outbox, in the same transaction:**
  - cancellation (patron, or accepted request)
  - manager request created (ask the patron to answer)
  - refund Processed or Failed
- **Content:** the refund amount, fee and reference come from the `refunds` row; the reason from `cancellation_requests`.
- **Do not email the reason to anyone but the patron.**

## 11. Commit history (M8b)

| Commit | Description |
| --- | --- |
| `44676f5` | Cancellation and refund domain; M8b decisions recorded |
| `6a7c2d8` | `refunds` and `cancellation_requests` tables, `Refund.FeeRate` setting, migration |
| `c4f93a0` | Paystack refund client and patron cancellation |
| `4792010` | Manager requests, patron answers, refunds needing attention |
| `378b5cc` | Refund webhooks and reconciliation; Home-notice query fix |
| `375e9df` | React cancellation screens and Management → Orders |
| `b5a7256` | Hardening tests, My orders page (M11 brought forward) and M8b documentation |
| (this commit) | Test mode kept permanently (no production activation); reports finalised |
