# M13 Test Plan – System Integration and End-to-End Testing

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Task:** T13.1 (E2E test plan and scenarios mapped to roadmap Section 11)
**Date:** 21 November 2026 · **Status:** Approved (decisions D1 to D8, 21 Nov 2026)

## 1. Acceptance criteria (roadmap §5, M13)

| ID | Criterion |
| --- | --- |
| AC-13.1 | 100% of planned E2E scenarios executed; 0 open Critical or High defects. |
| AC-13.2 | Every mandatory requirement (MR-01 to MR-10) has at least one passing E2E test. |
| AC-13.3 | Security testing (OWASP Top 10 checklist and an automated scan) shows no unresolved High or Critical findings. |
| AC-13.4 | Performance smoke test: order and payment pages respond within 2 seconds at 50 concurrent users \[Rec target, to confirm – Q-15\]. |
| AC-13.5 | Database integrity checks show no orphaned orders, lines, payments or audit gaps. |

"Planned E2E scenarios" are the scenarios in section 5. No acceptance criteria are added beyond the roadmap's.

## 2. Test environments

| Code | Environment | Used for |
| --- | --- | --- |
| **L** | **Local E2E stack** (D1, D2, D3): React (Vite) → ASP.NET Core API in the `E2E` environment → disposable PostgreSQL 17 container built from migrations. A Paystack simulator sits at the API's `Payments:Paystack:BaseUrl`, and a disposable Mailpit captures email. No user secrets, no Supabase, no real Paystack key. Started by `npm run test:e2e:local` | New E2E journeys in Chromium, Firefox, WebKit and Mobile (Pixel 7); security scan; performance smoke |
| **S** | **Test environment**: React → API (Development) → Supabase, Paystack in test mode (`npm run test:e2e`) | Existing smoke and regression journeys (Chromium); final regression |
| **I** | Integration: ASP.NET Core TestServer → disposable PostgreSQL 17 (Testcontainers), fake provider | Boundary, concurrency, isolation, integrity and injection tests |
| **M** | Manual on S | Real Paystack sandbox checkout with the Paystack test card (D2) |

**The payment simulator only ever runs in the test browser and the local stack.** It answers the API's server-side
Paystack calls and signs webhooks with a throwaway test key. Playwright serves a test checkout page for
`checkout.paystack.com` in the test browser only. No application code or production setting is changed for it.

## 3. Entry and exit criteria

- **Entry:** the baseline was reproduced on 21 Nov 2026: backend 1,176, Vitest 205, Playwright 9; build with
  warnings as errors, lint, `npm audit` and the .NET vulnerability check all clean.
- **Exit:**
  - every scenario in section 5 executed, with its result recorded;
  - 0 open Critical or High defects;
  - AC-13.1 to AC-13.5 evidenced;
  - a full regression run on S.

## 4. Defect severity (D8)

| Severity | Meaning |
| --- | --- |
| Critical | Money handled wrongly (e.g. confirmed without verified payment, double refund), security breach, or data loss |
| High | A mandatory requirement (MR-01 to MR-10) does not work |
| Medium | A requirement works with a workaround, or a non-mandatory function fails |
| Low | Cosmetic or wording |

Defects are logged in `docs/testing/m13-defect-log.md` with reproduction steps, fix, retest and status.

## 5. Scenarios

| ID | Scenario | MR / FR | Env | Priority |
| --- | --- | --- | --- | --- |
| E2E-01 | Register, sign in, sign out; validation, invalid credentials, lockout | MR-07, FR-01, FR-02 | S, I | High |
| E2E-02 | Build an order: quantity limits, options, restaurant, pickup window, live quote | FR-03 to FR-05, FR-10 to FR-19 | S, L | High |
| E2E-03 | Place and pay. The browser's return alone does not confirm; after verified payment the order is Confirmed. One receipt arrives in Mailpit with the stored total and order link. History shows Paid and the manager report shows Paid | MR-05, MR-06, MR-08, MR-09, MR-10, FR-06, FR-07 | L (all browsers) | Critical |
| E2E-04 | Payment declined or abandoned: the order stays unpaid and payable with no receipt; paying again confirms it | MR-06, FR-06 | L | Critical |
| E2E-05 | Webhook before the browser returns; duplicate webhooks give one receipt; wrong amount needs a refund and does not confirm | MR-06 | L, I | Critical |
| E2E-06 | Paid order cancelled by the patron: fee and refund shown, refund processed, cancellation and refund emails, history "Refunded less fee" | MR-05, MR-06, MR-09 | L | High |
| E2E-07 | Manager cancellation request accepted (full refund) or declined; refund failure email | MR-05, MR-10 | L | High |
| E2E-08 | A fee or price change affects new quotes only; a placed order, its receipt and its report row are unchanged | MR-02, FR-22 | L, I | High |
| E2E-09 | Lookup and settings management; Audit Lookup shows who changed what from what to what | MR-03, MR-04, FR-21, FR-23, FR-27 | S, L | High |
| E2E-10 | Audit trail of a paid and refunded order, with system actors; audit history cannot be changed | MR-04, FR-09 | L, I | High |
| E2E-11 | Reports: orders on a date, trends, export file matching the screen | MR-10, FR-25, FR-26 | L, I | High |
| E2E-12 | Cross-patron isolation and role boundaries over real HTTP | MR-07 | I, S | Critical |
| E2E-13 | Database integrity after the full run (`tools/db/integrity-checks.sql`) | MR-01, AC-13.5 | L, I, Supabase (read-only, D7) | High |
| E2E-14 | Patron journey on a mobile viewport and by keyboard only, with axe checks | FR-28 | L | Medium |
| E2E-15 | Real Paystack sandbox payment with the Paystack test card | MR-06 | M | High |
| SEC-01 to SEC-10 | OWASP Top 10 (2021) checklist items, each with evidence; ZAP baseline and API scans | AC-13.3 | I, L | High |
| PERF-01 | 50 concurrent users: order page, options, quote, place order, start payment, order page | AC-13.4 | L (k6) | High |

## 6. Mandatory requirement coverage (AC-13.2)

| MR | Requirement | Scenarios |
| --- | --- | --- |
| MR-01 | Use of a database | E2E-03, E2E-13 |
| MR-02 | Configurable fees | E2E-08 |
| MR-03 | Lookup table manager | E2E-09 |
| MR-04 | Full auditing | E2E-09, E2E-10 |
| MR-05 | Confirmation email of orders and payments | E2E-03, E2E-06, E2E-07 |
| MR-06 | Payment gateway | E2E-03, E2E-04, E2E-05, E2E-15 |
| MR-07 | Two user profiles with two logins | E2E-01, E2E-12 |
| MR-08 | Final receipt by email | E2E-03 |
| MR-09 | Patron report | E2E-03, E2E-06 |
| MR-10 | Management reports | E2E-03, E2E-07, E2E-11 |

## 7. Evidence

- Logs: `.m13-logs/` (git-ignored on this machine).
- Results: `docs/testing/m13-integration-test-report.md`, `m13-security-report.md`, `m13-performance-report.md` and
  `m13-defect-log.md`, summarised in the M13 progress report.
