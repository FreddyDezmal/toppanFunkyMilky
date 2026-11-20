# Progress Report – Milestone 12: Management Reporting and Order History

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 21 November 2026
**Period covered:** 20–21 November 2026
**Status:** M12 built and tested. The roadmap requires **stakeholder sign-off** for M12 (section 9).
- **Patrons** see their full order history: drinks, total, order status and payment status.
- **Managers** have a Reports dashboard that matches the URS example screens:
  - **Orders:** a single date or a range, URS columns, status filter, search, numbered pages, Copy and Export.
  - **Trends:** weekly, monthly and growth charts with data tables.
  - **Audit Lookup:** lookup changes, all changes and sign-in events.
- Every report is checked row by row against seeded orders and reconciled with direct SQL. Each answers within
  3 seconds on 50,000 orders.

**Requirements traced:** roadmap AC-12.1 to AC-12.6, T12.1 to T12.6, MR-09, MR-10; URS §3.4.4, §3.4.7 and §4; M12
decisions R1 to R15 (`docs/decisions/2026-11-21_m12-reporting-decisions.md`).

Related: [reporting architecture](../reports/reporting-architecture.md) · [M11 report](2026-11-20_milestone-11-audit-report.md) ·
[audit architecture](../audit/audit-architecture.md)

---

## 1. Summary

- **Patron history (AC-12.1):** My orders shows 20 orders at a time with Load more. Each order shows when it was
  placed (business time zone), each milkshake, the pickup time, the total and "order status · payment". Only the
  patron's own orders are ever returned.
- **Orders report (AC-12.2):** one row per milkshake: Date, Time, Order, Flavour, Topping, Consistency, Pickup, and
  Payment status (Paid / Unpaid / Cancelled plus the refund outcome). Today is the default.
- **Trends (AC-12.3):** order volumes by weekday, month and year, all orders against paid orders, for a period or for
  all orders.
- **Lookup changes and audit report (AC-12.4, AC-12.5):** built on the M11 API, with every AC-11.4 filter and masking
  unchanged.
- **Search, paging, export, reconciliation (AC-12.6):** search and paging on every view; CSV exports that are
  formula-injection safe, capped at 10,000 records, separately rate-limited and recorded in the security log; Copy on
  the Orders tab; reconciliation tests against direct SQL.
- **Database:** one additive index, `(created_at, id)`, applied to Supabase. No data changed.

## 2. Decision register

Decisions R1 to R15 are in the M12 decision register. R1 (extra columns), R2 (two-tone trends) and R12 (one lookup
table at a time) are provisional until stakeholder sign-off.

## 3. Design

See the [reporting architecture](../reports/reporting-architecture.md). In short:

| Part | Contents |
| --- | --- |
| Patron history | `GET /api/ordering/orders?cursor&limit`, keyset paging, drinks and payment label from stored records |
| Reports API | `GET /api/manager/reports/orders` and `/trends`; managers only, no-store, 60/min, strict parsing, business-local dates |
| Exports | `…/orders/export`, `…/trends/export`, `…/audit/changes/export`, `…/audit/security/export`; 10/min, ≤ 10,000 records, `ReportExported` event |
| Payment labels | `PaymentLabels.For` shared by patron history and manager reports |
| Dashboard | `/manager/reports`: shared date filter, Orders / Trends / Audit Lookup tabs, state kept in the address |

## 4. Requirements traceability

| Requirement | Delivered | Evidence |
| --- | --- | --- |
| AC-12.1 patron history: date, drinks, total, payment status, nobody else's | My orders with paging, drinks, placed time and payment wording | `OrderRetrievalTests` (own orders only, paging with no gaps or repeats, another patron's cursor, invalid parameters, cancelled unpaid order); `OrderCancellationTests` (Paid → Refund in progress → Refunded less fee); `OrderHistoryTests` (labels, cursor, parser); `my-orders.test.tsx` |
| AC-12.2 orders for a date or range: date, time, flavour, topping, consistency, payment status | Orders tab and API | `ManagerReportsTests` (row-by-row content, local-midnight boundaries, filters, numbered pages, search); `reports.test.tsx` |
| AC-12.3 order volumes by day of the week for a period | Trends tab (weekly, monthly, growth) | `ManagerReportsTests` (weekday, month and year counts for a period, and "None" compared with SQL); `OrderReportTests` (zero-filling, Monday first); `reports.test.tsx` |
| AC-12.4 lookup changes: who changed what from what to what, and when | Audit Lookup → Lookup changes | `audit.test.tsx`; Playwright manager journey (the flavour it created appears) |
| AC-12.5 audit report with the AC-11.4 filters | Audit Lookup → All changes, Sign-in events | `audit.test.tsx`; M11 `ManagerAuditTests` (API) |
| AC-12.6 search, paging, export; totals reconcile with database queries on seeded data | Search and paging everywhere; Copy; CSV exports; reconciliation tests | `ManagerReportsTests` (direct-SQL reconciliation, exports, 10,000 cap, rate limit); `CsvWriterTests`; `exports.test.tsx` |
| T12.1 to T12.6 | Report queries, patron screen, Orders, Trends, Audit and reconciliation | As above |
| R8 performance | Under 3 s on 50,000 orders / 100,000 milkshakes | `OverFiftyThousandOrders_EveryReportAnswersWithinThreeSeconds` (includes a one-week export) |

## 5. Security controls with evidence

