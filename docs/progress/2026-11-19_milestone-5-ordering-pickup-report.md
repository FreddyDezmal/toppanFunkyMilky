# Progress Report – Milestone 5: Milkshake Ordering and Pickup Selection

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 19 November 2026
**Period covered:** 19 November 2026
**Status:** Milestone 5 complete – all Definition of Done criteria met; pickup rules are provisional and two
acceptance criteria are deliberately deferred or deviate (sections 4 and 10)
**Requirements traced:** URS §3.1 (place an order, select pickup restaurant), §3.4 field table (number of drinks,
flavour, thick-or-not, topping), roadmap AC-5.1 to AC-5.8, FR-03, FR-04

---

## 1. Summary

A signed-in patron can start a draft order, choose how many milkshakes they want within the configured limits, give each
milkshake exactly one flavour, consistency and topping from the active, priced options loaded from the backend, choose an
active restaurant and a pickup time, and review the draft. The review is checked by the server against the options and
settings in force at that moment; the browser never decides prices and never sends them. Nothing is ordered, stored or
paid: the draft lives in the browser tab until the patron signs out, and payment arrives in a later milestone.

| Area | Result |
| --- | --- |
| Options API | `GET /api/ordering/options`: active flavours, consistencies, toppings with current fees; active restaurants; drink limits; provisional pickup window; display locale and currency |
| Draft validation API | `POST /api/ordering/draft/validate`: stateless; quantity, one active choice of each kind per milkshake, active restaurant, pickup time; field-level errors; stores nothing |
| Pickup (provisional) | Business time zone setting; earliest = now + lead time; latest = 23:59 local, `MaxAdvanceDays` ahead; whole minutes; clock-change times rejected |
| Frontend | Four patron screens (`/order`, `/order/drinks`, `/order/pickup`, `/order/review`), Order link and Start an order button, draft kept per user in `sessionStorage` |
| Security | Patron-only, CSRF, strict JSON (unknown and duplicate fields rejected), per-patron rate limit (60/min), 16 KB body cap on validation |
| Database | No schema change; two new settings rows (`Pickup.TimeZone`, `Pickup.MaxAdvanceDays`) added by the seeder |
| Tests | 218 backend (xUnit), 95 frontend (Vitest), 8 end-to-end (Playwright) with axe scans – all passing |

---

## 2. Design decisions

| # | Decision | Rationale | Status |
| --- | --- | --- | --- |
| E1 | Active items without a current price in the pricing currency are left out of the patron catalogue and logged as a warning | Nothing is ever offered at an unknown or zero price | **Approved by developer** |
| E2 | The draft is kept in React state mirrored to `sessionStorage`: identifiers and pickup time only, keyed by user id, cleared on sign-out; no database tables | No order exists until M7; nothing price-related is trusted from the browser | **Approved by developer** |
| E3 | `PatronOnly` policy on every `/api/ordering/*` endpoint | Ordering is a patron function; whether managers may order is Q-19 | **Approved by developer** |
| E4 | Stateless `POST /api/ordering/draft/validate`; quantity = number of drinks sent; unknown JSON fields rejected; errors keyed by field (`drinks[2].toppingId`) | One authoritative check, reusable by M7; a forged price is refused, not ignored | **Approved by developer** |
| E5 | Show each choice's current fee, labelled as not final; no per-drink price, discount or total until M6 | VAT treatment (Q-07) and discount rules (Q-01 to Q-04) are unconfirmed | **Approved by developer** |
| E6 | Options reloaded when the ordering area opens and after a rejected review; stale selections cleared with a notice | Managers can deactivate items or change limits while a patron is ordering | **Approved by developer** |
| P1 | Provisional pickup rules (section 2.1) | URS gives no pickup rules beyond restaurant and time (Q-05, Q-06) | **Approved as provisional by developer**; M7 must recheck |
| F1 | Rate limit on ordering endpoints: 60 requests per minute per signed-in patron (`RateLimiting:OrderingPermitLimit`) | Each call runs five queries; per user so a shared address does not penalise patrons | **Approved by developer** (security review) |
| F2 | 16 KB request-body cap on draft validation; 413 ProblemDetails | The largest real draft (50 milkshakes) is about 4 KB | **Approved by developer** (security review) |
| T1 | Duplicate JSON properties rejected globally (`AllowDuplicateProperties = false`) | `{"a":1,"a":2}` is ambiguous; no existing client sends duplicates | Recommended, accepted |
| T2 | Pickup times exchanged as business-local `yyyy-MM-ddTHH:mm` strings; shown without converting through the browser's time zone | Matches `datetime-local`; seconds or offsets cannot slip in | Recommended, accepted |
| T3 | A local time skipped or repeated by a clock change is rejected rather than guessed | Technical safety; no effect for Africa/Johannesburg (no daylight saving) | Recommended, accepted |
| T4 | The rate limiter now runs after authentication (needed for per-user limits); login and registration remain per IP, 10/min | Cookie decryption only; guarded by the existing 429 test | Technical consequence of F1 |

