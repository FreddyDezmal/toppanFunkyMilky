# M13 Integration Test Report – System Integration and End-to-End Testing

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Tasks:** T13.1 (plan), T13.2 (automated and manual E2E execution), T13.5 (defect fixing)
**Date:** 21 November 2026 · **Result:** every planned scenario executed and passing; 0 open Critical or High defects.

Related: [test plan](m13-test-plan.md) · [defect log](m13-defect-log.md) · [security report](m13-security-report.md) ·
[performance report](m13-performance-report.md)

## 1. Acceptance criteria

| ID | Criterion | Result | Evidence |
| --- | --- | --- | --- |
| AC-13.1 | 100% of planned E2E scenarios executed; 0 open Critical or High defects | **Met** | §3: 15 of 15 scenarios plus SEC and PERF executed and passing. Defect log: 6 defects, none Critical or High; 5 closed, DEF-04 (Low) open for M14 |
| AC-13.2 | Every mandatory requirement (MR-01 to MR-10) has at least one passing E2E test | **Met** | §4 |
| AC-13.3 | OWASP Top 10 checklist and automated scan: no unresolved High or Critical findings | **Met** | Security report: 0 High, 0 Critical; one Low (site HSTS) deferred to the production host |
| AC-13.4 | Order and payment pages within 2 s at 50 concurrent users | **Met** | Performance report: 95th percentile at most 161 ms per step, 0 failed requests |
| AC-13.5 | Database integrity checks show no orphaned orders, lines, payments or audit gaps | **Met** | 18 checks at 0 after every local run, and on Supabase (read-only) |
| Exit | Full regression run on the test environment | **Met** | §5: 9 of 9 on Supabase with Paystack test mode |

## 2. How the tests run

| Env | What | Command |
| --- | --- | --- |
| **L** Local E2E stack | Production build (HTTPS) → API (`E2E` environment) → disposable PostgreSQL 17 from migrations; Paystack simulator; Mailpit. 18 Playwright tests in Chromium, Firefox, WebKit and Pixel 7; integrity checks after the run | `npm run test:e2e:local` (`--perf`, `--zap`) |
| **S** Test environment | React → API (Development) → Supabase; Paystack test mode | `npm run test:e2e` |
| **I** Integration | ASP.NET Core TestServer → Testcontainers PostgreSQL 17 | `dotnet test` |
| **M** Manual | Real Paystack sandbox checkout with Paystack's test card | – |

The local stack holds no real keys or personal data. The Paystack simulator answers the API's server-side calls and
signs webhooks with a throwaway key; the test browser serves a test checkout page for `checkout.paystack.com`. No
application code behaves differently for it.

## 3. Scenario results

| ID | Scenario | Env | Tests | Result |
| --- | --- | --- | --- | --- |
| E2E-01 | Register, sign in, sign out; validation, invalid credentials, lockout | S, I | `e2e/auth.spec.ts`; `RegistrationTests`, `LoginAndSessionTests` (lockout, identical responses, rate limit) | Pass |
| E2E-02 | Build an order: quantity, options, restaurant, pickup window, live quote | S, L | `e2e/ordering.spec.ts` (draft, forged prices, phone layout); every L journey builds an order | Pass |
| E2E-03 | Pay: the browser's return alone confirms nothing; confirmed after verification; one receipt with the stored total and link; history and manager report show Paid | L (4 browsers) | `payment.spec.ts` "a paid order is confirmed only after server verification…" (Chromium, Firefox, WebKit, Pixel 7) | Pass |
| E2E-04 | Declined payment: unpaid, payable, no receipt; paying again confirms | L | `payment.spec.ts` "a declined payment…" | Pass |
| E2E-05 | Webhook before return; duplicate webhooks, one receipt; wrong amount held for refund | L, I | `payment.spec.ts` (3 tests); `PaystackWebhookTests` | Pass |
| E2E-06 | Patron cancels a paid order: fee and refund shown, refund processed, cancellation and refund emails, "Refunded less fee" | L | `cancellation.spec.ts` E2E-06 | Pass |
| E2E-07 | Manager request accepted (full refund) or declined; refund failure email | L | `cancellation.spec.ts` E2E-07 (3 tests) | Pass |
| E2E-08 | A price change affects new quotes only; the order, its receipt and its report row unchanged | L, I | `catalog-audit.spec.ts` E2E-08/09; `HistoricalAccuracyTests` | Pass |
| E2E-09 | Lookup management; Audit Lookup shows who changed what from what to what | L, S | `catalog-audit.spec.ts` E2E-08/09; `e2e/management.spec.ts` | Pass |
| E2E-10 | Audit trail of a paid and refunded order with system actors; audit history unchangeable | L, I | `catalog-audit.spec.ts` E2E-10; `AuditIntegrityTests` | Pass |
| E2E-11 | Reports: orders on a date, trends, export matching the screen | L, I | `reports.spec.ts` E2E-11 (runs after all others); `ManagerReportsTests` (SQL reconciliation) | Pass |
| E2E-12 | Cross-patron isolation and role boundaries over real HTTP | L, I, S | `isolation.spec.ts` (2 tests); `OrderingSecurityTests`, `ManagementSecurityTests` | Pass |
| E2E-13 | Database integrity after the full run | L, Supabase | `tools/db/integrity-checks.sql` after every L run; on Supabase in a read-only transaction (decision D7) | Pass (18 of 18 at 0, both) |
| E2E-14 | Patron journey on a phone and by keyboard only, with axe | L | `mobile-keyboard.spec.ts` (2 tests) | Pass |
| E2E-15 | Real Paystack sandbox payment with the test card | M | Order MS-2X9G-21TM, ZAR 62.10, Paystack reference MSP-T5XMB7S0QE8K, Visa ending 4081: order Confirmed and Paid; Paystack receipt received. Screenshots in `evidence/` | Pass |
| SEC-01 to 10 | OWASP Top 10 checklist; ZAP baseline and API scans | I, L | Security report | Pass |
| PERF-01 | 50 concurrent users | L (k6) | Performance report | Pass |

