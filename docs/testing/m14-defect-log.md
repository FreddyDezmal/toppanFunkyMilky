# M14 Defect Log

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Severity scale:** as M13 (test plan §4, D8). Critical = money handled wrongly, security breach or data loss; High = a
mandatory requirement does not work; Medium = works with a workaround, or a non-mandatory function fails; Low =
cosmetic or wording. Numbering continues from the [M13 defect log](m13-defect-log.md). UAT defects will be logged
here as `UAT-DEF-nn`.

| ID | Found | Where | Severity | Summary | Status |
| --- | --- | --- | --- | --- | --- |
| DEF-04 | 21 Nov 2026 (M13) | Milkshakes page | Low (layout) | The live estimate pushed "Continue to pickup" down about 110 px as it appeared | Closed (retest `2026-11-21T08-51-33-655Z`) |
| DEF-07 | 21 Nov 2026 (M14 baseline) | Vitest `ordering-screens.test.tsx` | Low (test only) | "order page › shows the order as stored…" failed once: focus checked before the page's focus effect ran | Closed (Vitest 207 of 207, 21 Nov 2026 10:49) |
| DEF-08 | 21 Nov 2026 (first CI run) | Payment confirmation (webhook, return, reconciliation) | Medium | Two requests confirming the same payment at the same moment: the later one failed with HTTP 500 instead of "retry" | Fixed; local retest passed; CI retest pending |
| DEF-09 | 21 Nov 2026 (first CI run) | Vitest `my-orders.test.tsx` | Low (test only) | Expected "placed" time built with the machine's locale; the page uses the store's (en-ZA) | Fixed, awaiting CI retest |
| DEF-10 | 21 Nov 2026 (E2E `2026-11-21T10-25-58-944Z`) | Whole site (grey secondary text) | Low (accessibility) | `text-slate-500` on the pink page background is 4.36:1, under WCAG AA 4.5:1 (seen on "Checking your session…") | Closed (E2E `2026-11-21T10-42-51-261Z` 19 of 19) |
| DEF-11 | 21 Nov 2026 (CI image check) | Production image | Low (configuration) | `appsettings.Development.json` (development seed data and localhost CORS origin; no secrets) was published into the image | Fixed; CI retest pending |

## DEF-04 – The live estimate moved the Continue button (carried over from M13)

- **History:** M13 DEF-04 (`m13-defect-log.md`); fix chosen in M14 Step 2 (decision D-15).
- **Fix:** `PriceSummaryPlaceholder` (`features/ordering/PriceSummary.tsx`) renders the same four rows as the price
  table with "—" for the amounts. The milkshakes page (`OrderDrinksPage.tsx`) shows it until the first estimate
  arrives, so the section keeps its height. The placeholder is `aria-hidden`: the section's status line already
  announces "Updating prices…" or "Complete every milkshake to see the total." Its text uses `slate-600`, which meets
  WCAG AA contrast (DEF-06). The review page was checked: it renders its totals with the page, so it never shifted.
- **Regression tests:**
  - Vitest `ordering-screens.test.tsx` "keeps the price table's space while the estimate loads…": placeholder with
    four rows, `aria-hidden`, while the quote is held; replaced by four rows with the server's amounts.
  - Playwright `e2e-local/specs/layout.spec.ts`: holds the quote for 3 s, measures "Continue to pickup" while
    "Updating prices…" and again after the estimate; fails if it moves more than 1 px; then clicks it and reaches the
    pickup step.
  - The E2E helpers now wait for the real estimate (the status line "Estimated total R…") rather than the label, which
    the placeholder shows from the start.
- **Retest:** Vitest 207 of 207 (DEF-04 test passed); lint and build clean; `npm run test:e2e:local` `2026-11-21T08-51-33-655Z`: 19 of 19,
  including `layout.spec.ts` (8.1 s), integrity checks all 0. **Closed.**

## DEF-07 – Flaky focus assertion on the order page

- **Run:** M14 baseline, 21 Nov 2026 09:49 – Vitest 205 of 206. The same code passed 206 of 206 earlier that day.
- **Observed:** `expect(await screen.findByRole('heading', { name: 'Order MS-7KQ2-X9TD' })).toHaveFocus()` failed with
  focus still on `<body>`.
- **Cause:** `OrderDetailsPage` moves focus to the heading in a `useEffect` after the order renders. `findByRole`
  returns as soon as the heading exists, so on a busy machine the check could run one tick before the effect. The
  application behaves correctly: focus does arrive.
- **Fix:** the test waits for focus (`await waitFor(() => expect(heading).toHaveFocus())`). No other test asserts
  focus immediately after a `findBy…`.
- **Retest:** Vitest 207 of 207 on 21 Nov 2026 10:49. **Closed.** (One clean run; watched in later regressions.)

## M12 intermittent failures (status)

`OrderPlacementTests` retry test and one unnamed integration test each failed once in M12. Since then the full backend
suite has run five times (M13 ×4, M14 baseline) with no recurrence. The M14 baseline used `--logger trx`, so a
recurrence will name the test. **Status: not reproduced.**