### 2.1 Provisional pickup rules (pending Q-05, Q-06, Q-17, Q-18)

- All times are in the business time zone setting `Pickup.TimeZone` (IANA id, currently `Africa/Johannesburg`).
- Earliest = now + `Pickup.MinLeadTimeMinutes`, rounded up to the next whole minute.
- Latest = 23:59 local time, `Pickup.MaxAdvanceDays` days after today (currently 7; 0 = same day only).
- Whole minutes only. No opening hours, time slots or restaurant capacity are applied.
- The window is checked when options load, in the browser before review, and on the server at validation.
- M7 must check the pickup time again when the order is actually placed.

---

## 3. What was built

### Backend
- **Application (no EF Core):** `OrderingSettings` and `OrderingSettingsReader` (settings validated with the same
  `SettingRules` managers are held to; invalid settings make ordering unavailable rather than guessing), `PickupRules`
  (`WindowFor`, `Check`), `OrderDraft` and `OrderDraftRules` (pure), `IOrderDraftValidator` (loads the options in force,
  then applies the rules; for M7 to reuse).
- **Infrastructure:** `OrderingCatalogService` – one read of settings, the three catalogue lists with prices, and active
  restaurants; unpriced items logged and excluded (E1); invalid settings logged and reported as unavailable.
- **Settings:** `Pickup.TimeZone` (IANA, validated) and `Pickup.MaxAdvanceDays` (0–30) with management hints; seeded.
- **API:** `OrderingController` (`PatronOnly`, rate-limited), strict request DTOs, `RequestBodyLimitAttribute`
  (works on Kestrel and in the in-memory test host), `PatronOnly` policy, `Ordering` rate-limit policy.

| Endpoint | Access | Purpose |
| --- | --- | --- |
| `GET /api/ordering/options` | Patron | Active options with current fees, restaurants, drink limits, pickup window, display format; 503 if settings are invalid |
| `POST /api/ordering/draft/validate` | Patron + CSRF | Checks a draft; 200 with the resolved summary (`isDraft: true`), 400 with field errors, 413, 429, 503 |

### Frontend
- `features/ordering`: types, API calls (request copied field by field), draft reducer, `sessionStorage` persistence with
  sanitising on read, stale-selection reconciliation, error mapping, `OrderLayout` (loads options, step navigation,
  notices), `DrinkCard`, `EmptyDraft`, display formatting (`formatLocalTime`, `timeZoneLabel`, fees).
- Screens (`pages/ordering`): quantity, milkshakes (Done / Edit / Remove per milkshake), pickup (restaurant and
  `datetime-local` limited to the window, time zone named), review (server summary only, draft note, no totals,
  disabled "Continue to payment (coming soon)", Start over).
- Shared: `SelectField` component; `TextField` gained `max`; Order link for patrons; Start an order on the home page;
  sign-out clears every stored draft.
- Confirmations before dropping milkshakes that have choices, removing a milkshake and starting over.

---

## 4. Requirements traceability

