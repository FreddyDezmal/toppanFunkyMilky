# M14 UAT sign-off form

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks) · **Milestone:** M14 · **Release:** `<version / commit, filled in at UAT>`
**Site tested:** `https://<filled in at UAT>`

Completed and signed by the stakeholders only. The developer prepares the results summary but does not sign for,
or on behalf of, anyone.

## 1. Results

| | Count |
| --- | --- |
| Scripts in scope (P, M, C) | 38 |
| Executed by a stakeholder tester | |
| Passed | |
| Failed (each with a defect) | |
| Additional scripts (X-01 – X-03) passed / failed | |

## 2. Defects (from `m14-uat-defect-log.md`)

| Severity | Open at sign-off |
| --- | --- |
| Critical | |
| High | |
| Medium (each with an agreed plan) | |
| Low (each with an agreed plan) | |

AC-14.2 requires **0 open Critical and 0 open High** defects.

## 3. Decisions the stakeholder is asked to record

| # | Decision | Built as | Stakeholder decision (Approve / Change / Defer + note) |
| --- | --- | --- | --- |
| D-1 | AC-14.3 amended: Paystack test mode and allow-listed email instead of live credentials and a live transaction | Test mode, no real money | |
| Q-01–Q-04 | Frequent-customer discount rates | Engine built, switched off (R 0,00 for every patron) | |
| Q-07 / AC-6.6 | VAT exclusive of fees, 15 %, rounding; calculation sheet `m6-calculation-sign-off.csv` | As built | |
| B1.1–B1.4 | Patron cancellation with 10 % fee; restaurant-requested cancellation with full refund | As built (X-01, X-02) | |
| Q-08 | One combined order confirmation and receipt email | As built | |
| Q-14 / B6 | Retention of personal data (POPIA); demo holds test data only | Kept, masked in audit views | |
| B5 / R7 | No "Collected" status after pickup | Not built (out of URS scope) | |
| M4 follow-up | Email confirmation and password reset not implemented | Not built | |
| D-9 / DoD #2 | Code not reviewed by a second developer | Documented limitation | |
| — | Earlier milestone sign-offs still outstanding: M1, M2, M6 (AC-6.6), M10, M12 | Pending | |

## 4. User guides reviewed (AC-14.5)

| Guide | Reviewed by | Comments |
| --- | --- | --- |
| Patron guide | | |
| Manager guide | | |
| Technical and operations guide | | |

## 5. Decision

- [ ] **Accepted:** the release meets the requirements tested; open Medium/Low defects follow their agreed plans.
- [ ] **Accepted with conditions:** (list)
- [ ] **Not accepted:** (reasons)

## 6. Signatures

| Role | Name | Signature | Date |
| --- | --- | --- | --- |
| Business owner / sign-off authority | | | |
| Patron tester | | | |
| Manager tester | | | |
| Developer (prepared the results; not an approval) | | | |
