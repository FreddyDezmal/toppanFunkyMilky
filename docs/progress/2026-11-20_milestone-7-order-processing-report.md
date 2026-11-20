# Progress Report – Milestone 7: Order Processing and Database Transactions

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 20 November 2026
**Period covered:** 19–20 November 2026
**Status:** Milestone 7 complete. Orders are placed, stored atomically with immutable price snapshots, retried safely
and read back by their owner only. Nothing can mark an order paid; payment is Milestone 8.
**Requirements traced:** roadmap AC-7.1 to AC-7.6, T7.1 to T7.5, URS §2.2 and §3.4.6 (as cited by the roadmap)

---

## 1. Summary

- A patron reviews the server-calculated price, confirms it and places the order. The server revalidates every
  choice, the restaurant and the pickup time, reprices the order at one instant and stores it **unpaid**.
- The order and its items keep the names, fees, VAT rate, discount and total in force at placement. Later catalogue,
  restaurant or VAT changes do not alter them (tested).
- One submission creates at most one order, enforced by a unique database index, including for simultaneous
  requests and retries after a lost response.
- A changed price stops placement (409) until the patron confirms the new total. No price lock is promised.
- Patrons can read only their own orders; anything else is a 404.
- The URS v1.1 is not in the repository; requirements were taken from the roadmap's citations.

## 2. Decision register

| ID | Decision | Status | Basis |
| --- | --- | --- | --- |
| O1 | Statuses `PendingPayment` (new), `Confirmed`, `PaymentFailed`, `Cancelled`. Allowed: PendingPayment → Confirmed / PaymentFailed / Cancelled; PaymentFailed → Confirmed / Cancelled; all else rejected. No endpoint changes status in M7 | Approved | AC-7.3 example lifecycle; AC-7.4 |
| O2 | No separate payment-status column; M8 adds the payments table (T8.1). The API shows `status`, `isPaid` and `awaitsPayment` separately | Approved | Avoids two fields that could disagree |
| O3 | Unpaid orders do not expire and reserve no pickup capacity | Provisional | Not specified – **new open question** |
| O4 | Internal integer key plus a random public reference `MS-XXXX-XXXX` (Crockford base 32) used in URLs | Approved | Not specified; references cannot be guessed |
| O5 | `Idempotency-Key` header (UUID) stored on the order with a SHA-256 fingerprint of the content; unique index (patron, key); same content → original order (200), different → 422. Failed attempts store nothing, so the key can be retried | Approved | Simplest correct design |
| O6 | The request carries `expectedTotal` and `expectedCurrencyCode` for comparison only; any difference → 409 with `currentTotals`, nothing stored | Approved | Never trusts a browser amount |
| O7 | One `SaveChanges` (one transaction, Read Committed) for order, items and audit rows; the unique index, not a prior lookup, prevents duplicates; a retried commit becomes a replay | Approved | AC-7.1 |
| O8 | Snapshot schema with check constraints for the arithmetic, non-negative amounts, valid statuses and reference format; restrict-only foreign keys; indexes (patron, created_at) and (patron, status) | Approved | AC-7.2; future eligibility queries |
| O9 | `POST /api/ordering/orders`, `GET /api/ordering/orders/{reference}`, `GET /api/ordering/orders` (20 newest); another patron's order is 404 | Approved | T7.4, AC-7.6 |
| O10 | React: confirmation dialog, one request at a time, one key per draft content kept through timeouts and refreshes, draft cleared only after the order is stored | Approved | Prompt UX rules |
| O11 | Discount gate stays shut; unpaid orders never count towards eligibility | Approved | A-05, Q-03 |
| O12 | The e2e placement journey stores one real unpaid order per run for its own new patron; there is no delete endpoint | Approved | Avoids a test-only delete path |

## 3. Architecture

```
React review → ConfirmDialog → POST /api/ordering/orders  (Idempotency-Key, expectedTotal, identifiers only)
   ▼
OrderingController (PatronOnly, CSRF, Ordering rate limit, 16 KB, strict JSON, no-store)
   ▼
OrderPlacementService (Infrastructure)
   1. same patron + key already stored? → replay (same fingerprint) or 422
   2. IOrderPricingService.PriceDraftAsync → revalidate + reprice at one instant
   3. total/currency ≠ expected → 409 currentTotals (nothing stored)
   4. Order.Place(...) → one SaveChanges: orders + order_items + audit_logs
      unique (patron, key) violation → replay;  reference collision → new reference (max 3 tries)
   ▼
/orders/:reference ← GET /api/ordering/orders/{reference} (OrderQueryService, filtered by patron)
```