| Criterion | Implementation | Status |
| --- | --- | --- |
| AC-5.1 Number of drinks compulsory, numeric, within min and max | Drop-down offering only the configured range; required; server checks the count | ✅ |
| AC-5.2 Changing the maximum takes effect without a code change | Limits read from settings on every request | ✅ (`DrinkLimits_FollowTheCurrentSettings`, `DrinkCount_IsCheckedAgainstTheLimitsInForceAtValidation`) |
| AC-5.3 Exactly N drink containers for N drinks | One card per milkshake | ✅ (Vitest "creates one card per milkshake") |
| AC-5.4 Flavour, Thick-or-not, Topping compulsory drop-downs from active lookups | Three required drop-downs per milkshake, active and priced options only | ✅ |
| AC-5.5 Price per drink, discount and total shown, read-only, refreshed on each selection | Current fee per choice shown and labelled not final | ⏳ **Deferred to M6** by decision E5 (Q-01 to Q-04, Q-07) |
| AC-5.6 Done enabled only when every field in the container is completed | Done is always enabled; pressing it with a missing choice names the missing field and moves focus to it | ⚠ **Deviation DV-1**, pending developer confirmation (section 10) |
| AC-5.7 Restaurant and pickup time required before payment; invalid times rejected | Both required before review; window enforced in the browser and on the server | ✅ (payment itself arrives in a later milestone) |
| AC-5.8 All validation repeated on the server; tampered requests rejected | `OrderDraftRules` on every validation; strict JSON; forged prices and ids rejected | ✅ |
| FR-03 Place an order (draft part) | Draft built and validated; order creation is M7 | ✅ for M5 scope |
| FR-04 Select pickup restaurant | Active restaurants only | ✅ |

| Item | Source |
| --- | --- |
| Drink limits 1–10, one flavour/consistency/topping per drink | URS §3.4 (limits configurable, M10) |
| Each drink configured independently; identical drinks allowed | Assumption in Q-11 (stated in the roadmap); developer brief |
| Pickup rules in section 2.1 | Provisional, approved by developer (Q-05, Q-06, Q-17, Q-18 open) |
| Fees shown, no totals | Decision E5 |
| Rate limit and body cap | Security review findings F1 and F2, approved by developer |

---

## 5. Security controls with evidence

| Control | Evidence (automated) |
| --- | --- |
| Every ordering endpoint requires the Patron role and the ordering rate limit | `EveryOrderingEndpoint_RequiresThePatronOnlyPolicy` (metadata test over all routes) |
| Anonymous 401, manager 403 | `Options_WhenAnonymous_Returns401`, `Options_ForAManagerOnlyAccount_Returns403`, `Validate_WhenAnonymous_Returns401`, `Validate_ForAManagerOnlyAccount_Returns403`; Playwright anonymous and manager checks |
| CSRF on validation | `Validate_WithoutAntiForgeryToken_Returns400`; Playwright from a real patron session |
| Prices never accepted from the browser | `ABrowserSuppliedPrice_IsRejectedNotIgnored`; Vitest "sends identifiers and the pickup time only"; Playwright forged-price check |
| Tampered and stale ids | `TamperedIds_AreRejected`, `FlavourDeactivatedAfterLoading_IsRejectedAsNoLongerAvailable`, `InactiveRestaurant_IsRejected`; Vitest reconciliation tests; Playwright tampered-storage check |
| Malformed JSON | `MalformedRequests_AreRejected` (wrong type, truncated, duplicate property), `PickupTime_InAnyOtherFormat_IsRejected` |
| Hostile sizes | `HundredsOfDrinks_GetOneQuantityMessage_NotHundreds`, `AnOversizedDraft_Returns413_WhileTheLargestRealDraftFits` |
| Rate limit per patron | `OrderingRequests_BeyondTheLimit_Return429_PerPatronNotPerAddress`; login limit unchanged (`Login_BeyondRateLimit_Returns429`) |
| Errors are ProblemDetails and never echo input | `ValidationErrors_AreProblemJson_AndNeverEchoWhatWasSent`; unknown, inactive and unpriced ids share one message |
| Invalid configuration never guessed | `InvalidSettings_MakeOrderingUnavailable_WithoutGuessing` (503; details only in the server log) |
| Validation stores nothing | `ValidDraft_ReturnsTheResolvedSummary_AndStoresNothing` (audit row count unchanged) |
| Browser storage holds no prices or personal data and is per user | Vitest storage and provider tests; Playwright refresh and sign-out checks |
| Accessibility | axe (WCAG 2 A/AA, serious/critical) on all four screens and on the milkshake screen at 375 px; no horizontal scrolling at 375 px |

