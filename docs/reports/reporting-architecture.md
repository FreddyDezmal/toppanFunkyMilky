# Reporting Architecture (Milestone 12)

**Scope:** patron order history (AC-12.1), the management dashboard with Orders, Trends and Audit Lookup tabs
(AC-12.2 to AC-12.5) and exports (AC-12.6), as asked for in URS §3.4.4, §3.4.7 and §4.
**Decisions:** `docs/decisions/2026-11-21_m12-reporting-decisions.md` (R1 to R15).

## 1. Principles

- **Figures come from what was stored when the order was placed.** Names, totals and times come from the order and
  order-item snapshots. Payment status comes from the stored order status and refund records. Reports never read
  today's catalogue or prices, and never derive figures from audit rows (R4).
- **Business-local days.** A report date means 00:00 to 24:00 in the business time zone setting
  (`Pickup.TimeZone`, Africa/Johannesburg), not the browser's zone and not UTC (R1). Dates become UTC bounds before
  querying, so the `(created_at, id)` index serves every query.
- **One payment-label rule** for the patron and the manager (R3): `PaymentLabels.For(status, refunds)` in
  `Application/Ordering/PaymentLabels.cs`.
- **No patron identity in manager reports** (R6). The order reference links to the existing order page.
- **Read-only, managers only, never cached, rate-limited, strictly parsed.** Patron history is limited to the signed-in
  patron.

## 2. Endpoints

| Endpoint | Who | Purpose | Limits |
| --- | --- | --- | --- |
| `GET /api/ordering/orders?cursor&limit` | Patron (own orders only) | Order history, newest first: drinks, total, order status, payment status | `limit` 1–50 (default 20); keyset cursor; ordering rate limit |
| `GET /api/manager/reports/orders?from&to&status&q&page&pageSize` | Manager | Orders placed on a date or in a range, one row per milkshake (URS columns + reference, pickup, restaurant) | Range ≤ 366 days; `pageSize` 1–50 (default 25); `q` ≤ 100 chars; 60/min |
| `GET /api/manager/reports/trends?from&to` | Manager | Orders and paid orders by weekday (Monday first), month and year; no dates = all orders | 60/min |
| `GET /api/manager/reports/orders/export` | Manager | The orders report as CSV (all rows of the filter) | ≤ 10,000 rows (422 above); 10/min |
| `GET /api/manager/reports/trends/export` | Manager | The trends as CSV | 10/min |
| `GET /api/manager/reports/audit/changes/export` | Manager | M11 change history as CSV, one line per changed field, masked | ≤ 10,000 records; 10/min |
| `GET /api/manager/reports/audit/security/export` | Manager | Sign-in events as CSV, masked | ≤ 10,000 events; 10/min |
| `GET /api/manager/audit/changes`, `/security` (M11, unchanged) | Manager | Audit Lookup tab | 60/min |

Unknown or repeated parameters return 400 with the field named. If the time zone setting is unusable, the reports
return 503 instead of guessing a zone.

## 3. Components

| Layer | Files |
| --- | --- |
| Application | `Ordering/OrderQueries.cs` (history page, cursor, parser), `Ordering/PaymentLabels.cs`, `Management/OrderReports.cs` (queries, parser, `ReportPeriod`, `OrderTrendBuilder`), `Management/ReportExports.cs` (`CsvWriter`, export service contract) |
| Infrastructure | `Ordering/OrderQueryService.cs`, `Ordering/RefundFactsReader.cs`, `Management/OrderReportService.cs`, `Management/ReportExportService.cs` |
| API | `Controllers/ManagerReportsController.cs`, `Contracts/Management/ReportContracts.cs`, `OrderingController.ListOrders`, policies `reports` and `report-exports` |
| Database | Index `ix_orders_created_at_id` (migration `20261120214814_AddOrderReportIndex`) |
| Frontend | `pages/orders/MyOrdersPage.tsx`, `pages/management/ReportsPage.tsx`, `features/management/{AuditPanel,TrendChart,ExportButton}.tsx`, `features/management/{reports,audit}.ts`, `lib/download.ts` |

## 4. How each report is built