## 4. Database changes (migration `20261119191550_AddOrders`)

Purely additive; applied to Supabase after review (the Playwright placement journey runs against it).

| Table | Key contents | Constraints and indexes |
| --- | --- | --- |
| `orders` | reference, patron_id, status, restaurant id/name/address snapshot, pickup UTC + time zone, currency, subtotal, discount (amount, tier, rate, cap applied), taxable, VAT rate, VAT, total, priced_at_utc, idempotency_key, request_fingerprint, timestamps, xmin | checks: status list, reference format, amounts ≥ 0, discount ≤ subtotal, taxable = subtotal − discount, total = taxable + VAT, rates 0–1, tier/rate together, fingerprint format; unique reference; unique (patron, key); (patron, created_at); (patron, status); FKs to users and restaurants ON DELETE RESTRICT |
| `order_items` | order_id, number, flavour/consistency/topping id + name + fee snapshots, price | checks: number ≥ 1, fees ≥ 0, price = sum of fees; unique (order, number); FKs ON DELETE RESTRICT |

## 5. Requirements traceability

| AC | Status | Evidence |
| --- | --- | --- |
| AC-7.1 Single transaction; forced failure leaves no partial order | **Met** | `OrderPersistenceTests` (failure on last item), `OrderFailureRecoveryTests` (trigger fails the item insert through the real endpoint: no order, items or audit rows) |
| AC-7.2 Snapshot names, fees, VAT rate, discount; later changes leave orders unchanged | **Met** | `OrderPersistenceTests` and `OrderRetrievalTests` (rename, reprice, deactivate, restaurant rename, VAT change) |
| AC-7.3 Documented lifecycle; invalid transitions rejected | **Met** (lifecycle provisional for Q-10) | `OrderTests` transition matrix (5 allowed, 7 rejected); database status check |
| AC-7.4 Not Confirmed until payment succeeds | **Met** | No caller of `MarkConfirmed`; forged `status`/`isPaid` fields rejected; e2e shows "Not paid" |
| AC-7.5 Creation and every status change audited | **Met** | Audit rows for order and items in the same transaction; status-change audit test with old and new value |
| AC-7.6 A patron retrieves only their own orders | **Met** | `OrderRetrievalTests` (other patron, unknown, malformed → identical 404; lists isolated); e2e 200/404 check |

## 6. Security controls with evidence

| Control | Evidence |
| --- | --- |
| Browser never determines the amount | Expected total compared only; forged total, subtotal, VAT rate, discount, status, isPaid, patronId and reference fields → 400 (8-case theory); React sends identifiers only |
| Duplicate prevention in the database | Unique (patron, key) index; 5 simultaneous API submissions → one order; concurrent direct inserts → one row |
| Authentication, role, CSRF | Class-level `PatronOnly`; anonymous 401, manager 403, missing CSRF 400 on placement; 401/403 on both reads |
| Ownership | Every order query filters by the signed-in patron; other patrons' orders indistinguishable from missing ones |
| Abuse limits | Per-patron `Ordering` rate limit; 16 KB body limit (413 test); confirmation dialog and single in-flight request in React |
| Safe errors | Database failure → generic 500 ProblemDetails with no SQL, table, schema, Npgsql or exception text (tested) |
| Integrity in the database | Check constraints reject tampered totals, status, rates, references and item prices even from direct SQL (8 cases) |
| History cannot be deleted | Deleting an order, its patron, restaurant or flavour is refused (restrict foreign keys, 4 cases) |
| No payment data | Orders hold no card or payment details; no gateway code; nothing marks an order paid |
| Secrets and bundle | Tracked-file scan clean; built bundle has no server details, no source maps, no localStorage; sessionStorage holds the draft (IDs) and the submission key only |

## 7. Test results (executed 20 November 2026)

Run by the developer on Windows 11 with Docker Desktop. Results below are from the runs whose output was reviewed.