**Accepted limitations:** a script injected through some future XSS flaw could read the draft (identifiers only, no
personal data); a chunked oversized body is stopped by Kestrel while being read rather than before; validation results
are not audited (nothing changes).

---

## 6. Test results (executed 19 November 2026)

| Suite | Result |
| --- | --- |
| Backend unit + integration (xUnit, Testcontainers PostgreSQL 17) | 218 passed, 0 failed (after F1/F2, commit `b4b7e47`) |
| Frontend component (Vitest 5) | 95 passed, 0 failed |
| End-to-end (Playwright, Chromium, real API + Supabase) | 8 passed, 0 failed with manager credentials set (6 passed, 2 skipped without) – run before F1/F2 were added; rerun at sign-off (section 12) |
| Accessibility (axe, WCAG 2 A/AA) | 0 serious or critical issues on Quantity, Milkshakes, Pickup, Review and Milkshakes at phone width |
| Frontend type-check and lint | `tsc -b` no errors; oxlint 0 warnings, 0 errors |
| Secret search of `frontend/src` and `frontend/e2e` | No connection strings or real credentials |

Growth from Milestone 10: backend 144 → 218 tests, frontend 46 → 95, end-to-end 4 → 8.

---

## 7. Database changes

No migration. Two settings rows are added by the seeder where missing: `Pickup.TimeZone` = `Africa/Johannesburg` and
`Pickup.MaxAdvanceDays` = 7. Both are editable in Management → Settings within their limits (IANA time zone; 0–30 days).
Nothing about a draft is ever written to the database.

---

## 8. Milestone 5 – Definition of Done

| Criterion | Status |
| --- | --- |
| Authenticated patrons can start a new milkshake draft | ✅ |
| Flavours, toppings and consistencies loaded from the backend | ✅ |
| Only active options available for new selections | ✅ (and only priced ones, E1) |
| Drink quantity follows configurable limits | ✅ |
| Each drink requires exactly one flavour, one consistency and one topping | ✅ |
| Patrons can configure multiple drinks independently | ✅ |
| Patrons can select an active restaurant | ✅ |
| Pickup selection follows confirmed or explicitly approved provisional rules | ✅ provisional rules approved (section 2.1) |
| Patrons can review and edit their draft | ✅ |
| Backend validation rejects invalid or stale selections | ✅ |
| Ordering screens accessible and responsive | ✅ (axe scans, 375 px check, keyboard and focus handling) |
| Existing authentication and security protections intact | ✅ (all earlier tests pass; login rate limit verified after the reorder) |
| No unconfirmed pricing or discount rules invented | ✅ (fees only; no totals, VAT or discount) |
| No real payment processed | ✅ (payment button disabled) |
| No completed order created prematurely | ✅ (validation stores nothing) |
| All existing tests continue to pass | ✅ |
| New backend, frontend and end-to-end tests pass | ✅ |
| Documentation updated | ✅ (README, this report) |
| No credentials or confidential documents committed | ✅ (section 12) |

---

## 9. Issues encountered and resolutions

| Issue | Cause | Resolution / lesson |
| --- | --- | --- |
| Patron received 503 from the options endpoint | The two new seeder lines were missing and `appsettings.Development.json` had a nested duplicate `Settings` block, so the pickup settings were never created | Seeder and JSON fixed; settings rows confirmed in Supabase; the 503 path itself proved the "never guess" design |
| `OrderDraftProvider` failed to compile | JSX saved in a `.ts` file | Renamed to `.tsx`; extension table now included with every frontend step |
| `StatusCodes` not found in a test | The integration-test project does not import ASP.NET Core's HTTP namespace implicitly | Used `HttpStatusCode` from `System.Net` |
| `[RequestSizeLimit]` would not be tested | The in-memory test host has no request-size feature (checked in its assembly) | `RequestBodyLimitAttribute` sets Kestrel's limit and also refuses an oversized `Content-Length` itself |
| Per-user rate limiting impossible as configured | The rate limiter ran before authentication | Moved after authentication; login limit unchanged and re-tested |
| Possible 429s in end-to-end runs | Registration and login share a 10-per-minute limit per IP | Ordering spec registers as few patrons as possible; wait a minute and rerun if needed |