**Patron history.** The query is keyset-paged on `(created_at DESC, id DESC)` over the patron's own orders, using the
existing `(patron_id, created_at)` index. It reads items through a split query. Refund facts are read for the page's
orders only, and the label rule is applied in memory. The cursor is opaque (base64url of ticks and id). A cursor taken
from another patron can only ever page through the caller's own orders.

**Orders report.** LINQ joins `orders` and `order_items` within the UTC bounds of the local dates, with an optional
status filter and a literal case-insensitive search (`ILIKE` with `%`, `_` and `\` escaped). Two counts are made: rows
(milkshakes) and distinct orders. Pages are numbered: OFFSET/LIMIT, ordered by placed time, order id and milkshake
number. Each row's times are converted to local time: placed time in the business zone, pickup time in the order's own
stored zone.

**Trends.** One parameterised SQL query groups orders by `(created_at AT TIME ZONE <business zone>)::date`, counting
all orders and those whose status is Confirmed (the Paid label). `OrderTrendBuilder` then folds the days into weekdays,
months and years, filling periods without orders with zero. This is the only new raw SQL. The M11 audit bypass scan
allow-lists it as one read-only `SqlQuery`.

**Exports.** `ReportExportService` calls the same report and audit services, so an export has exactly the screen's
rows, labels and masking. Audit exports page through the M11 service 1,000 records at a time and stop at 10,000.
`CsvWriter` produces RFC 4180 CSV, UTF-8 with a byte-order mark and CRLF lines. Any cell starting with `=`, `+`, `-`,
`@`, tab or carriage return gets a leading apostrophe (CSV formula injection). Each successful export writes a
`ReportExported` security event (who, when, IP address, trace ID) and a log line with the report name and record count,
under the same trace ID.

## 5. Payment-label rule (R3)

| Order status | Label | Refund detail |
| --- | --- | --- |
| PendingPayment, PaymentFailed | Unpaid | – |
| Confirmed | Paid | – (refunds of extra or unacceptable payments do not change it) |
| Cancelled, no refund | Cancelled | Not paid |
| Cancelled, cancellation refund Initiating/Pending | Cancelled | Refund in progress |
| Cancelled, refund Processed, fee kept | Cancelled | Refunded less fee |
| Cancelled, refund Processed, no fee | Cancelled | Refunded |
| Cancelled, refund Failed | Cancelled | Refund failed |

The cancellation's own refund decides the detail. If there is none, the newest refund of an unaccepted payment
decides it.

## 6. Dashboard (`/manager/reports`)

- A shared filter bar: **Single date / Date range**, date inputs and **Clear**. The tab and filters are kept in the
  address, so a view can be refreshed, bookmarked or shared.
- **Orders:** today by default. It has a payment status filter, search, numbered pages, Copy (the page as
  tab-separated text) and Export.
- **Trends:** with no date it covers all orders. Weekly, Monthly and Growth are hand-built SVG bar charts:
  - light bars are all orders, with paid orders in dark in front, as on the URS example screen;
  - each bar has a hover title, and each chart has a legend and a "Show the numbers" table;
  - the colours passed the data-viz palette check, apart from a contrast warning that the tables relieve.
- **Audit Lookup** has three views, all on the M11 audit API:
  - **Lookup changes:** one lookup table at a time; who changed which field from what to what, and when.
  - **All changes:** every AC-11.4 filter.
  - **Sign-in events.**
  The shared local dates are sent to the audit API as UTC instants (local midnight to the next local midnight).

## 7. Performance (R8)

`ManagerReportsTests.OverFiftyThousandOrders_EveryReportAnswersWithinThreeSeconds` seeds 50,000 orders (100,000
milkshakes) through 2045. It checks that every report shape answers in under 3 seconds: a full year, the last page,
a status filter, a search, a single day, trends for the year and trends for all orders. It also checks that a one-week
export takes under 3 seconds and that a year export is refused at the 10,000-row cap.

## 8. Reconciliation (T12.6)

Orders are placed through the API and then moved to fixed UTC times in 2041 to 2043. The tests check each report row
by row: dates, times, local-day boundaries (22:30 UTC falls on the next day; 21:59 UTC stays on the same day), payment
labels, filters, paging, search and trends. The totals are then compared with direct SQL that groups by local date in
the database.