| Suite | Count | Result |
| --- | --- | --- |
| Backend unit + integration (Testcontainers PostgreSQL 17) | 549 (470 at M6 + 79) | Passed |
| Vitest | 110 (100 at M6 + 10) | Passed (110/110) after fixing a timing race in an M5 test; the first run had 109 passed, 1 failed |
| Playwright without manager credentials | 7 passed + 2 skipped | Passed (includes the new placement journey and its axe scan) |
| Playwright with manager credentials | 9 passed | Passed (9/9) |
| Build `-warnaserror`, `tsc -b`, oxlint | 0 warnings / errors | Passed |
| `npm audit --omit=dev`, `dotnet list package --vulnerable` | No vulnerabilities | Passed |

New in M7: domain 26 (`OrderTests`), fingerprint 3, persistence 18, placement API 23, retrieval 8, failure recovery 1;
Vitest 10 (placement 5, order page 2, submission key 3); Playwright placement journey with axe scan.

## 8. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| Integration tests failed with "Docker is either not running" (twice: once not started, once the engine not responding) | Started / restarted Docker Desktop; no code change; 549/549 afterwards |
| `OrderDraftProvider` test "removes drafts left in the tab by another user" failed once on a heavily loaded run | The test checked storage before the provider's cleanup effect had run; it now waits for it (`waitFor`). Behaviour unchanged |
| Linux shell showed whole-file diffs | Line endings only (Windows `core.autocrlf`); real diffs verified with `--ignore-cr-at-eol` |
| Parallel test requests could race the anti-forgery cookie | Concurrency test fetches one CSRF token and shares it |
| Intl's non-breaking space in a button's accessible name | Test compares the name with whitespace normalised |
| Constants exported from page components (fast-refresh lint) | Moved to `features/ordering/placement.ts` |

## 9. Open questions and deferred items

| Item | Needed from | Effect |
| --- | --- | --- |
| Q-10 Can patrons (or managers) cancel an order? Refunds? | Stakeholder | `Cancel()` exists but no endpoint calls it |
| New: Do unpaid orders expire, and after how long? (O3) | Stakeholder | Unpaid orders currently remain PendingPayment indefinitely |
| New: Is pickup capacity limited per restaurant or time slot? | Stakeholder | No reservation is made |
| New: Is a Completed/Collected status needed after pickup? | Stakeholder | Not in the roadmap's lifecycle; not built |
| Q-03 Which orders count towards frequent-customer tiers | Stakeholder | Eligibility source still `NoOrderHistoryYet` |
| Q-14 Data retention (POPIA) | Stakeholder | A patron with orders cannot be deleted (restrict foreign key); retention/anonymisation rules needed |
| Manager view of orders | M12 | Not in M7 acceptance criteria |
| Patron order list page | M11 | API ready (`GET /api/ordering/orders`); no UI yet |
| DV-1 (AC-5.6 Done button) | – | Decided 20 November 2026: kept as an accepted deviation (see M5 report) |
| AC-6.6 calculation sheet sign-off | Stakeholder | Unchanged |

## 10. Integration notes for Milestone 8 (payments)

- Pay only orders where `OrderStatusTransitions.AwaitsPayment(status)` is true; charge exactly `orders.total` in
  `orders.currency_code` – never an amount from the browser.
- Add the payments table (T8.1) referencing `orders.id` with the gateway reference and its own idempotency key.
- Call `Order.MarkConfirmed()` / `MarkPaymentFailed()` **only** from verified server-side gateway notifications;
  the domain rejects any other transition and the audit interceptor records the change.
- Replace the disabled "Pay now (coming soon)" button on `/orders/:reference`; `isPaid` becomes true only after
  `Confirmed`.
- Once Q-03 is answered, implement `IDiscountEligibilitySource` over Confirmed orders using the (patron, status) index.

## 11. Commit history (Milestone 7)

| Commit | Description |
| --- | --- |
| `536003e` | Order and order-item entities, immutable snapshots, status lifecycle, idempotency constraint, `AddOrders` migration |
| `b720275` | Transactional order placement: server repricing, price-change conflict, idempotent retries |
| `72d9cc8` | Patron-scoped order retrieval |
| `3b47036` | Forced mid-transaction failure test |
| `3da72a9` | React order placement and unpaid order page |
| (this commit) | M7 documentation and README; `OrderDraftProvider` test made to wait for its cleanup effect |
