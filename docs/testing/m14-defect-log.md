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
