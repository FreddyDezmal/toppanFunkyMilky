# Decision Register – Before Milestone 8 (Payments, Cancellation and Refunds)

**Date:** 20 November 2026
**Decided by:** the developer. Items marked *provisional* are business rules that the stakeholder should ratify;
they are built as configurable or easily changed where practical.

## Payment (M8)

| ID | Decision | Status | Basis |
| --- | --- | --- | --- |
| A1 | Payment gateway: **Paystack** (ZAR, test mode, hosted checkout, HMAC-SHA512-signed webhooks, full and partial refunds via API) | Approved | Q-12 open; meets A-10 |
| A2 | An unpaid order cannot be paid once its pickup time has passed; the patron places a new order | Provisional | Prevents paying for a past pickup |
| A3 | Unpaid orders do not expire automatically | Provisional | Not specified (M7 decision O3) |
| A4 | The amount charged is always the order's stored total; prices are fixed when the order is placed | Approved | M7 immutable snapshots; AC-6.5 |

## Cancellation and refunds (B1, built in M8)

| ID | Decision | Status |
| --- | --- | --- |
| B1.1 | Patrons and managers can cancel an order **any time before the pickup time** | Provisional |
| B1.2 | A manager cannot cancel directly: the manager **requests** cancellation with a **reason (required, at most 50 characters)**; the order is cancelled only when the patron **accepts** the request. The patron may decline; an unanswered request **lapses at the pickup time** | Provisional |
| B1.3 | Cancelling an **unpaid** order costs nothing | Provisional |
| B1.4 | Cancelling a **paid** order refunds the amount paid **less a fee of 10% of the order's VAT-exclusive amount** (`taxable`), rounded once to cents like VAT; the rate is a setting (`Refund.FeeRate = 0.10`). Example: R52.00 + VAT R7.80 = R59.80 paid → fee R5.20 → refund R54.60 | Provisional |
| B1.5 | Cancellation and refunds are built together with payments in M8 | Approved |

## Other provisional rules (B2–B7)

| ID | Decision | Status |
| --- | --- | --- |
| B2 | Frequent-customer discounts stay switched off until the stakeholder gives tier rates (Q-01), the cap meaning (Q-02), which orders count (Q-03), before/after VAT (Q-04) and VAT-inclusive/exclusive fees (Q-07). Proposed: only paid (Confirmed) orders count (A-05) | Provisional |
| B3 | The M6 calculation sheet (`docs/pricing/m6-calculation-sign-off.csv`) goes to the stakeholder for Y/N sign-off (AC-6.6) | Pending stakeholder |
| B4 | No pickup capacity limit per restaurant or time slot | Provisional |
| B5 | A **Collected** status, set by a manager after pickup, is added before M12 reporting | Provisional |
| B6 | Orders are kept; on account deletion the patron's personal details are anonymised (POPIA; legal confirmation needed, Q-14) | Provisional |
| B7 | One combined email for order and payment confirmation (Q-08) | Provisional |

## Earlier milestones

| ID | Decision | Status |
| --- | --- | --- |
| C1 / DV-1 | AC-5.6: the Done button stays enabled and explains missing choices; recorded as an accepted deviation | Approved |

## M8 payment design (approved 20 November 2026)

