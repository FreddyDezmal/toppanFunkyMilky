# Progress Report – Milestone 10: Lookup and Configuration Management

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 19 November 2026
**Period covered:** 18–19 November 2026
**Status:** Milestone 10 complete – all Definition of Done criteria met (two items depend on later milestones, see section 8)
**Requirements traced:** URS §3.4 (flavours, toppings, consistencies and their prices), §3.4.5 (frequent-customer
discount tiers), §3.4.6 (management-configurable values), §3.1 (pickup restaurants), §3.3 and §4 (auditing)

---

## 1. Summary

Managers can maintain everything patrons will choose from and every configurable value the ordering process depends on,
through a manager-only API and an accessible management area in React. Prices are effective-dated: a new price starts now
or in the future, prices that have taken effect are never edited, and a scheduled price can be cancelled before it starts.
Every change is audited field by field, and every change is protected against two managers overwriting each other.
Frequent-customer discount tiers can be prepared but not switched on until the discount rules are confirmed.

| Area | Result |
| --- | --- |
| Catalogue | Flavours, toppings, consistencies: list (active/inactive filter), add with starting price, edit, deactivate, reactivate |
| Prices | Full history per item; change now or from a future date and time; cancel the latest future price; no back-dating |
| Restaurants | List, add, edit (name, optional address), deactivate, reactivate |
| Settings | Minimum/maximum drinks, VAT, maximum discount, pickup lead time, display locale (editable within limits); currency read-only |
| Discount tiers | Thresholds and rates editable; activation refused by the server until Q-01 to Q-04 are answered |
| Concurrency | PostgreSQL `xmin` row version on every editable record; stale changes get 409 Conflict |
| API | 20 new manager-only endpoints (21 including the M4 probe), DTOs, RFC 9457 ProblemDetails (`application/problem+json`) |
| Frontend | Management area with section navigation, 7 screens, confirmation dialog, conflict handling, double-submit protection, locale-aware money |
| Database | One migration: `CaseInsensitiveNamesAndRowVersions` |
| Tests | 144 backend (xUnit), 46 frontend (Vitest), 4 end-to-end (Playwright) with automated accessibility scans – all passing |

---

## 2. Design decisions

| # | Decision | Rationale | Status |
| --- | --- | --- | --- |
| D1 | Optimistic concurrency with PostgreSQL's built-in `xmin` row version; every response carries `version`, every change sends it back; mismatch → 409 | No extra column; races in the database are caught as well as stale forms | **Confirmed by developer** |
| D2 | Names unique per list ignoring letter case (unique index on `lower(name)`) | "Vanilla" and "vanilla" must not both exist, even under simultaneous saves | **Confirmed by developer** |
| D3 | New prices start now or later (no back-dating); effective prices never edited; latest future price can be cancelled; new items need a starting price | Orders keep the price they were placed at; nothing orderable is ever unpriced | **Confirmed by developer** |
| D4 | Currency read-only in M10; display locale editable | Changing currency would require repricing everything; locale affects only formatting | **Confirmed by developer** |
| D5 | Tier thresholds and rates editable; activation refused server-side until Q-01 to Q-04 are answered; maximum discount labelled "meaning pending confirmation" | The URS gives thresholds but not rates, cap meaning or eligibility rules | **Confirmed by developer** |
| D6 | Setting limits: min drinks ≥ 1 and ≤ max; max drinks ≥ min and ≤ 50; VAT 0–100 %; max discount ≥ 0 (money); lead time 0–240 minutes | URS values 1, 10 and 15 %; the upper limits 50 and 240 are project recommendations | **Confirmed by developer** (including 50 and 240) |
| D7 | No pagination; active/inactive filter; sorted by display order (restaurants by name) | Lists hold at most a few dozen rows | **Confirmed by developer** |
| D8 | A cancelled future price is deleted (not flagged); the audit log keeps the full record | It never applied to any order; avoids a schema change and keeps "one open price" simple | Recommended, accepted |
| D9 | Discount activation gated by a code constant (`DiscountPolicy.RulesConfirmed = false`), not configuration | Cannot be switched on by accident before the M6 discount engine implements the confirmed rules | Recommended, accepted |
| D10 | Setting limits enforced only by the server; the UI shows the server's message on the field | One source of truth (`SettingRules`); the UI cannot drift from the API | Recommended, accepted |
| D11 | Prices and rates accept a comma or a dot as decimal separator; money formatted with `Intl` from the configured locale and currency | South African users type commas; no currency symbol is hard-coded (developer requirement) | Recommended, accepted |
| D12 | Price times entered in local time, sent and stored in UTC | PostgreSQL `timestamptz`/Npgsql accept UTC only; history shown in the manager's local time | Technical requirement |

---

## 3. What was built