---

## 10. Open questions and decisions needed

**Questions for stakeholders:**
- **Q-05:** restaurants' opening hours and capacity – no opening hours are applied to pickup times yet.
- **Q-06:** pickup-time rules (lead time, slot size, latest pickup, same-day only).
- **Q-07, Q-01 to Q-04:** VAT treatment of fees and discount rules – block AC-5.5 (prices, discount, total) until M6.
- **Q-11:** assumptions applied – each drink has its own choices; one restaurant per order.
- **Q-16:** must higher discount tiers have higher thresholds and rates? (raised in M10, still open).
- **Q-17 (new):** one business time zone for all restaurants, or one per restaurant?
- **Q-18 (new):** how far ahead may patrons book a pickup? (provisionally 7 days, configurable 0–30).
- **Q-19 (new):** may managers place orders, or patrons only? (currently patrons only).

**Decision needed from the developer:**
- **DV-1 – AC-5.6:** the roadmap says Done is enabled only when the milkshake is complete. M5 keeps Done enabled and,
  when pressed early, names each missing choice and moves focus to it (disabled buttons give keyboard and screen-reader
  users no explanation). Either keep this as an accepted deviation, or disable Done until all three choices are made.

---

## 11. Integration notes for later milestones

- **M6 (pricing and discounts):** compute per-drink price, VAT, discount and total on the server from the options in
  force (never from the browser); extend the validation summary or add a quote endpoint; then satisfy AC-5.5.
- **M7 (order creation):** reuse `IOrderDraftValidator` immediately before creating the order; recheck the pickup time;
  store the pickup instant in UTC with the business time zone; snapshot prices and the VAT rate; clear the browser draft
  after success. The draft request shape (`restaurantId`, `pickupLocal`, `drinks[]`) can be reused unchanged.
- **M8/M9 (payment, notifications):** enable "Continue to payment" only after M7 creates the order.
- **Q-17 answered "per restaurant":** move the time zone from settings to the restaurant record; `PickupRules` already
  takes the zone as input.
- **Rate limit:** checkout endpoints added under `/api/ordering` inherit the 60/min per-patron limit; review the figure
  when payment polling arrives.

---

## 12. Sign-off checks before the final commit

```powershell
git log --all -p | Select-String -Pattern 'Password=|pooler\.supabase\.com|service_role|sb_secret|PRIVATE KEY|MILKSHAKE_MANAGER_PASSWORD=|E2E_MANAGER_PASSWORD='
git ls-files | Select-String -Pattern '\.env|secrets\.json|appsettings\.(Production|Staging)'
cd frontend; npm run build
Select-String -CaseSensitive -Path dist/assets/*.js -Pattern 'Host=|Database=|Username=|supabase\.co|pooler'
npx playwright test
```

Expected: no output from the first and fourth commands; only `frontend/.env.example` from the second; all end-to-end
tests pass (manager checks skipped unless the credentials are set in the terminal).

---

## 13. Commit history (Milestone 5)

| Commit | Description |
| --- | --- |
| `b27757f` | Patron-only options API: current fees, active restaurants, limits, provisional pickup window, new settings |
| `4110b64` | Stateless draft validation: live limits, active selections, field-level errors, strict JSON |
| `0f98f07` | Provisional pickup-time validation in the business time zone; local and UTC summary |
| `23f9a09` | Client draft state: per-user `sessionStorage`, sanitising, stale-selection reconciliation, error mapping |
| `4b0d64f` | Patron ordering screens: quantity, milkshakes, pickup, server-validated review |
| `b4b7e47` | Per-patron ordering rate limit (F1) and 16 KB draft body cap (F2); also contains the Step 8 tests (end-to-end journey with axe, tamper and refresh checks; access and hostile-input tests) |
| *(pending)* | Documentation: README and this report |