### Final runs

| Suite | Run | Result |
| --- | --- | --- |
| Local E2E (with ZAP) | `2026-11-21T06-15-17-905Z` | **18 of 18** passed (4.9 min); integrity 18 of 18 at 0; ZAP High 0, Medium 0 |
| Performance | `2026-11-21T05-53-08-262Z` | All thresholds met |
| Test environment (S) | 21 Nov 2026 | **9 of 9** passed |
| Backend (`dotnet test`) | 21 Nov 2026 | **1,180 of 1,180** passed (M12 baseline 1,176, plus 4 security-header tests) |
| Frontend unit (Vitest) | 21 Nov 2026 | **206 of 206** passed (M12 baseline 205, plus the DEF-03 regression test) |
| Lint, build, `npm audit`, NuGet vulnerability check | 21 Nov 2026 | Clean |

## 4. Mandatory requirement coverage (AC-13.2)

| MR | Requirement | Passing E2E tests |
| --- | --- | --- |
| MR-01 | Use of a database | E2E-03 (stored order, payment, receipt), E2E-13 (integrity) |
| MR-02 | Configurable fees | E2E-08 (price change, new quotes only) |
| MR-03 | Lookup table manager | E2E-09 (add, change price, rename, deactivate) |
| MR-04 | Full auditing | E2E-09 (lookup changes), E2E-10 (order trail, system actors, masking) |
| MR-05 | Confirmation email of orders and payments | E2E-03 (receipt), E2E-06 and E2E-07 (cancellation, refund and request emails) |
| MR-06 | Payment gateway | E2E-03, E2E-04, E2E-05 (simulator), E2E-15 (real Paystack sandbox) |
| MR-07 | Two user profiles with two logins | E2E-01, E2E-12 |
| MR-08 | Final receipt by email | E2E-03 (exactly one receipt with the stored total and order link) |
| MR-09 | Patron report | E2E-03, E2E-06 (My orders: Paid, Refunded less fee) |
| MR-10 | Management reports | E2E-03, E2E-07, E2E-11 |

## 5. Defects

Six defects were found and logged (`m13-defect-log.md`). None was Critical or High.

| ID | Severity | Summary | Status |
| --- | --- | --- | --- |
| DEF-01 | Low (test environment) | WebKit journey timed out on the Vite dev server | Closed: the stack serves the production build |
| DEF-02 | Low (test tooling) | Playwright's Firefox build crashes intermittently inside axe | Closed: axe skipped in Firefox only (recorded on the test) |
| DEF-03 | Low (wording; money protected) | Cancel dialog briefly showed unpaid figures after a payment was confirmed | Closed: fixed with a regression test |
| DEF-04 | Low (layout) | The live estimate pushes "Continue to pickup" down as it appears | **Open**: product change proposed for M14 |
| DEF-05 | Low (test environment) | Suite hit the sign-in rate limit from 127.0.0.1 | Closed: E2E environment only |
| DEF-06 | Medium (WCAG 2 AA 1.4.3) | Grey text on the page background below 4.5:1 | Closed: darker grey on 6 elements |

Product changes made in M13: DEF-03 fix, DEF-06 contrast, security headers on site and API with HSTS (security
report), and a deterministic batch in the reconciliation job.

## 6. Items for M14

| Item | Detail |
| --- | --- |
| Production host headers | Serve `frontend/security-headers.ts` and add `Strict-Transport-Security: max-age=31536000` |
| DEF-04 | Agree the layout change in UAT |
| Q-15 | Stakeholder to confirm performance and availability targets; repeat a smoke test on production (AC-14.3) |
| M12 sign-off items | Still open (M12 report §9) |