### Backend
- **Domain:** `IHasRowVersion` (`Version` = `xmin`) on catalogue items, restaurants, settings and discount tiers;
  `CatalogItem.SchedulePrice` (no back-dating), `CancelLatestScheduledPrice`, `GetPriceHistory`; times normalised to UTC;
  `BusinessRuleException` with user-facing messages (`PricingMessages`).
- **Persistence:** `xmin` mapped as row version for every `IHasRowVersion` entity (no column created); case-insensitive
  unique name indexes created by SQL in the migration; the audit interceptor skips `xmin` (bookkeeping, not data).
- **Application:** `ManagementResult<T>` (Success / NotFound / Invalid / Conflict), shared validation and messages,
  `SettingRules` (D6 limits, hints, read-only currency, min/max partner), `DiscountPolicy` (D9).
- **Services (Infrastructure):** catalogue, pricing, restaurants, settings, discount tiers. Price changes and min/max drink
  changes also version-check the related row ("touch"), so a race between two managers is detected in the database.
- **API:** manager-only controllers with request DTOs (only allowed fields bind), 400 ValidationProblemDetails,
  404/409 ProblemDetails, CSRF on every POST/PUT (global filter from M4).

| Endpoint | Purpose |
| --- | --- |
| `GET /api/manager/catalog/{kind}?status=all\|active\|inactive` | List flavours, toppings or consistencies with current and next scheduled price |
| `GET /api/manager/catalog/{kind}/{id}` | One item |
| `POST /api/manager/catalog/{kind}` | Create with starting price |
| `PUT /api/manager/catalog/{kind}/{id}` | Rename / reorder (with `version`) |
| `POST /api/manager/catalog/{kind}/{id}/deactivate` · `/activate` | Availability for new orders |
| `GET /api/manager/catalog/{kind}/{id}/prices` | Item and full price history |
| `POST /api/manager/catalog/{kind}/{id}/prices` | Change now (`effectiveFrom` null) or schedule |
| `POST /api/manager/catalog/{kind}/{id}/prices/{priceId}/cancel` | Cancel the latest future price |
| `GET/POST /api/manager/restaurants`, `GET/PUT /api/manager/restaurants/{id}`, `POST …/{id}/deactivate` · `/activate` | Restaurants |
| `GET /api/manager/settings`, `PUT /api/manager/settings/{key}` | Settings (PUT returns all settings) |
| `GET /api/manager/discount-tiers`, `PUT /api/manager/discount-tiers/{tierNumber}`, `POST …/{tierNumber}/activate` | Tiers (activation currently always refused) |

### Frontend
- `ManagementLayout` with section navigation (Overview, Flavours, Toppings, Consistencies, Restaurants, Settings,
  Discount tiers); loads the display locale and currency once and updates them after a settings change.
- Screens: catalogue lists, price history and price changes, restaurants, settings, discount tiers.
- Shared: `putJson` in the HTTP client, `ConfirmDialog` (alertdialog, safe button focused first, focus kept inside,
  Escape closes, focus restored), `formatMoney` / `formatPercent` / `formatDateTime` via `Intl`.
- Forms: client-side checks first, then server field errors on the matching inputs; focus moves to the first invalid
  field (after re-enabling inputs when the error comes from the server); buttons disabled while saving plus a ref guard,
  so a double click sends one request; 409 shows "changed by someone else" with a Reload button.

---

## 4. Requirements, recommendations and assumptions

| Item | Source |
| --- | --- |
| Maintain flavours, toppings, consistencies and their prices | URS §3.4 |
| Price changes affect new orders only; history preserved | URS (milestone brief); enforced by immutable price rows. Order snapshots arrive in M7 |
| Restaurants as a maintained lookup | Recommendation (Q-05 asks whether management maintains them); no opening hours or capacity invented |
| Minimum 1 / maximum 10 drinks, VAT 15 % | URS values (seeded), editable |
| Upper limits 50 drinks and 240 minutes | Project recommendations (D6) |
| Maximum discount amount editable, meaning unconfirmed | URS §3.4.5 names it; meaning is Q-02 |
| Discount tier rates, activation | Not in the URS (Q-01 to Q-04) – activation blocked |
| Pickup lead time setting | Recommendation (Q-06) |
| Currency and display locale settings | Developer decision (no hard-coded prices or symbols) |
| Case-insensitive unique names, deactivate instead of delete, optimistic concurrency | Project recommendations (D1, D2) |

---

## 5. Concurrency, audit and security controls with evidence

