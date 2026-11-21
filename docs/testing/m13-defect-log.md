# M13 Defect Log

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Severity scale:** test plan §4 (D8). Critical = money handled wrongly, security breach or data loss; High = a mandatory
requirement does not work; Medium = works with a workaround, or a non-mandatory function fails; Low = cosmetic or wording.

| ID | Found | Scenario | Severity | Summary | Status |
| --- | --- | --- | --- | --- | --- |
| DEF-01 | 21 Nov 2026 | E2E-03 (WebKit) | Low (test environment, not the product) | Paid-order journey timed out in WebKit on the return page | Fixed, retested (WebKit passed in 55 s) |
| DEF-02 | 21 Nov 2026 | E2E-03 (Firefox) | Low (test tooling, provisional) | Firefox page crashed during the axe accessibility scan | Closed (retest `2026-11-21T03-47-03-449Z`: 7 of 7 passed) |
| DEF-03 | 21 Nov 2026 | E2E-06 | Low (wording; money protected by the server) | Just after a payment is confirmed, the Cancel dialog briefly offered the unpaid wording ("nothing is charged") | Closed (retest `2026-11-21T04-03-22-923Z`: 4 of 4 passed) |
| DEF-04 | 21 Nov 2026 | E2E-03 (WebKit) | Low (layout) | The live estimate appears above "Continue to pickup" and pushes it down about 110 px, so a click at that moment misses | Open (Low): test waits for the estimate; product change proposed for M14 |
| DEF-05 | 21 Nov 2026 | E2E-06, E2E-07, E2E-10 | Low (test environment, not the product) | "Too many attempts" at sign-in and registration: every simulated user shares 127.0.0.1 | Closed (retest `2026-11-21T04-31-59-945Z`: 14 of 14 passed) |
| DEF-06 | 21 Nov 2026 | E2E-14 (phone) | Medium (accessibility: WCAG 2 AA 1.4.3 not met) | Loading messages and the footer were grey text on the pink page background below 4.5:1 contrast | Closed (retest `2026-11-21T04-47-51-794Z`: 18 of 18 passed) |

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
- **Diagnosis** (`2026-11-21T03-42-47-542Z`, temporary spec `diag-firefox-axe.spec.ts`, since removed): this time the
  full scan on the paid page **passed**, as did legacy mode (a single `axe.run`), the unpaid order page and the order
  page. Rules run one at a time passed until `aria-conditional-attr`, a simple attribute check, crashed the page; the
  next rule passed. The crash is **intermittent and not tied to a rule, page or scan mode**. (The diagnostic then hit
  the app's ordering rate limit, "Too many attempts", after about 20 page loads. That is the control working, not a
  defect.)
- **Resolution:** a Playwright Firefox engine instability under axe, outside the product. `expectNoSeriousAccessibilityProblems`
  (`support/stack.ts`) skips the axe scan in Firefox and records an "accessibility scan skipped" annotation on the
  test. axe inspects the DOM, so its findings do not depend on the engine: Chromium, WebKit and Mobile scan every page.
  Firefox still runs every functional check. Revisit if Playwright's Firefox build is updated.

## DEF-03 – Cancel dialog briefly showed unpaid figures after a payment was confirmed

- **Run:** `.m13-logs/e2e/2026-11-21T03-56-16-789Z`, `cancellation.spec.ts` (E2E-06, Chromium).
- **Observed:** straight after "Payment received. Your order is confirmed.", the patron opened Cancel order and the
  dialog said "You have not paid for this order, so nothing is charged. This cannot be undone."
- **Cause:** `CancellationPanel` reloads its details when the order's status changes, but it kept showing the previous
  (unpaid) details until the new ones arrived. For about two requests the page could show the unpaid cancellation
  offer for an order that had just been paid.
- **Impact:** wording only. Confirming would send `expectedRefund: 0`; the server compares it with its own figure,
  refuses with 409 and cancels nothing, then shows the real figures (decision R6). No money could be handled wrongly.
