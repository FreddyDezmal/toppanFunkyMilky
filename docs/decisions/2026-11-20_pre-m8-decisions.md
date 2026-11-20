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

## Open points

None. Both points were decided on 20 November 2026: the fee is 10% of the VAT-exclusive amount (better for the
patron), and unanswered manager requests lapse at the pickup time.