| Control | Evidence (automated) |
| --- | --- |
| Every manager endpoint requires the Manager role | `EveryManagerEndpoint_RequiresTheManagerOnlyPolicy` (metadata test over all 21 routes); patron 403 tests; Playwright patron test |
| No deletion endpoints | `ManagerEndpoints_NeverDeleteOrPatch` |
| CSRF on every change | `Create_WithoutAntiForgeryToken_Returns400` |
| Stale changes refused (409) | `Update_WithStaleVersion_*` (catalogue, restaurants, settings, tiers), `Schedule_WithStaleVersion_*`, `DeactivateAndActivate_*` |
| Races caught in the database | `Database_RejectsUpdateBasedOnStaleRowVersion`, `SettingsPartnerCheck_RejectsTheSaveWhenTheOtherLimitChangedMeanwhile` |
| Double submits | `DoubleSubmittedCreate_SavesOnlyOneItem`, `DoubleSubmittedPriceChange_AppliesOnlyOnce`; UI test "sends only one request when Save is double-clicked" |
| Case-insensitive names | `Create_WithNameDifferingOnlyByCase_Returns400`, `Database_RejectsNamesDifferingOnlyByCase` |
| Field-level audit with the manager's identity, never `xmin` | `CatalogueChanges_AreAuditedWithTheManager_AndNeverRecordTheRowVersion` (all three lists), `RestaurantChanges_AreAuditedFieldByField`, settings/tier/price audit assertions |
| No back-dating; history preserved | `SchedulePrice_StartingInThePast_IsRejected`, `Schedule_InThePast_Returns400…`, `CancelScheduled_ReopensThePreviousPriceAndIsAudited` |
| Discount activation blocked | `Activate_IsRefused_AndTierStaysInactive`, `DiscountActivation_IsBlockedUntilRulesAreConfirmed`, Playwright check |
| Over-posting ignored | `Create_IgnoresFieldsTheClientMayNotSet` |
| Errors leak no internals | `ConflictResponse_IsProblemDetailsWithoutInternals` |
| Accessibility | axe (WCAG 2 A/AA, serious/critical) on Flavours, Prices, Settings, Discount tiers in Playwright; manual keyboard check |

**Accepted limitations:** rejected attempts (400/409) are not audited (the log records changes, not attempts); the
"touch" used for concurrency changes only `updated_at`, which is not audited; `xmin` wraps after ~4 billion transactions
(a false "no conflict" would need that many between loading and saving a form).

---

## 6. Test results (executed 19 November 2026)

| Suite | Result |
| --- | --- |
| Backend unit + integration (xUnit, Testcontainers PostgreSQL 17) | 144 passed, 0 failed |
| Frontend component (Vitest 5) | 46 passed, 0 failed |
| End-to-end (Playwright, Chromium, real API + Supabase) | 4 passed, 0 failed (manager journey runs when `E2E_MANAGER_EMAIL`/`E2E_MANAGER_PASSWORD` are set) |
| Accessibility (axe, WCAG 2 A/AA) | 0 serious or critical issues on the four scanned screens |
| Frontend build and lint | `tsc -b && vite build` passed; oxlint 0 warnings, 0 errors |
| `npm audit --omit=dev` | 0 vulnerabilities in shipped dependencies |
| `dotnet list … --vulnerable --include-transitive` | No vulnerable packages in any project |
| Secret scan of tracked files | Only password-handling code and known throwaway test passwords |
| Built bundle (case-sensitive search for connection-string keys, `supabase.co`, `pooler`) | Only the validation message `confirmPassword=…`; no server details |

Growth from Milestone 4: backend 50 → 144 tests, frontend 13 → 46, end-to-end 2 → 4.

---

## 7. Database changes

| Migration | Change |
| --- | --- |
| `CaseInsensitiveNamesAndRowVersions` | Replaces the case-sensitive unique name indexes on `flavours`, `toppings`, `consistencies` and `restaurants` with unique indexes on `lower(name)` (`ux_<table>_lower_name`). Maps `xmin` as row version (no SQL generated – Npgsql skips system columns) |

No table, column or row changed. Checked for existing case duplicates and reviewed with `dotnet ef migrations script`
before applying; applied to Supabase on 18 November 2026. Steps 4–9 needed no further schema changes
(`has-pending-model-changes` confirmed).

---

## 8. Milestone 10 – Definition of Done

| Criterion | Status |
| --- | --- |
| Managers can maintain flavours, toppings and consistencies (URS values) | ✅ |
| Managers can maintain restaurants | ✅ |
| Effective-dated prices with history | ✅ |
| System settings maintainable within agreed limits | ✅ |
| Discount tiers maintainable without being activated or inventing rules | ✅ |
| Concurrency protection | ✅ (409 on stale versions; database-level race detection) |
| Full audit of every change | ✅ |
| Historical data preserved (deactivate, never delete; effective prices immutable) | ✅ |
| Changes apply to new orders only | ✅ at the data level; ⏳ orders must snapshot prices and VAT when created (M7) |
| Manager-only API with DTOs and ProblemDetails | ✅ |
| Accessible React management UI | ✅ (axe scans, keyboard check, labelled fields, announced errors) |
| Confirmation before deactivation and price cancellation | ✅ |
| Conflict handling and duplicate-submit protection | ✅ |
| Tests: xUnit + Testcontainers, Vitest, Playwright | ✅ |
| All existing tests continue to pass | ✅ |
| Documentation updated | ✅ (README, this report) |
| No secrets committed | ✅ (section 10) |
| Discount tiers can be activated | ⏳ intentionally blocked until Q-01 to Q-04 are answered and M6 implements them |

