# M14 decisions register (User Acceptance Testing, Deployment and Handover)

Earlier registers: `2026-11-20_pre-m8-decisions.md`, `2026-11-20_m9-email-decisions.md`,
`2026-11-20_m11-audit-decisions.md`, `2026-11-21_m12-reporting-decisions.md`.

**Who decided:** "Developer" means the developer, on the assistant's recommendation, within the authority the
internship brief gives. Items marked **Stakeholder** need the people who set the assignment to approve them; they are
listed on the UAT sign-off form. Nothing here records a stakeholder decision that has not been made.

## Decisions taken in M14

| ID | Decision | Date | Decided by | Stakeholder action |
| --- | --- | --- | --- | --- |
| D-1 | Paystack stays in **test mode**; AC-14.3's "live gateway credentials" and "low-value live transaction" are replaced by test-mode credentials and the test card | 21 Nov | Developer | **Accept the AC-14.3 deviation** |
| D-2 | One Docker container serving site and API on one origin; free hosting (Koyeb, fallback Render) | 21 Nov | Developer | – |
| D-3 | Data Protection key ring in PostgreSQL (`milkshake.data_protection_keys`), encrypted with a certificate from host settings, not audited | 21 Nov | Developer | – |
| D-4 | Fresh Supabase Free project in West EU (Ireland) with fresh demonstration data | 21 Nov | Developer | – |
| D-5 | Email through Resend with an allow-list, no sender domain (amended by E-1) | 21 Nov | Developer | – |
| D-6 | Host's free subdomain with its HTTPS; HSTS one year without `includeSubDomains` or `preload` | 21 Nov | Developer | – |
| D-7 | UAT testers: the people who set the assignment, referred to by aliases ToppanTester1–3 (aliases chosen by the developer, 22 Nov) | 21–22 Nov | Developer | Testers confirm their roles at the session |
| D-8 | Budget: completely free | 21 Nov | Developer | – |
| D-9 | No second-developer code review (Definition of Done #2): documented limitation | 21 Nov | Developer | **Accept the limitation** |
| D-10 | Database in West EU (Ireland); application host in Frankfurt (nearest free region) | 21 Nov | Developer | – |
| D-11 | Background workers run only while the app is awake; they run at start-up; no keep-alive pinging | 21 Nov | Developer | Accept as known limitation |
| D-12 | Backups by scheduled GitHub Actions `pg_dump`, encrypted, with a restore test into a disposable database | 21 Nov | Developer | – |
| D-13 | Monitoring by a scheduled GitHub Actions health check (every 6 hours) with email alerts; periodic, not continuous | 21 Nov | Developer | Accept as known limitation |
| D-14 | CI on every push; deployment only by hand, from a release tag on `main` whose CI run passed | 21 Nov | Developer | – |
| D-15 | DEF-04: reserve the price table's space while the estimate loads | 21 Nov | Developer | – |
| E-1 | Amends D-5: Resend's test sender delivers only to the Resend account's own address (no domain); other patrons' emails are recorded as Suppressed; UAT email scripts run on the facilitator's account | 21 Nov | Developer | **Accept for the demonstration** (or supply a domain) |
| E-3 | Uses the D-2 fallback: Koyeb closed its free plan to new accounts after joining Mistral AI, so the site runs on **Render free** (Frankfurt; sleeps after 15 minutes, about a minute to wake); client address from `CF-Connecting-IP` | 21 Nov | Developer | Accept the free-hosting limitations |

(E-2 was reserved for "reuse the development database" and was not taken.)

## Stakeholder decisions still open (carried from earlier milestones)

On 22 Nov 2026 the project owner decided each of these for the demonstration (see
`2026-11-22_owner-decisions.md`). They remain open until the stakeholder confirms them.

| Ref | Decision needed | Owner's decision (22 Nov 2026), pending stakeholder confirmation |
| --- | --- | --- |
| Q-01 – Q-04 / B2 | Frequent-customer discount rates | 5 / 10 / 15 %; cap R 50,00 per order; paid, not-cancelled orders count; before VAT. Tiers can be activated by a manager (all inactive until then) |
| Q-07 / AC-6.6 | VAT exclusive of fees, 15 %, rounding; sign `m6-calculation-sign-off.csv` | As built |
| B1.1 – B1.4, I4, I8 | Patron cancellation with 10 % fee; restaurant-requested cancellation with full refund; wrong-amount payments refunded by hand; managers see patron contact | As built |
| Q-08 / D1 | One combined confirmation and receipt email | As built |
| Q-14 / B6 | Personal-data retention and anonymisation (POPIA) | Policy written (`docs/policies/data-retention-policy.md`): 5 years after the last order, then personal details removed; applied by hand, automation later |
| B5 / R7 | "Collected" status after pickup | Not now (future work) |
| M4 follow-up | Email confirmation and password reset | Future work |
| Q-15 | Performance and availability targets | Target: 2 s at 50 concurrent users (met locally in M13); live site checked only lightly |
| M12 R1–R13 | Reporting choices (two-tone trends, columns, labels, one audit table at a time, CSV with 10,000-row cap, further charts) | As built |
| – | Outstanding milestone sign-offs: M1, M2, M6 (AC-6.6), M10, M12 | Not yet signed |
