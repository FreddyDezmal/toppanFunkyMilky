# M13 Defect Log

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Severity scale:** test plan §4 (D8). Critical = money handled wrongly, security breach or data loss; High = a mandatory
requirement does not work; Medium = works with a workaround, or a non-mandatory function fails; Low = cosmetic or wording.

| ID | Found | Scenario | Severity | Summary | Status |
| --- | --- | --- | --- | --- | --- |
| DEF-01 | 21 Nov 2026 | E2E-03 (WebKit) | Low (test environment, not the product) | Paid-order journey timed out in WebKit on the return page | Fixed, retested (WebKit passed in 55 s) |
| DEF-02 | 21 Nov 2026 | E2E-03 (Firefox) | Low (test tooling, provisional) | Firefox page crashed during the axe accessibility scan | Open, diagnosing |

## DEF-01 – WebKit journey timed out on the return from checkout

- **Run:** `.m13-logs/e2e/2026-11-21T03-27-57-247Z`, project `webkit`, `payment.spec.ts:19`.
- **Observed:** after "Pay (test)", the browser returned to `/orders/MS-FK3J-XRPH?payment=return` and stayed on
  "Loading your order…" until the 120 s test timeout.
- **Cause (from the Playwright trace):** the local stack served the site with the Vite **dev server**. Each full page load
  in WebKit fetched the app's unbundled modules over HTTPS and took 15–20 s (`config.ts` alone 3.3 s). The journey has
  several full page loads; after the last one the page was still loading modules when the 120 s budget ran out, and it
  had not yet called `/api/ordering/orders/{reference}`. Every API call that was made answered 200 in under 1 s. The
  server confirmed the payment; the integrity checks for the run were all 0. Chromium passed the same test in 72 s.
- **Product impact:** none. Production serves the built bundle, not the dev server.
- **Fix:** `e2e-local/run.mjs` now runs `npm run build` and serves the production build with `vite preview` on the same
  address (`https://localhost:5173`); `vite.config.ts` gives `preview` the same port, HTTPS certificate and `/api` proxy
  as `server`. `npm run dev` is unchanged. This also makes the E2E stack test what will be deployed.
- **Retest:** `2026-11-21T03-38-06-993Z` – WebKit passed in 55 s (was a 120 s timeout); the whole run took 109 s
  instead of 215 s. **Closed.**

## DEF-02 – Firefox page crashed during the accessibility scan

- **Run:** `.m13-logs/e2e/2026-11-21T03-27-57-247Z`, project `firefox`, `payment.spec.ts:19`, at
  `expectNoSeriousAccessibilityProblems` (`support/stack.ts:101`): `frame.evaluate: Page crashed`.
- **Observed:** the payment had already been confirmed on screen (the scan runs after the "Paid" checks pass).
- **Likely cause:** memory pressure. Three Playwright workers ran browsers alongside the dev server, the API, PostgreSQL
  and Mailpit, and axe walks the whole DOM. Not yet confirmed.
- **Retest 1** (`2026-11-21T03-38-06-993Z`, production build): **recurred** at the same call. The test took 22 s, with no
  time or load pressure. The trace shows the crash 130 ms into `axe.runPartial`, after axe had loaded and the payment
  had been confirmed on screen. Chromium, WebKit and Mobile run the same scan on the same page and pass. **Revised
  cause:** a repeatable Firefox (Playwright build 159.0, revision 1572) content-process crash triggered by axe-core
  4.14.1, not memory and not an accessibility fault in the app.
- **Diagnosis:** temporary spec `e2e-local/specs/diag-firefox-axe.spec.ts`. It checks legacy mode, other pages, and
  each of the 65 WCAG 2 A/AA rules alone, and writes `firefox-axe-diagnosis.json` to the run's log folder.