| ID | Decision |
| --- | --- |
| Scope | M8a: payments now; M8b: cancellation and refunds (B1) straight after, with their own design review and the lifecycle change (paid Confirmed → Cancelled with refund) |
| P1 | Tables `payments` (one row per attempt) and `payment_events` (verified webhook receipts: event type, Paystack transaction id, reference, time; never the raw payload). Restrict-only foreign keys; audited |
| P2 | Attempt statuses: Initiating, Pending, Succeeded, Failed, Abandoned, Superseded, RequiresRefund |
| P3 | Partial unique indexes: one Succeeded payment per order; at most one open (Initiating/Pending) attempt per order. Extra or unacceptable charges are kept as RequiresRefund |
| P4 | Initiation: ownership and eligibility (awaits payment, pickup not passed – A2); check an open attempt with Paystack first; insert and commit an Initiating row with our reference before calling Paystack; no database transaction held during the call |
| P5 | Webhook: anonymous, CSRF-exempt, HMAC-SHA512 over the raw body (constant-time compare), size-limited, deduplicated by unique (event, transaction id), re-verified with Paystack's Verify Transaction API and checked for reference, amount in cents and currency |
| P6 | Payment and order change in one SaveChanges. Success → Confirmed; failure → PaymentFailed; abandoned → order unchanged; success on a cancelled or already paid order, or with a mismatched amount/currency → RequiresRefund, order untouched |
| P7 | Recovery: server-side verification when the patron returns or views an order with an open attempt, plus a background job every 5 minutes for open attempts up to 72 hours old |
| P8 | Redirect only to https://checkout.paystack.com; server-configured return URL; secret key server-side only |
| P9 | A payment started before the pickup time but verified after it still confirms the order (provisional) |
| P10 | Automated tests use a simulated Paystack HTTP client and test-signed webhooks; one manual sandbox payment with a Paystack test card |

## Open points

None. Both points were decided on 20 November 2026: the fee is 10% of the VAT-exclusive amount (better for the
patron), and unanswered manager requests lapse at the pickup time.

## M8b cancellation and refund design (approved 20 November 2026)

| ID | Decision |
| --- | --- |
| R1 | Lifecycle gains Confirmed → Cancelled; Cancelled is final; refund progress is shown separately from order status |
| R2 | Table `cancellation_requests`: order, manager, reason (required, ≤ 50 characters), status Pending / Accepted / Declined / Lapsed, timestamps; at most one Pending per order (database-enforced) |
| R3 | Table `refunds`: one per payment; amount paid, fee, fee rate used, refund amount (cents), reference `MSR-…`, Paystack refund id, status Initiating / Pending / Processed / Failed; audited; restrict-only foreign keys |
| R4 | Paid cancellation: order Cancelled + refund row committed together before Paystack is called; Processed only after Paystack confirms (signed, re-verified `refund.processed` / `refund.failed` webhook, or the reconciliation job) |
| R5 | Fee = `Refund.FeeRate` (setting, default 0.10, 0–0.5) × order `taxable`, rounded once to cents half away from zero; refund = amount paid − fee; rate stored on the refund |
| R6 | Unpaid cancellation first verifies any open payment attempt; if it was paid, the patron is shown the refund and confirms again |
| R7 | Cancellation only before the pickup time; pending manager requests lapse at the pickup time |
| R8 | Patron confirms a server-calculated fee and refund (`expectedRefund` compared only, `Idempotency-Key`); pending manager requests are accepted or declined on the order page |
| R9 | Ownership, Manager role, CSRF, rate limits, strict JSON; the reason is plain text; everything audited |
| R10 | Simulated Paystack refund client and test-signed refund webhooks in automated tests; one manual sandbox refund |
| Q1 | A cancellation requested by a manager and accepted by the patron is refunded **in full, without a fee** |
| Q2 | Payments that cannot be accepted (`RequiresRefund`) are refunded **automatically, in full, without a fee** |
| Q3 | Managers get **Management → Orders** (find an order by reference, request cancellation, see failed refunds); patrons see requests on the order page and a Home page notice; email follows in M9 |
| Q4 | A refund Paystack cannot complete is marked **Failed**; the patron is told to contact the restaurant; managers see it on the Orders page for manual handling (no bank details collected) |

## M8b implementation decisions (20 November 2026)

| ID | Decision |
| --- | --- |
| I1 | Cancel and accept carry no Idempotency-Key: repeating them is already harmless (cancelled stays cancelled; one refund per payment by unique index) |
| I3 | Paystack `needs-attention` refunds stay Pending, flagged for managers (not Failed) |
| I4 | A payment of a different amount is not refunded automatically; it is logged for a manual refund at Paystack (approved by the developer as an exception to Q2) |
| I8 | Managers see the patron's first name and email on Management → Orders (provisional, POPIA Q-14) |

Details: `docs/progress/2026-11-20_milestone-8b-cancellation-refunds-report.md`.