- **Fix:** the panel shows details only for the order status they were loaded for, or the server's answer to the
  patron's own action. While it reloads after a status change it shows nothing, and if that reload fails it shows
  "Cancellation details are unavailable right now." Regression test in `cancellation.test.tsx` ("never shows the
  unpaid figures while it reloads for the paid order"). The E2E helper now also waits for the order to show Paid, as
  the patron would.
- **Retest:** Vitest (cancellation tests), lint and `npm run test:e2e:local -- cancellation`
  (`2026-11-21T04-03-22-923Z`, 4 of 4, integrity checks all 0) passed on 21 Nov 2026. **Closed.**

## DEF-04 – The live estimate moves the Continue button as it appears

- **Run:** `.m13-logs/e2e/2026-11-21T04-12-49-193Z`, project `webkit`, `payment.spec.ts:19`, at `placeOrder`.
- **Observed:** the test clicked "Continue to pickup" and stayed on the milkshakes step until the timeout. No "choice
  missing" message: the click did not reach the button.
- **Cause (trace):** the click was aimed at the button at y 547–583. At that moment the quote for the completed
  milkshake arrived, and the Estimated price section grew by its price table, pushing the button to about y 674. The
  click landed on the estimate instead.
- **Impact:** a patron clicking at that moment needs a second click. Nothing is lost or charged. Layout only.
- **Test change:** `reviewOrder` waits for "Estimated total" before continuing, as a patron reads the price first.
- **Proposed product change (not made):** reserve the price table's space on the milkshakes page while the estimate
  loads, or place the Continue button above the estimate. To be agreed in M14 UAT.

## DEF-05 – Sign-in and registration refused as "Too many attempts" during the suite

- **Run:** `.m13-logs/e2e/2026-11-21T04-12-49-193Z`: E2E-06 and E2E-07 (manager sign-in) and E2E-10 (registration).
  Screenshot: "Too many attempts. Please wait a minute and try again."
- **Cause:** login and registration are limited to 10 a minute per client address (`RateLimiting:AuthPermitLimit`).
  On the local stack every simulated patron, and the manager, comes from 127.0.0.1, and the suite now signs in or
  registers more than 10 times a minute. The control worked as designed.
- **Fix:** `e2e-local/run.mjs` sets `RateLimiting__AuthPermitLimit=300` for the E2E API only. Production and
  development keep 10. The limit itself is proven by the integration tests (429 after the limit).
- **Retest:** full `npm run test:e2e:local` (`2026-11-21T04-31-59-945Z`): 14 of 14 passed, integrity checks all 0.
  **Closed.**

## DEF-06 – Text on the page background below WCAG AA contrast

- **Run:** `.m13-logs/e2e/2026-11-21T04-40-47-532Z`, `mobile-keyboard.spec.ts` (E2E-14, phone-sized screen). axe:
  "color-contrast: Elements must meet minimum color contrast ratio thresholds (1 element(s))" on Order – quantity.
- **Cause:** the scan ran while "Loading the menu…" was showing. It is `text-slate-500` (#64748b) directly on the page
  background `brand-50` (#fdf2f8): **4.36:1**, below the 4.5:1 that WCAG 2 AA (1.4.3) requires for normal text. On the
  white cards the same grey is 4.76:1, which is why earlier scans (taken after loading) passed. A review of other text
  drawn straight on the page background found the same loading style on four more pages, and the footer
  ("Development build", `slate-400`) at **2.35:1**. The footer is outside `<main>`, which the scans cover, so it had
  not been reported.
- **Fix:** those six elements use `text-slate-600` (#475569): 6.94:1 on the page background, 7.58:1 on white.
  `OrderLayout.tsx` (loading the menu), `OrderDetailsPage.tsx` (loading your order), `CatalogPage.tsx`,
  `DiscountTiersPage.tsx`, `RestaurantsPage.tsx` (loading lists) and `AppLayout.tsx` (footer).
- **Test change:** accessibility failures now name each element and axe's reason (for contrast, the measured ratio).
- **Retest:** full `npm run test:e2e:local` (`2026-11-21T04-47-51-794Z`: 18 of 18 passed, integrity checks all 0), Vitest and lint
  passed on 21 Nov 2026. **Closed.**