| Control | Evidence |
| --- | --- |
| Managers only; patrons see only their own history | Access tests on every report and export endpoint (patron 403, anonymous 401); history tests with another patron's cursor |
| Never cached | `no-store` asserted on history, reports and exports |
| Rate limits | `reports` 60/min and `report-exports` 10/min per manager, both tested; history keeps the ordering limit |
| Strict input | Unknown or repeated parameters refused; dates, 366-day ranges, pages and search length bounded (unit and integration tests); literal search (`%` and `_` tested) |
| Parameterised SQL only | One new raw query (trends), parameterised; audit bypass scan updated (`AuditBypassScanTests`) |
| No patron identity in reports | Row fields asserted; no `@` in rows or exports |
| Masking in audit views and exports | Patron emails exported as `***` (tested); managers shown in full, as in M11 A4 |
| CSV formula injection | `CsvWriterTests` (`=`, `+`, `-`, `@`, tab, CR, quoting) |
| Exports recorded | `ReportExported` security event asserted after an export |
| Cross-site requests | Exports are GETs behind `SameSite=Strict` session cookies |
| No personal data in logs | `Microsoft.AspNetCore` at Warning (request URLs with filters not logged); EF sensitive-data logging off; export log holds report name and count only |
| Secrets | `git grep` over tracked files and the built bundle: none (only known false positives) |
| Dependencies | `npm audit`: 0; `dotnet list package --vulnerable`: none |
| Accessibility | Playwright axe checks (WCAG 2 A/AA, serious and critical) on Reports Orders, Trends and Audit Lookup |

## 6. Test results (executed 20–21 November 2026)

| Suite | Result |
| --- | --- |
| Backend (`dotnet test`) | **1,176 passed, 0 failed** (673 unit, 503 integration), final run 21 Nov 2026 |
| Frontend unit (`npm test`) | **205 passed** in 22 files; also passes with `--maxWorkers=2` |
| Lint (`npm run lint`) | 0 warnings, 0 errors |
| Build (`npm run build`) | Succeeded |
| End-to-end (Playwright, with manager credentials) | **9 passed** |
| `npm audit` / `dotnet list package --vulnerable` | 0 / none |

Baseline before M12: backend 1,050, Vitest 164, Playwright 9.

## 7. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| `HistoricalAccuracyTests` read the old history list shape | Updated to read `items` |
| `OrderPlacementTests` retry test failed once with 400 on both requests | Passed on two reruns with no change; cause not identified (intermittent) |
| Build error CS0102: response records had both a `From` property and a `From` factory | Factories renamed `Create` |
| Lint warning (effect depended on a function) in the Audit Lookup panel | Effect now depends on plain values only |
| Vitest workers timed out while the machine was busy with the backend suite | Clean rerun passed (22 files); `--maxWorkers=2` documented as a fallback |
| One integration test failed once during the Step 8 run | Not reproduced: 1,176 of 1,176 on rerun. The test's name was not captured; if it recurs, `dotnet test 2>&1 \| Out-File test.log` will identify it |
| Security events have no details column | Export details go to the server log under the event's trace ID (R13); no migration |

## 8. Database

| Change | Migration | Supabase |
| --- | --- | --- |
| Index `ix_orders_created_at_id` on `orders (created_at, id)` | `20261120214814_AddOrderReportIndex` (reviewed with `migrations script`: one `CREATE INDEX` plus the history row) | Applied 21 Nov 2026 with approval |
| `ReportExported` security event type | None (stored as text, max 30) | – |

## 9. Stakeholder sign-off items

| # | Item | Current behaviour | Needed |
| --- | --- | --- | --- |
| 1 | Collected status (B5) | Not added; an order stays Paid after pickup | Decide whether a Collected status is wanted |
| 2 | Retention and masking (Q-14, M11 A4) | Audit and security records kept indefinitely; personal data masked in views and exports | Ratify retention (POPIA) and masking |
| 3 | Two-tone trend bars (R2) | Light = all orders, dark = paid orders | Confirm this reading of the URS example |
| 4 | Extra report columns (R1) | Order reference and pickup time on screen; restaurant also in the export | Confirm or trim |
| 5 | Report date basis (R1) | When the order was placed, business time zone | Confirm (the alternative is pickup time) |
| 6 | Payment labels (R3) | Unpaid covers awaiting payment and payment failed; refund outcome shown for cancelled orders | Confirm wording |
| 7 | Audit Lookup (R12) | Lookup changes shown one table at a time | Confirm, or ask for a combined view (small API change) |
| 8 | Exports (R5, R13) | CSV, at most 10,000 records; recorded as `ReportExported` | Confirm cap and format |
| 9 | URS "XX" charts (R11) | Only Weekly, Monthly and Growth are built | Confirm no further charts are needed |

## 10. Commit history (M12)

| Commit | Description |
| --- | --- |
| f065ad4 | Patron order history: cursor paging, drinks and payment status (AC-12.1) |
| 87f6754 | Manager reports API, reconciliation and 50k timing tests, `created_at` index (AC-12.2, AC-12.3) |
| a260ed6 | Reports dashboard: Orders and Trends tabs; order lookup by link |
| 39b3cfa | Audit Lookup tab (AC-12.4, AC-12.5) |
| 49fb7b0 | CSV exports and Copy (AC-12.6) |
| 5283d56 | Shared refund helper for patron history; regression and security review |
| (this commit) | M12 documentation |
