# Progress Report – Milestone 13: System Integration and End-to-End Testing

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 21 November 2026
**Period covered:** 21 November 2026
**Status:** M13 complete. AC-13.1 to AC-13.5 are met and the full regression on the test environment passes. 0 open
Critical or High defects. One Low defect (DEF-04) and the website's HSTS header carry over to M14.

**Requirements traced:** roadmap AC-13.1 to AC-13.5, T13.1 to T13.5, MR-01 to MR-10; risks R-05 and R-07; test plan
decisions D1 to D8 (`docs/testing/m13-test-plan.md`).

Related: [integration test report](../testing/m13-integration-test-report.md) ·
[security report](../testing/m13-security-report.md) · [performance report](../testing/m13-performance-report.md) ·
[defect log](../testing/m13-defect-log.md) · [M12 report](2026-11-21_milestone-12-reporting-report.md)

---

## 1. Summary

- **Local E2E stack** (`npm run test:e2e:local`): the production build over HTTPS, the API in an `E2E` environment, a
  disposable PostgreSQL 17 built from migrations, a Paystack simulator and Mailpit. No real keys or personal data.
  Everything is created for the run and removed after it. Options run the k6 load test (`--perf`) and the OWASP ZAP
  scans (`--zap`) against the same stack.
- **18 E2E journeys** cover payments (in Chromium, Firefox, WebKit and Pixel 7), cancellations and refunds, price and
  lookup changes, audit trails, reports and exports, isolation and role boundaries, and phone and keyboard-only use.
  The 9 earlier journeys run against Supabase with Paystack test mode. One real Paystack sandbox payment was made by
  hand.
- **Integrity:** 18 read-only SQL checks (orphans, payment and refund consistency, audit coverage) run after every
  local run, and once on Supabase. All are 0.
- **Security:** OWASP Top 10 checklist passes; ZAP found 0 High and 0 Critical. The two Medium findings (CSP,
  clickjacking) and the Low header findings were fixed with security headers on the site and the API.
- **Performance:** at 50 concurrent patrons the slowest step's 95th percentile is 161 ms (target 2 s), with 0 failed
  requests.
- **Product fixes from testing:** stale cancellation figures after a payment (DEF-03), text contrast below WCAG AA
  (DEF-06), security headers and HSTS, and a deterministic batch in the reconciliation job.

## 2. Acceptance criteria

| ID | Criterion | Result | Evidence |
| --- | --- | --- | --- |
| AC-13.1 | 100% of planned scenarios executed; 0 open Critical or High defects | **Met** | 15 of 15 scenarios plus SEC and PERF; 6 defects, none Critical or High |
| AC-13.2 | Every MR-01 to MR-10 has a passing E2E test | **Met** | Integration test report §4 |
| AC-13.3 | Security testing: no unresolved High or Critical findings | **Met** | Security report |
| AC-13.4 | Order and payment pages within 2 s at 50 concurrent users \[Q-15\] | **Met** | Performance report |
| AC-13.5 | No orphaned orders, lines, payments or audit gaps | **Met** | 18 checks at 0 locally and on Supabase |
| Sign-off test | Full regression on the test environment | **Met** | 9 of 9 on Supabase, 21 Nov 2026 |

## 3. Test results (21 November 2026)

| Suite | Result |
| --- | --- |
| Backend (`dotnet test`) | **1,180 passed**, 0 failed (M12: 1,176) |
| Frontend unit (`npm test`) | **206 passed** in 22 files (M12: 205) |
| Lint, build | 0 warnings, 0 errors; build succeeded |
| Local E2E (`npm run test:e2e:local -- --zap`, run `2026-11-21T06-15-17-905Z`) | **18 passed**; integrity checks 18 of 18 at 0 |
| Test environment E2E (`npm run test:e2e`, Supabase) | **9 passed** |
| Performance (`--perf`, run `2026-11-21T05-53-08-262Z`) | All thresholds met; 5,702 requests, 0 failed |
| ZAP baseline and API scans | High 0, Medium 0; Low 1 (site HSTS, deferred) |
| E2E-15 manual sandbox payment | Order MS-2X9G-21TM paid (ZAR 62.10, Paystack MSP-T5XMB7S0QE8K): Confirmed and Paid; receipt email with R62,10 total paid and the payment reference |
| Supabase integrity (read-only) | 18 of 18 at 0 |
| `npm audit` / `dotnet list package --vulnerable --include-transitive` | 0 / none |
| Secrets scan (`git grep`) | None found |

## 4. Defects

