# M14 User Acceptance Testing plan

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks) · **Milestone:** M14 · **Version:** draft 1, 21 November 2026

> **Draft status.** The scripts were drafted from the Development Roadmap §11 requirements catalogue and the features
> as built, because the User Requirement Specification v1.1 was not available. Each script names its requirement ID;
> the wording should be checked against the URS when it is supplied. Tester names, dates and signatures are left
> blank on purpose: only the people who take part fill them in.

## 1. Purpose and acceptance criteria

UAT lets the stakeholders confirm that the system does what the business asked for, on the deployed demonstration
site, before they sign it off.

| ID | Acceptance criterion (roadmap) | How this plan meets it |
| --- | --- | --- |
| AC-14.1 | UAT scripts cover every requirement in Section 11 and are executed by stakeholder testers. | `m14-uat-scripts.md`: one script per requirement for all 38 in-scope requirements (MR-01–MR-10, FR-01–FR-28), run by the stakeholder testers in §3. B-01/B-02 (mobile app) belong to M15. |
| AC-14.2 | UAT sign-off with 0 open Critical/High defects; Medium/Low defects have an agreed plan. | `m14-uat-defect-log.md` and `m14-uat-sign-off.md`. |

Developer test results (M13 and M14 Step 4) are **not** a substitute for this plan; they only show the system is ready
to be tested.

## 2. Scope

**In scope:** the patron website (account, ordering, pickup, payment, emails, order history) and the management area
(prices, lookups, settings, discount tiers, restaurants, orders, emails, reports, audit).

**Also offered for acceptance (built, not in the §11 catalogue):** patron cancellation with a fee and refund, the
restaurant asking a patron to cancel, and keyboard/phone use (scripts X-01 to X-03). These were built under
provisional decisions B1.1–B1.4; the stakeholder may accept, change or reject them.

**Out of scope:** the mobile app (M15); real-money payments (decision D-1: Paystack test mode only); a real sender
domain for email (decision D-5).

## 3. People

| Role | Name | Responsibility |
| --- | --- | --- |
| Business owner / sign-off authority | | Accepts or rejects the release; signs `m14-uat-sign-off.md` |
| Patron tester(s) | | Run the patron scripts (P-xx) and X-01, X-03 |
| Manager tester(s) | | Run the manager scripts (M-xx) and X-02 |
| UAT facilitator (developer) | | Prepares the environment and accounts, answers questions, logs defects as reported, never records a result on a tester's behalf |

## 4. Environment and test data

| Item | Value |
| --- | --- |
| Site | The deployed demonstration site, `https://<to be confirmed in Step 8>` |
| Data | Fresh demonstration data (decision D-4): 7 flavours, 4 consistencies, 6 toppings, 2 sample restaurants, VAT 15 %, frequent-customer discounts switched off |
| Patron accounts | Each patron tester registers their own account during script P-01 |
| Manager accounts | One per manager tester, created by the facilitator before the session; the password is given to the tester in person and changed by nobody else |
| Payments | **Paystack test mode.** No real card and no real money. Use the test card on Paystack's "Test Payments" documentation page; the facilitator hands it out in the session |
| Email | **Decision E-1:** the demonstration has no own email domain, so the email service delivers only to one address: the facilitator's. Emails to any other patron are recorded and shown as *Suppressed* under Management → Emails. The email scripts (P-18 to P-20) are therefore run with the facilitator's patron account, with the testers watching and reading the received email on the facilitator's screen; every other patron script uses the testers' own accounts |
| Browsers | Any current Chrome, Edge, Firefox or Safari; at least one tester also uses a phone |

**Demonstration prices** (as seeded; if a manager tester has changed them, use the current values in
Management → Flavours / Consistencies / Toppings):

| Flavour | Price | Consistency | Price | Topping | Price |
| --- | --- | --- | --- | --- | --- |
| Strawberry | R 32,00 | Double thick | R 10,00 | Frozen Strawberries | R 12,00 |
| Vanilla | R 30,00 | Thick | R 6,00 | Freeze-dried banana | R 12,00 |
| Chocolate | R 32,00 | Milky | R 0,00 | Oreo crumbs | R 10,00 |
| Coffee | R 34,00 | Icy | R 4,00 | Bar one syrup | R 10,00 |
| Banana | R 32,00 | | | Coffee powder with chocolate | R 9,00 |
| Oreo | R 36,00 | | | Chocolate vermicelli | R 8,00 |
| Bar one | R 38,00 | | | | |

Prices exclude VAT; VAT (15 %) is added to the order total.

## 5. How a session runs

1. **Before:** facilitator confirms the site is healthy, creates manager accounts, adds tester emails to the approved
   list, prints or shares the scripts.
2. **During:** testers follow each script, write the actual result, mark **Pass** or **Fail**, and note evidence (a
   screenshot file name, an order reference or an email subject). Anything unexpected is a defect, even if the script
   passes.
3. **Defects:** the tester describes it; the facilitator records it in `m14-uat-defect-log.md` with the tester's
   wording; the tester (not the facilitator) sets or agrees the severity.
4. **Fixes:** Critical and High defects are fixed (T14.2), redeployed and **retested by a tester**. Medium and Low get
   an agreed plan (fix now, fix later with a date, or accept).
5. **Sign-off:** when the exit criteria are met, the business owner completes `m14-uat-sign-off.md`.

## 6. Severity scale (same as M13)

| Severity | Meaning | Example |
| --- | --- | --- |
| Critical | Wrong money, lost data, security breach, or a core journey impossible | A paid order shows as unpaid; a patron sees another patron's order |
| High | A requirement not met, with no reasonable workaround | Order total excludes VAT; managers cannot change a price |
| Medium | A requirement met with a workaround, or a significant usability problem | A filter needs to be applied twice to work |
| Low | Cosmetic or minor wording issue | A spelling mistake; spacing on one screen |

## 7. Entry and exit criteria

**Entry:** site deployed and smoke-tested (Step 9); developer test suites passing on the deployed commit; manager
accounts and the email list ready; testers have this pack.

**Exit:** every script in `m14-uat-scripts.md` executed by a stakeholder tester with a recorded result; 0 open
Critical/High defects; every open Medium/Low defect has an agreed plan; the outstanding decisions listed in the
sign-off form are recorded as approved, changed or deferred by the stakeholder.

## 8. Schedule

| Session | Date | Testers | Scripts |
| --- | --- | --- | --- |
| 1 – Patron journeys | | | P-01 to P-22, X-01, X-03 |
| 2 – Management | | | M-01 to M-13, C-01 to C-03, X-02 |
| 3 – Retest and sign-off | | | Failed scripts and fixed defects |

## 9. Documents in this pack

| File | Contents |
| --- | --- |
| `m14-uat-scripts.md` | The scripts, each with a result table |
| `m14-uat-defect-log.md` | Defect log and how to fill it in |
| `m14-uat-sign-off.md` | Sign-off form |
| `../guides/patron-guide.md`, `../guides/manager-guide.md` | User guides, also reviewed during UAT (AC-14.5) |