---

## 9. Issues encountered and resolutions

| Issue | Cause | Resolution / lesson |
| --- | --- | --- |
| Errors returned as `application/json` instead of `application/problem+json` | `[Produces("application/json")]` on every controller overrode error content types | Found by a Step 6 test; attribute removed from all 8 controllers |
| Server field errors did not move focus | Inputs are disabled while saving; a disabled input cannot take focus | Focus moved after saving ends (`focusAfterSaveRef` pattern, used in every form) |
| Prices link nested inside the Edit button | Paste slip | Found by the axe scan (`nested-interactive`), not by component tests – keep browser-level scans |
| Service classes created in the Application project; JSX files saved as `.ts`; content replaced instead of appended; stray duplicate files | Manual copy-and-paste of large changes | Rules adopted: `*Service.cs` (non-interface) → Infrastructure; JSX → `.tsx`; Ctrl+End before "paste at the end"; complete replacement files for multi-edit changes; file checks before building |
| `xUnit2031` analyzer error | `Where(...)` before `Assert.Single` | Use the filtering overload |
| Test timeouts and 15-minute test runs | Heavy machine load (normal runs: ~2 minutes backend, ~10 s Vitest) | Re-run single tests with `--filter`; not a code issue |
| Partial text matches in tests (`/in the future/`, `Read-only`) | Hints contain the same words as messages | Match exact messages |
| Old UI served after renames | Vite dev server stuck after compile errors | Hard refresh or restart `npm run dev` |

---

## 10. Open questions and recommendations

**Questions for stakeholders:**
- **Q-01 to Q-04** (discount rate per tier, meaning of the maximum discount, which orders count, before/after VAT) – block
  tier activation (M6).
- **Q-02** specifically: the "maximum discount amount" setting is editable but its meaning is unconfirmed.
- **Q-05:** restaurant details beyond name and address (opening hours, capacity).
- **Q-06:** pickup-time rules; the lead-time setting (0–240 minutes) is a placeholder until answered.
- **Q-16 (new):** must higher discount tiers have higher thresholds and rates than lower ones? Not enforced.

**For later milestones:**
- **M7:** each order must copy the prices and VAT rate it used, completing "changes apply to new orders only".
- **M6:** implement the confirmed discount rules, set `DiscountPolicy.RulesConfirmed` to true, wire the Activate button.
- **M11/M12:** audit-log and price-history reports for managers.
- Changing the currency would need a dedicated repricing process; not required by the URS.

**Test data:** each Playwright manager run adds one deactivated `E2E Flavour <timestamp>` to the development database
(nothing is ever deleted). Plan a dedicated test database before UAT.

---

## 11. Sign-off checks before the final commit

```powershell
git log --all -p | Select-String -Pattern 'Password=|pooler\.supabase\.com|service_role|sb_secret|PRIVATE KEY|MILKSHAKE_MANAGER_PASSWORD=|E2E_MANAGER_PASSWORD='
git ls-files | Select-String -Pattern '\.env|secrets\.json|appsettings\.(Production|Staging)'
cd frontend; npm run build
Select-String -CaseSensitive -Path dist/assets/*.js -Pattern 'Host=|Database=|Username=|supabase\.co|pooler'
```

Expected: no output from the first and fourth commands; only `frontend/.env.example` from the second.

---

## 12. Commit history (Milestone 10)

| Commit | Description |
| --- | --- |
| `2872ca7` | Manager API for catalogue items and restaurants; `xmin` concurrency; case-insensitive names (migration) |
| `68d15b6` | Effective-dated pricing: history, scheduling, cancellation, no back-dating |
| `d76b532` | Settings and discount tier management; D6 limits; activation blocked |
| `f373c06` | Security, audit, concurrency and over-posting tests; errors as `application/problem+json` |
| `c0b6cd3` | Management area, catalogue screens, confirmation dialog, locale-aware prices |
| `d0c621c` | Price history and scheduling screen; restaurant screens |
| `692896d` | Settings and discount tier screens |
| `9ec2c7f` | Playwright manager journey with axe scans; patron exclusion; nested-link fix |
| *(pending)* | Documentation: README and this report |