| ID | Severity | Summary | Status |
| --- | --- | --- | --- |
| DEF-01 | Low (test environment) | WebKit timed out on the Vite dev server's unbundled modules | Closed: the stack serves the production build |
| DEF-02 | Low (test tooling) | Playwright's Firefox build crashes at random inside axe | Closed: axe skipped in Firefox only; three other engines scan every page |
| DEF-03 | Low (money protected by the server) | After a payment, the Cancel dialog briefly offered the unpaid wording | Closed: panel shows details only for the current status; regression test |
| DEF-04 | Low (layout) | The live estimate pushes "Continue to pickup" down as it appears | **Open**: proposed fix for M14 UAT |
| DEF-05 | Low (test environment) | Suite hit the sign-in limit from 127.0.0.1 | Closed: E2E environment only raises it |
| DEF-06 | Medium (WCAG 2 AA 1.4.3) | Grey text on the page background at 4.36:1 and 2.35:1 | Closed: 6.94:1 |

## 5. Design and tooling added

| Part | Contents |
| --- | --- |
| `frontend/e2e-local/run.mjs` | Builds and removes the local stack; runs Playwright, then `--perf` (k6) and `--zap` (ZAP), then the integrity checks. Reachability check from Docker, time limits, container clean-up |
| `frontend/e2e-local/support/paystack-simulator.mjs` | Answers the API's Paystack calls and signs webhooks with a throwaway key; test checkout page served only in the test browser |
| `frontend/e2e-local/specs/` | `payment`, `cancellation`, `catalog-audit`, `reports`, `isolation`, `mobile-keyboard` (18 tests) |
| `frontend/e2e-local/perf/order-and-pay.js` | k6 scenario, 50 patrons |
| `tools/db/integrity-checks.sql` | 18 read-only checks (AC-13.5) |
| `playwright.local.config.ts` | Four browser projects; `reports` runs after all others so totals are stable |
| `backend/.../Security/SecurityHeaders.cs`, `frontend/security-headers.ts` | Security headers for the API and the site |

The `E2E` environment publishes the OpenAPI document (for the ZAP API scan) and raises the sign-in limit. Development
and production are unchanged apart from the security headers and HSTS.

## 6. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| Docker could not reach the site: `vite preview` on `localhost` listened on IPv6 only | E2E preview binds `127.0.0.1`; runner checks reachability first |
| A failing k6 request looped without pause (87 MB log); VS Code crashed during that session | Failure paths pause; k6 aborts in `setup()` if unreachable; ZAP scans have hard time limits; heavy runs from a standalone terminal |
| k6 cleared cookies between iterations, signing patrons out | `noCookiesReset: true` |
| Some file copies to the project folder reported success without overwriting | Existing files are now edited in place, and checked by checksum |
| A line-ending bug in an edit left stray carriage returns in `SecurityPipelineTests.cs` (commit `dc1c51b`) | Repaired: content unchanged, line endings only |
| First test-environment regression: manager sign-in refused | The environment variable held a different email from the real manager account; rerun with the correct one passed |

## 7. Items for M14

| # | Item | Needed |
| --- | --- | --- |
| 1 | Website security headers on the production host | Serve `frontend/security-headers.ts`, plus `Strict-Transport-Security: max-age=31536000` |
| 2 | DEF-04 layout | Agree in UAT: reserve the price table's space, or move the button above it |
| 3 | Q-15 targets | Stakeholder to confirm; repeat a smoke test on production (AC-14.3) |
| 4 | M12 sign-off items | M12 report §9 (Collected status, retention and masking, report wording, exports) |
| 5 | Monitoring and alerting | AC-14.4 |

## 8. Commit history (M13)

| Commit | Description |
| --- | --- |
| bdf2616 | Local E2E stack and payment journeys (E2E-03 to E2E-05), integrity checks, test plan and defect log |
| bd5c12b | Cancellation and refund journeys (E2E-06, E2E-07); DEF-03 fix |
| 76f391e | Lookup, price and audit journeys (E2E-08 to E2E-10); reports and exports (E2E-11) |
| f9b8be9 | Isolation and role boundaries (E2E-12); phone and keyboard-only journeys (E2E-14); DEF-06 fix |
| 23118e1 | PERF-01 (k6) and security scan tooling (ZAP) |
| dc1c51b | Security response headers on the API and the site |
| f3e8e8a | Security and performance reports; reconciliation batch ordering |
| (this commit) | Integration test report, M13 progress report, E2E-15 evidence; line-ending repair |
