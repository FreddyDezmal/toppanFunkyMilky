# Progress Report – Milestone 6: Pricing, VAT and Discounts

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 19 November 2026
**Period covered:** 19 November 2026
**Status:** Milestone 6 complete for drink prices, VAT and totals. The frequent-customer discount is built, tested
and **deliberately switched off** until its rules are confirmed (AC-6.3, AC-6.4 deferred; section 10). AC-6.6 awaits
stakeholder sign-off of the calculation sheet.
**Requirements traced:** URS §3.4 (price per drink, discount, total with VAT), §3.4.5 (discount settings), §4 (configurable
fees); roadmap AC-6.1 to AC-6.6, MR-02, FR-14 to FR-16, FR-19, FR-20, A-02, A-03

---

## 1. Summary

- One **server-side pricing engine** calculates every amount. The browser sends identifiers only and never submits a price,
  VAT rate, discount or total.
- A drink costs flavour + consistency + topping fees (A-02). Fees are VAT-exclusive; VAT is added to the discounted
  subtotal (A-03), at the rate in the `Tax.VatRate` setting.
- Prices are **effective-dated**: the engine reads the fees in force at one pricing instant per request, so a scheduled
  price change takes effect exactly at its start time. Historical price rows are never modified.
- Patrons see live, debounced price estimates while choosing, and the same server totals on the review screen.
- No database schema change, no new tables, no order or payment tables (those belong to M7).

## 2. Decision register

| ID | Decision | Status | Basis |
| --- | --- | --- | --- |
| P1 | Fees are VAT-exclusive; VAT is added | Approved developer decision | Roadmap A-03; **Q-07 still to be ratified** |
| P2 | Drink price = flavour + consistency + topping fee; no base price; a zero fee is allowed | Approved | Roadmap A-02, AC-6.1 |
| P3 | VAT and discount are each rounded **once, at order level**, to 2 decimals, half away from zero; total = taxable + VAT | Approved | AC-6.2 ("documented rule"); Q-07 |
| P4 | Discount is applied **before VAT**, to the whole order | Approved, provisional | A-03 wording; **Q-04 open** |
| P5 | Discount maths built and tested with fictional rates; live discount stays 0.00 behind the gate `DiscountPolicy.RulesConfirmed = false`; eligibility source `NoOrderHistoryYet` | Approved | Q-01 to Q-03 open; no order history exists before M7 |
| Rule | Provisional tier rule: an order *qualifies* if it has ≥ `MinDrinksPerOrder` drinks; the highest active tier whose `MinPreviousOrders` is met wins; tiers never combine | Provisional | **Q-03 open** |
| Rule | Maximum discount is a currency cap per order | Provisional | **Q-02 open** |
| Rule | Seeded tier rates 5 / 10 / 15 % are placeholders and cannot be activated | Placeholder | **Q-01 open** |

## 3. Architecture

```
React (OrderDrinksPage, OrderReviewPage)  – formats only
   │  POST /api/ordering/quote            { drinks: [ids] }            live, debounced 400 ms, latest wins
   │  POST /api/ordering/draft/validate   { restaurant, pickup, ids }  review
   ▼
OrderingController (PatronOnly, CSRF, Ordering rate limit, 16 KB, unknown fields rejected, no-store)
   ▼
IOrderPricingService (Application)  – one options load + one pricing instant (Pickup.AsOfUtc); reads only
   ├─ IOrderingCatalogService       – fees in force at that instant
   ├─ OrderingSettings.VatRate      – validated setting
   └─ IDiscountResolver             – gate first; while shut, reads nothing and returns no discount
         ├─ IDiscountConfigurationSource (tiers + max discount, validated)
         └─ IDiscountEligibilitySource  → NoOrderHistoryYet (M7 replaces this)
   ▼
PriceCalculator (Domain, pure)  – PriceRounding.ToCents, DiscountTierSelector
```

