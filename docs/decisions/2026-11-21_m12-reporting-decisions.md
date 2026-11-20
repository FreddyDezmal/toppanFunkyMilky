# Decision Register – Milestone 12 (Management Reporting and Order History)

**Date:** 20–21 November 2026
**Decided by:** the developer, on the assistant's recommendations. *Provisional* items await stakeholder ratification:
the roadmap requires stakeholder sign-off for M12.

**Requirements:** URS §3.4.4 (example report screens), §3.4.7 (reports), §4 (patron and management reports mandatory);
roadmap AC-12.1 to AC-12.6, T12.1 to T12.6, MR-09, MR-10.

| ID | Decision | Status |
| --- | --- | --- |
| R1 | **Reports use the time the order was placed**, in the business time zone setting (`Pickup.TimeZone`). A date means local 00:00 to 24:00. The orders report adds **Reference** and **Pickup** columns to the URS columns, and the export also carries **Restaurant** | Approved; extra columns provisional |
| R2 | **The orders list includes every order** (Paid, Unpaid and Cancelled, as on the URS screen) with an optional status filter. **Trend bars are two-tone:** all orders (light) with paid orders (dark), our reading of the URS example | Approved; two-tone reading provisional |
| R3 | **Payment labels follow the URS:** Paid, Unpaid (awaiting payment or payment failed) and Cancelled, with the refund outcome for cancelled orders (Not paid, Refund in progress, Refunded, Refunded less fee, Refund failed). One rule (`PaymentLabels`) for patrons and managers, from stored records only | Approved |
| R4 | **No financial totals** beyond each order's stored total in patron history. The roadmap asks for none, and audit rows and current prices are never used for figures | Approved |
| R5 | **Exports are CSV made on the server** with the screen's filters and masking: at most 10,000 records (422 above), formula-injection escaping, UTF-8 with a byte-order mark, a separate rate limit of 10 per minute. **Copy** copies the visible page as tab-separated text | Approved |
| R6 | **No patron identity in manager reports.** The order reference links to the existing order page | Approved |
| R7 | **Collected status (B5) is not added.** No M12 criterion needs it | Approved; stakeholder item |
| R8 | **Performance target: 3 seconds** on 50,000 orders (100,000 milkshakes), met with the additive index `(created_at, id)` (migration `AddOrderReportIndex`, applied to Supabase 21 Nov 2026) | Approved |
| R9 | **Patron history** is keyset-paged (20 per page, at most 50) with each order's drinks and payment label. `GET /api/ordering/orders` now returns `{ items, nextCursor }` | Approved |
| R10 | **The orders report has one row per milkshake** (flavour, topping and consistency belong to a milkshake), numbered pages (25, at most 50) and a literal search over flavour, consistency, topping, restaurant and reference. Totals show both orders and milkshakes | Approved |
| R11 | **Trends:** no date means all orders ("None" on the URS screen). Weekday, month and year series are filled with zeros. Paid means the Paid label (status Confirmed). The URS's "XX" placeholder charts are not built | Approved |
| R12 | **The Audit Lookup tab uses the M11 audit API unchanged.** Lookup changes are shown **one lookup table at a time**, because the API filters one table. A combined view would need a small API change | Approved; provisional |
| R13 | **Exports are recorded** as a `ReportExported` security event (who, when, IP address, trace ID); the report and record count go to the server log under the same trace ID. No migration: the event type is stored as text | Approved |
| R14 | **Audit exports live under `/api/manager/reports/audit/…`**, so the M11 audit controller is unchanged. Times are UTC (ISO 8601) and there is one line per changed field | Approved |
| R15 | **Reports need the business time zone setting.** If it is missing or invalid they return 503 instead of guessing | Approved |