## DEF-08 – Simultaneous confirmations of one payment: HTTP 500 for the later request

- **Found:** first CI run on GitHub (21 Nov 2026), `PaystackWebhookTests.SimultaneousDuplicates_StoreOneReceipt_AndConfirmOnce`:
  2 of 5 identical webhooks delivered at once got HTTP 500. The same test had always passed locally (timing).
- **Effect:** no data harm – the losing save is rolled back; one receipt, one confirmation email and the correct
  state were stored (the test's other assertions held). But Paystack (or the patron's browser) got a server error
  instead of "retry"/"in progress", and the error was logged as unhandled.
- **Cause:** confirming a payment queues the receipt email under a unique notification key. When two requests confirm
  the same payment, EF writes `email_notifications` (alphabetical) before the `payments`/`orders` rows whose version
  check reports the conflict, so the later save failed with a unique-key error that no caller handled. The same race
  exists on every path that confirms a payment (webhook, return from checkout, reconciliation).
- **Fix:** `MilkshakeDbContext.SaveChanges/SaveChangesAsync` report a clash on `ix_email_notifications_notification_key`
  as `DbUpdateConcurrencyException` – which it is: another request made the same change at the same moment. Every
  payment path already handles that (webhook → 503 so Paystack retries and then sees a duplicate; patron → "in
  progress"; reconciliation → next run).
- **Regression tests:** `PaystackWebhookTests.TwoConfirmationsAtTheSameMoment_TheLaterSaveIsAConcurrencyConflict`
  reproduces the race deterministically with two database contexts; `EmailNotificationPersistenceTests.TheSameEvent_CanOnlyQueueOneEmail`
  now expects the conflict and still checks the unique index caught it.
- **Retest:** local, 21 Nov 2026: `PaystackWebhookTests` and `EmailNotificationPersistenceTests` 30 of 30 (including the new
  race test); E2E against the production image 19 of 19 (`2026-11-21T10-42-51-261Z`). CI retest pending (next push).

## DEF-09 – Locale-dependent expectation in a Vitest test

- **Found:** first CI run: `my-orders.test.tsx` expected `Placed Nov 20, 2026, 9:00 AM` (the Linux runner's default
  locale) but the page showed `Placed 20 Nov 2026, 09:00` (en-ZA, the store's locale from the API). Passed locally
  because the developer machine's locale formats the same way as en-ZA.
- **Fix:** the test formats its expected value with `en-ZA`, as the page does. No application change.
- **Retest:** local Vitest 209 of 209 (the test passed on the developer machine before too); the CI run on the next push is
  the real retest, because only the Linux runner's locale exposed it.

## DEF-10 – Grey secondary text below WCAG AA contrast on the page background

- **Found:** `npm run test:e2e:local -- --container --backup-drill` (`2026-11-21T10-25-58-944Z`), E2E-14 on a phone:
  axe reported one `p` on "Order – quantity" with contrast 4.36:1 (`#62748e` on `#fdf2f8`, 14 px). The next test in
  the same serial group did not run.
- **Cause:** `RequireAuth` shows "Checking your session…" for a moment while the session loads; it sits directly on the
  pink page background (brand-50) in `text-slate-500`. axe only catches it when it scans during that moment, which is
  why earlier runs passed. `text-slate-500` (47 places) and `text-slate-400` (1 place) pass on white cards but not on
  the page background.
- **Fix:** every `text-slate-400/500` in the site is now `text-slate-600` (about 7:1 on brand-50), as already done for
  DEF-06 in M13. Purely visual; no behaviour change.
- **Regression test:** Vitest `app/text-contrast.test.ts` reads every component source and fails if grey text lighter
  than slate-600 is used, so the check no longer depends on timing. The E2E axe checks still run on every page.
- **Retest:** Vitest 209 of 209 including `text-contrast.test.ts`; lint and build clean; E2E against the production image
  `2026-11-21T10-42-51-261Z` 19 of 19 with the axe checks on every page (E2E-14 on a phone passed). **Closed.**

## DEF-11 – Development settings published into the production image

- **Found:** CI "Container image builds" job (first run of the image content check, 21 Nov 2026): the image contained
  `app/appsettings.Development.json`. The same run also listed the base image's public root certificates
  (`etc/ssl/certs/*.pem`) – a false positive of an over-broad check, not a defect.
- **Effect:** none at run time – Production does not load Development settings, and the file holds no secrets (only
  demonstration seed data and the `http://localhost:5173` CORS origin). But development configuration does not belong
  in a production image.
- **Fix:** `Milkshake.Api.csproj` marks the file `CopyToPublishDirectory="Never"` (still in the build output for local
  runs and tests; the deploy tool and E2E stack read it from the repository). The CI check now inspects only the app's
  own folder (`app/…`) for settings, secret, certificate and key files and test assemblies.
- **Retest:** the check's pattern was run against the failing listing (matches only the settings file) and a clean
  listing (no matches); local image rebuild and the next CI run pending.