- **Calculation order:** subtotal = Σ drink prices → discount = min(round(subtotal × rate), cap, subtotal) → taxable =
  subtotal − discount → VAT = round(taxable × VAT rate) → total = taxable + VAT. `decimal` throughout.
- **Configuration errors** (missing or invalid VAT, tiers or cap) raise `PricingConfigurationException`; the API logs the
  reason and returns 503 "Ordering is temporarily unavailable". No default rate is ever assumed.
- **Responses** carry `isEstimate: true`, `pricedAtUtc` and a note that the final amount is calculated when the order is
  placed. Currency comes from the response; the UI formats with `Intl` and never hard-codes a symbol.
- **React live quote** (`useLiveQuote`): 400 ms debounce; each request numbered and the previous one aborted, so a late
  response cannot overwrite a newer price; 429 → "Prices will update in a moment" and one retry after 5 s; 400 → reload
  options so stale choices are cleared (E6); other errors show a message without blocking the order.

## 4. What was built

### Backend
- `Domain/Pricing`: `PriceRounding`, `PricingModels`, `PriceCalculator`, `DiscountTierSelector`, `PricingConfigurationException`.
- `Application/Ordering`: `OrderPricing` (`IOrderPricingService`: `PriceDraftAsync`, `QuoteDrinksAsync`), `Discounts`
  (gate, resolver, eligibility source), `OrderingSettings.VatRate`.
- `Infrastructure/Ordering/DiscountConfigurationSource`.
- API: `POST /api/ordering/quote`; `draft/validate` now returns per-drink prices and totals; strict request contracts.

### Frontend
- `useLiveQuote`, `PriceSummary`, per-drink price on `DrinkCard`, "Estimated price" panels on the milkshakes and review
  screens, `ESTIMATE_NOTE`, `quoteDrinks` API (identifiers only).

## 5. Requirements traceability

| AC | Status | Evidence |
| --- | --- | --- |
| AC-6.1 Drink price = fees, all 168 combinations | **Met** | `SeededCombinationTests` (168 + count check, independent integer-cent oracle) |
| AC-6.2 VAT at configured rate, documented rounding | **Met** (rule P3; Q-07 to ratify) | `PriceRoundingTests`, `PriceCalculatorTests`, `OrderPricingIntegrationTests` (VAT setting change) |
| AC-6.3 Highest qualifying tier; 0 when none | **Partly met / deferred**: "0 (none)" shown; tier logic built and unit-tested but switched off | `DiscountTierSelectorTests`, `DiscountRulesTests`, `DiscountIntegrationTests` |
| AC-6.4 Discount never exceeds the maximum | **Built, deferred** (live discount is always 0) | `PriceCalculatorTests` cap cases |
| AC-6.5 Server total = displayed total; server authoritative | **Met** | `OrderQuoteTests` forged-field theory; e2e checks the review total equals the live estimate; UI only formats |
| AC-6.6 ≥ 50 cases signed off against a stakeholder-checked sheet | **Tests met; sign-off pending** | 200+ calculation cases; sheet `docs/pricing/m6-calculation-sign-off.csv` (168 rows) awaits the stakeholder |

## 6. Security controls with evidence

| Control | Evidence |
| --- | --- |
| Browser never supplies amounts | Requests carry identifiers only; `[JsonUnmappedMemberHandling(Disallow)]` on all ordering requests; integration theory (price, vatRate, discountRate, previousOrders, total) → 400; e2e forged quote → 400 |
| No trusted pricing in client state | Prices live only in the latest quote response; the stored draft holds IDs only (E2) |
| Authentication, roles, CSRF | Class-level `PatronOnly`; global CSRF filter; tests for 401, 403 (manager) and missing token |
| Abuse limits | `Ordering` rate limit (60/min per patron) after authentication; 16 KB body limit (413 test); client debounce |
| Discount gate | `RulesConfirmed = false` checked in the resolver and in tier activation; tests prove nothing is read while shut |
| No writes, history preserved | Integration tests: pricing writes nothing; price history unchanged |
| Errors | Misconfiguration → logged reason + 503 ProblemDetails; no stack traces or values exposed |
| Secrets | Static scan of tracked files: no connection strings, keys or credentials; `.env`/`.env.local` ignored; built bundle: no server details, no source maps, no tokens in storage |
| Dependencies | `npm audit --omit=dev`, `dotnet list package --vulnerable` run in Step 9 |

