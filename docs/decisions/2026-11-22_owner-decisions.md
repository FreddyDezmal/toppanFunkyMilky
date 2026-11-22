# Project owner's decisions on the open questions — 22 November 2026

**Status:** decided by the project owner for the demonstration. **Not yet confirmed by the stakeholder.**
Each decision below replaces an "open" entry in the M14 decisions register. If the stakeholder later decides
differently, the change is made then; nothing here is presented as a stakeholder decision.

## Frequent-customer discounts (Q-01 to Q-04, B2)

| Ref | Question | Decision |
| --- | --- | --- |
| Q-01 | Rate per tier | Tier 1 **5 %**, tier 2 **10 %**, tier 3 **15 %** (the seeded values). Thresholds stay as seeded: 5 orders of 2+ drinks, 10 orders of 3+, 20 orders of 4+. |
| Q-02 | Meaning of "maximum discount amount" | A **rand cap per order**: no order is discounted by more than this amount (currently R 50,00). |
| Q-03 | Which previous orders count | Only **paid orders that were not later cancelled or refunded** (status *Confirmed*). Pending, failed and cancelled orders do not count. |
| Q-04 | Before or after VAT | The discount comes off the **whole order before VAT**; VAT is then charged on the reduced amount. |

**What changed in the system**

- The code gate `DiscountPolicy.RulesConfirmed` is now `true`, so managers can activate tiers.
- A new order-history source reads each patron's *Confirmed* orders and their drink counts.
- Managers can now also **deactivate** a tier (`POST /api/manager/discount-tiers/{n}/deactivate`); orders already
  placed keep their discount (the price is a snapshot).
- **All tiers remain inactive** until a manager activates them under Management → Discount tiers.
- When several active tiers are reached, the highest applies; tiers never combine (as built in M6).

## Other decisions

| Ref | Question | Decision |
| --- | --- | --- |
| Q-07 / AC-6.6 | VAT | Kept as built: fees exclude VAT, 15 % added, one rounding per order. `m6-calculation-sign-off.csv` still needs the stakeholder's signature. |
| B1.1 – B1.4, I4, I8 | Cancellation and refunds | Kept as built: patron cancels before pickup with a 10 % fee; restaurant-requested cancellation refunds in full; wrong-amount payments refunded by hand; managers see patron contact details. |
| Q-08 / D1 | Confirmation email | One combined confirmation and receipt email (as built). |
| Q-14 / B6 | Personal-data retention | Policy written now, automation later: see `docs/policies/data-retention-policy.md`. |
| B5 / R7 | "Collected" status | Not now (future work). |
| M4 follow-up | Email confirmation and forgot password | Future work. |
| Q-15 | Performance target | Pages and API answers within **2 seconds with 50 concurrent users** (met locally in M13). |
| M12 R1–R13 | Reporting choices | Kept as built. |

## Still needed

- Stakeholder confirmation of every decision above.
- Outstanding milestone sign-offs (M1, M2, M6 AC-6.6, M10, M12) — names, signatures and dates are to be supplied by
  the signatories, never filled in by the project.