## 7. Test results (executed 19 November 2026)

Run by the developer on Windows 11; all reported passing with no issues.

| Suite | Expected | Result |
| --- | --- | --- |
| Backend unit + integration (Testcontainers PostgreSQL 17) | 470 | Passed |
| Vitest | 100 | Passed |
| Playwright without manager credentials | 6 passed, 2 skipped | Passed |
| Playwright with manager credentials | 8 passed | Passed |
| Build `-warnaserror`, `tsc -b`, oxlint | 0 warnings / errors | Passed |
| `npm audit --omit=dev`, `dotnet list package --vulnerable` | No vulnerabilities | Passed |

New in M6: rounding 9, calculator 20, seeded combinations 169, settings reader 12 (replaced), pricing service 6, tier
selector 6, discount rules 7, quote service 6; integration pricing 3, discounts 2, quote API 16; Vitest +5; e2e journey
extended (live prices, total change, review total, forged quote).

## 8. Database changes

None. VAT, discount tiers and the maximum discount already existed as settings and tables (M10). No migration.

## 9. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| `OrderPricingService` internal, not unit-testable | Made public |
| `Intl` writes a non-breaking space in amounts | Test helper `zar()` normalises spaces |
| Review heading's accessible name ran prices together | Added a space between the name and price spans |
| Straight apostrophes inside single-quoted test names | Use ’ in test names |

## 10. Open questions and deferred items

| Item | Needed from | Blocks |
| --- | --- | --- |
| Q-01 tier discount rates | Stakeholder | Activating discounts |
| Q-02 meaning of "maximum discount" | Stakeholder | Cap rule confirmation |
| Q-03 which past orders count | Stakeholder | Eligibility rule; needs M7 order history |
| Q-04 discount before/after VAT, whole order vs per drink | Stakeholder | Confirms P4 |
| Q-07 VAT-inclusive/exclusive and rounding | Stakeholder | Ratifies P1 and P3 |
| AC-6.6 sign-off of `m6-calculation-sign-off.csv` | Stakeholder | Closing AC-6.6 |
| AC-6.3 / AC-6.4 live behaviour | Q-01 to Q-03 + M7 | Turning the gate on |

**To activate discounts later:** answers to Q-01 to Q-04 recorded; replace `NoOrderHistoryYet` with an eligibility source
reading real completed orders (M7); set real tier rates; set `RulesConfirmed = true`; add integration tests for a qualifying
patron. Never use browser-supplied purchase counts.

## 11. Integration notes for Milestone 7

- At order placement, call `IOrderPricingService.PriceDraftAsync` **on the server** and store the returned
  `PriceBreakdown` (per-drink prices, subtotal, discount with tier and rate, taxable, VAT rate, VAT, total, `PricedAtUtc`)
  as an immutable snapshot on the order. Never accept an amount from the browser.
- The payable amount is the stored snapshot's total; if it differs from the patron's last estimate, show the new amount
  before payment.
- Implement `IDiscountEligibilitySource` over completed orders once Q-03 is answered.

## 12. Commit history (Milestone 6)

| Commit | Description |
| --- | --- |
| `4ab8949` | Pure server-side price calculator; 168-combination oracle tests |
| `5b2445e` | VAT from settings; pricing service (one instant, no writes); effective-date integration tests |
| `49ac2bf` | Gated discount engine; provisional tier selection; no-history eligibility source |
| `f0c4151` | Pricing API: live quote endpoint; totals on draft validation; forged fields rejected |
| `b02cf6b` | React live prices: per-drink prices, VAT, estimated total; debounced, latest-response-wins |
| (this commit) | M6 documentation, decision register and calculation sign-off sheet |
