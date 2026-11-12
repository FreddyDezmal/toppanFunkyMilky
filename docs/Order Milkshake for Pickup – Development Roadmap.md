# Order Milkshake for Pickup – Development Roadmap

Nov 12, 2026 · @Red

## 1. Executive Summary

The core web application can be delivered in **15 milestones over about 66 working days (≈13 weeks), or ≈76 days with a 15% contingency**, by a team of two full-stack developers, one QA/tester and a part-time technical lead. The bonus mobile app (Milestone 15) is a separate phase of about 30 further working days.

The roadmap is derived from the *Order Milkshake for Pickup – User Requirement Specification v1.1* (Toppan Security, 20 Nov 2025). It covers every one of the 10 mandatory requirements in URS §4 and all functional rules in URS §3. Each requirement is traced to at least one milestone and acceptance criterion in Section 11.

Three design decisions shape the whole plan:

1. **Price snapshotting and versioned configuration.** URS §3.4.6 requires that changes to lookups, fees, VAT and limits never alter concluded, completed or open transactions. Every order line therefore stores the prices, VAT rate and discount that applied when it was created, and lookup values are versioned rather than overwritten.
2. **Audit as a foundation, not a feature.** URS §3.3 and §4 require full auditing of every field change and transaction. The audit mechanism is built in Milestone 3 and extended in Milestone 11, so no table is created without it.
3. **Payment gates confirmation.** URS §2.2 and §3.1 state an order is confirmed only after successful payment. The payment gateway (Milestone 8) is the longest-lead, highest-risk item and sits on the critical path; its sandbox account should be requested in week 1.

**Labels used in this document:** **\[URS §x\]** = documented requirement; **\[Rec\]** = recommendation by the project team; **\[Assumption\]** = assumed in the absence of detail and listed for confirmation in Section 12. No programming language, framework, database or payment provider is assumed.

## 2. Project Scope and Assumptions

Scope is a customer-facing ordering website plus a management back-office on one shared backend and database; the mobile app is a separate bonus phase.

### 2.1 In scope – customer (patron) functionality

| Area | Documented requirement | Source |
| --- | --- | --- |
| Account | Create an account capturing at least mobile number, first name and email address; log in with an existing account | URS §3.1, §3.4.2 |
| Ordering | Enter number of drinks (compulsory, numeric, 1–10, maximum configurable); one “Drink Detail” container per drink | URS §3.4 field table |
| Customisation | Flavour (7 values), consistency “Thick or not” (4 values), topping (6 values) – all compulsory drop-downs | URS §3.4 field table |
| Pricing display | Read-only price per drink, frequent-customer discount (0 if none) and order total including VAT; “Done” enabled only when the container is complete | URS §3.4 field table |
| Pickup | Choose the restaurant and a suitable collection time | URS §3.1 |
| Payment | Pay online through a payment gateway before the order is confirmed | URS §2.2, §3.1, §4 |
| Notifications | On successful payment, email a payment receipt and order confirmation; final receipt by email | URS §3.1, §4 |
| Reports | View own order history | URS §3.4.7, §4 |

### 2.2 In scope – management functionality

| Area | Documented requirement | Source |
| --- | --- | --- |
| Access | At least two user profiles with separate logins (e.g. Manager, Client) | URS §4 |
| Configurable fees | Fees per flavour, per topping, per consistency; VAT currently 15% | URS §3.4, §4 |
| Frequent-customer discount | Configure tier-1 minimum orders, minimum drinks per qualifying order, tier-2 and tier-3 order/drink thresholds, and maximum discount amount | URS §3.4.5 |
| Lookup manager | Add, edit, delete lookups (flavours, toppings, etc.) and fixed values (min/max drinks, VAT, etc.); changes apply to new transactions only | URS §3.4.3, §3.4.6, §4 |
| Auditing | Full audit of every field change and transaction, with an audit report using user-operable filters | URS §3.3, §4 |
| Reports | Orders per selected period; order volumes per day of week (trend); lookup changes – who changed what to what | URS §3.4.4, §3.4.7, §4 |

### 2.3 Bonus scope

A standalone Android/iOS app that replicates the website’s functionality and works with the same management-controlled backend \[URS §5.2\]. Delivered only after the core web application is accepted.

### 2.4 Out of scope (unless confirmed)

- Restaurant-side order preparation, kitchen display or “ready for collection” workflow (not in the URS).
- Refunds and order cancellation by the patron (only a “Cancelled” status appears in the example report screen).
- Loyalty points, promotions or vouchers beyond the frequent-customer discount.
- Multi-currency and multi-language support.

### 2.5 Assumptions

| ID | Assumption | Impact if wrong |
| --- | --- | --- |
| A-01 | Team: 2 full-stack developers, 1 QA/tester, a part-time technical lead/PM; 1 mobile developer for the bonus phase | Durations scale with team size; total effort in person-days is also given |
| A-02 | Drink price = flavour fee + topping fee + consistency fee; order total = sum of drink prices − discount + VAT | Pricing engine rework |
| A-03 | Configured fees are VAT-exclusive; VAT (15%) is added to the discounted subtotal | Totals and receipts change |
| A-04 | Each discount tier carries a configurable discount percentage (the URS lists thresholds but not the discount value per tier) | Discount engine rework |
| A-05 | Only paid orders count towards frequent-customer history | Discount eligibility changes |
| A-06 | Restaurants are maintained as a lookup list by management | Extra screen if restaurants come from elsewhere |
| A-07 | Currency is South African rand (ZAR), matching the 15% VAT rate | Gateway and formatting choice |
| A-08 | Sign-up captures first name, surname (optional), mobile number, email and a password of at least 8 characters, reconciling URS §3.1 with the §3.4.2 example screen | Minor UI and data change |
| A-09 | “Delete” on lookup screens is a soft delete (deactivate), so historical orders keep their references | Data integrity of history |
| A-10 | Payment gateway provides a sandbox, a hosted or tokenised checkout (no card data stored by the application) and server-to-server payment notifications | PCI scope and effort increase |
| A-11 | Hosting environment, technology stack and gateway are chosen in Milestone 1–2; estimates are stack-neutral | Learning-curve risk |
| A-12 | Business stakeholder is available for UAT and sign-off within 2 working days of each request | Schedule slips |

## 3. High-Level Development Roadmap

The project has **15 milestones in 6 phases**; the core web application (Milestones 1–14) completes on working day 66, and the bonus mobile app (Milestone 15) on day 96.

| Phase | Milestones | Working days | Exit gate |
| --- | --- | --- | --- |
| 1. Discovery and design | M1 Requirements and planning; M2 Architecture and database design | 1–10 | Signed-off scope, data model, architecture and API contract |
| 2. Foundation | M3 Environment and project foundation | 11–15 | Running skeleton app with database, CI, audit hook and error handling |
| 3. Core platform | M4 Registration, login and permissions; M10 Lookup management; M11 Auditing | 16–31 | Both roles can log in; management can maintain versioned lookups; every change audited |
| 4. Ordering and money | M5 Ordering UI; M6 Pricing, VAT and discount engine; M7 Order processing; M8 Payment gateway; M9 Emails | 23–46 | Patron can order, pay and receive confirmation and receipt emails end to end |
| 5. Reporting, test and release | M12 Reporting; M13 Integration and E2E testing; M14 UAT and deployment | 38–66 | UAT sign-off and production go-live |
| 6. Bonus | M15 Standalone mobile app | 67–96 | Mobile app accepted against the same backend |

### 3.1 Recommended implementation sequence

1. **M1 → M2 → M3** strictly in order: scope drives design, design drives the skeleton.
2. **M4 and M11 in parallel** once the skeleton exists: authentication and the audit framework are cross-cutting and must be in place before feature tables multiply.
3. **M10 and M5 in parallel**, followed closely by **M6**: lookups and configuration feed both the ordering screen and the pricing engine.
4. **M7 → M8 → M9**: an order must exist before it can be paid for, and emails fire on payment success.
5. **M12** starts once orders and audit data are flowing (day 38).
6. **M13 → M14** strictly in order, then **M15** as a separate phase \[URS §5; project constraint 2\].

### 3.2 Critical dependencies

- **Payment gateway choice and sandbox credentials** – needed by day 37; request in week 1 (M1).
- **Configuration-versioning design (M2)** – every pricing, order and report feature depends on it.
- **Audit framework (M3/M11)** – must precede feature tables so no change goes unaudited.
- **Email service and sender domain** – needed by day 42; set up in M3.
- **Stakeholder answers to Section 12 questions** – especially discount percentages per tier and pickup-time rules, needed before M6 and M5.

### 3.3 Key project risks (detailed in Section 9)

Payment gateway integration, discount-rule ambiguity, price/VAT rounding errors, configuration changes leaking into existing orders, and incomplete auditing are the five risks most likely to delay sign-off.

## 4. Detailed Milestone Breakdown

Core web delivery totals **158 person-days** across Milestones 1–14; the bonus mobile app adds about **45 person-days**. Effort is person-days; duration is elapsed working days with the team in A-01.

| Milestone | Goal | Key tasks | Deliverables | Dependencies | Priority | Effort (person-days) / Duration (working days) |
| --- | --- | --- | --- | --- | --- | --- |
| M1 Requirements analysis and planning | Agreed, traceable scope | Analyse URS v1.1; classify mandatory vs bonus; log assumptions and questions; choose stack, hosting, gateway; plan sprints | Requirements catalogue, assumptions/questions log, traceability matrix v1, project plan, decision log | URS v1.1 | Critical | 5 / 4 |
| M2 System architecture and database design | A design every later milestone builds on | Architecture; ERD; versioned lookup and price-snapshot model; roles/permissions matrix; API contract; audit design; security design | Architecture document, ERD and data dictionary, API specification, RBAC matrix, audit and security design | M1 | Critical | 10 / 6 |
| M3 Development environment and foundation | Running, deployable skeleton | Repos and branching; CI pipeline; dev/test environments; DB migrations and seed data; app structure; logging, error handling, audit hook; config/secrets; email and gateway sandbox accounts | Repository, CI pipeline, test environment URL, migration scripts, seeded lookups, coding standards | M2 | Critical | 9 / 5 |
| M4 Registration, login and permissions | Two roles with secure separate logins | Sign-up, login, logout, profile; password hashing and policy; session/token handling; Manager and Patron roles; route guards | Sign-up and login screens, profile screen, auth API, RBAC enforcement, seeded Manager account | M3 | Critical | 12 / 7 |
| M5 Milkshake ordering and customisation | Patron can build a valid order | Order screen; number-of-drinks validation (min/max from config); dynamic Drink Detail containers; flavour, consistency, topping drop-downs; restaurant and pickup-time selection; Done-button logic | Order screen, drink container component, restaurant/pickup selection, client and server validation | M4; seeded lookups (M3) | Critical | 14 / 9 |
| M6 Pricing, VAT and discount engine | Correct, testable prices on every order | Fee lookup per flavour/topping/consistency; drink price; frequent-customer tier evaluation; max-discount cap; VAT; rounding rules; price-quote API | Pricing service, discount service, price-quote API, automated calculation test suite | M2 model; M10 config tables | Critical | 10 / 8 |
| M7 Order processing and database transactions | Orders saved atomically with snapshot prices | Order header and line persistence; price/VAT/discount snapshot; status model; server-side revalidation; transactions and concurrency; order history query | Order API, status lifecycle, transactional persistence, order history API | M5, M6 | Critical | 10 / 7 |
| M8 Payment gateway integration | Paid orders, and only paid orders, are confirmed | Gateway client; checkout redirect/session; success, failure and cancel handling; callback/webhook verification; idempotency; payment records linked to orders | Payment integration, payment table, callback endpoint, reconciliation log | M7; gateway sandbox (M3) | Critical | 14 / 10 |
| M9 Email notifications and receipts | Patrons receive confirmation and receipt | Templates for order confirmation, payment confirmation and final receipt; send on payment success; retry queue; delivery log | Email templates, email service, delivery log, resend function | M8; email service (M3) | Critical | 6 / 5 |
| M10 Lookup management and configuration | Management controls all values without code changes | CRUD screens for flavours, toppings, consistencies, restaurants, fees, VAT, min/max drinks, discount tiers; versioning and soft delete; validation | Lookup manager screens, configuration API, versioned lookup tables | M4 (Manager role); M2 model | Critical | 14 / 8 |
| M11 Auditing and transaction history | Every change is recorded and searchable | Audit capture for inserts, updates, deletes; user, old value, new value, timestamp; config and transaction audit; audit query API with filters; tamper protection | Audit table and capture mechanism, audit query API, audit test suite | M3 audit hook | Critical | 10 / 9 |
| M12 Reporting and management dashboard | Patrons and managers get required reports | Patron order history; orders per period; volumes by day of week; lookup change report; audit report; filters, search, paging, export | Patron history screen, management dashboard with Orders, Trends and Audit tabs, report APIs | M7, M10, M11 | High | 14 / 9 |
| M13 System integration and end-to-end testing | Whole system proven before UAT | E2E order-to-email scenarios; pricing regression; payment failure paths; permissions; audit completeness; security scan; performance smoke test | Test plan, executed test cases, defect log, security and performance reports | M8, M9, M12 | Critical | 16 / 10 |
| M14 User acceptance testing and deployment | Accepted application live in production | UAT scripts; UAT sessions; defect fixes; production environment; deployment; smoke test; user and technical documentation | UAT sign-off, production URL, release notes, user guide, technical/operations guide | M13 | Critical | 14 / 10 |
| M15 Bonus: standalone mobile app | Mobile patron app on the same backend | Backend reuse review; mobile UI; auth, ordering, pricing display, payment, history; device testing; store packaging | Android/iOS build, mobile test report, store-ready packages | M14 (core accepted) | Low (bonus) | 45 / 30 |

## 5. Milestone Goals and Acceptance Criteria

Each milestone is complete only when every acceptance criterion (AC) below is met, its evidence is filed, and the Definition of Done (Section 10) is satisfied. AC IDs are referenced in the traceability matrix (Section 11).

### M1 – Requirements Analysis and Project Planning

**Must achieve:** a shared, signed-off understanding of scope, with every URS requirement catalogued.

- **AC-1.1** Every statement in URS §2–§5 is catalogued with an ID and classified as Mandatory, Functional or Bonus (100% coverage, checked by a second reviewer).
- **AC-1.2** Assumptions and open questions are logged with an owner and a needed-by date.
- **AC-1.3** Technology stack, hosting and payment gateway decisions are recorded with rationale.
- **AC-1.4** Project plan, team roles and sprint calendar are approved by the stakeholder.

**Evidence:** requirements catalogue, decision log, signed plan. **Test before sign-off:** walkthrough review of the catalogue against the URS, page by page.

### M2 – System Architecture and Database Design

**Must achieve:** a design that enforces “new transactions only” configuration, full auditing and role separation by construction.

- **AC-2.1** ERD covers users, roles, restaurants, flavours, toppings, consistencies, fee versions, configuration settings, discount tiers, orders, order lines, payments, email log and audit log.
- **AC-2.2** Order lines store snapshot values (item names, unit fees, VAT rate, discount) so later configuration changes cannot alter them \[URS §3.4.6\].
- **AC-2.3** RBAC matrix lists every screen and API endpoint against Patron and Manager roles.
- **AC-2.4** API contract defines every endpoint, request, response and error code needed by web and (later) mobile clients.
- **AC-2.5** Audit design names the mechanism, the audited tables and the stored fields (who, what, old, new, when).

**Evidence:** reviewed architecture document, ERD, data dictionary, API spec. **Test before sign-off:** design review with technical lead; walk three scenarios through the model (price change after order, lookup deletion, failed payment).

### M3 – Development Environment and Project Foundation

**Must achieve:** any developer can clone, build, test and deploy the skeleton to the test environment.

- **AC-3.1** A new developer sets up locally from the README in under 2 hours.
- **AC-3.2** CI runs build, linting and unit tests on every pull request; main branch is protected.
- **AC-3.3** Database is created from migration scripts and seeded with the URS lookup values (7 flavours, 4 consistencies, 6 toppings, min 1, max 10, VAT 15%).
- **AC-3.4** Centralised error handling returns no stack traces to the client; errors are logged with a correlation ID.
- **AC-3.5** Secrets are held outside source control; gateway sandbox and email service credentials are configured in the test environment.

**Evidence:** CI run history, test environment URL, seed script. **Test before sign-off:** fresh-environment build; deliberate error to confirm safe error output.

### M4 – User Registration, Login and Permissions

**Must achieve:** patrons self-register; managers log in separately; each sees only what their role allows \[URS §3.1, §3.4.2, §4\].

- **AC-4.1** Sign-up captures first name, mobile number, email address and password; email is unique; invalid formats are rejected with clear messages.
- **AC-4.2** Passwords are stored only as salted hashes and must be at least 8 characters (per URS example screen).
- **AC-4.3** At least two profiles exist with two separate logins: one Manager, one or more Patrons \[URS §4\].
- **AC-4.4** A Patron calling any management endpoint or screen receives an access-denied response (verified for 100% of management endpoints).
- **AC-4.5** Sessions expire after inactivity and on logout; repeated failed logins are throttled.
- **AC-4.6** Profile changes are audited.

**Evidence:** test report, RBAC test matrix results. **Test before sign-off:** positive/negative registration tests, role-boundary tests for every endpoint, session expiry test.

### M5 – Milkshake Ordering and Customisation

**Must achieve:** the order screen behaves exactly as the URS §3.4 field table specifies.

- **AC-5.1** Number of drinks is compulsory, accepts numeric input only, and rejects values below the configured minimum (1) or above the configured maximum (10).
- **AC-5.2** Changing the configured maximum (e.g. to 12) takes effect on the order screen for new orders without a code change.
- **AC-5.3** Exactly N Drink Detail containers display for N drinks.
- **AC-5.4** Flavour, Thick-or-not and Topping are compulsory drop-downs populated from active lookups.
- **AC-5.5** Price for a drink, Frequent Customer Discount and Total amount are read-only and refresh on each selection.
- **AC-5.6** The Done button is enabled only when every field in that container is completed.
- **AC-5.7** Patron must select a restaurant and a pickup time before proceeding to payment; invalid times are rejected.
- **AC-5.8** All validation is repeated on the server; a tampered request is rejected.

**Evidence:** screen recordings of each rule, test report. **Test before sign-off:** boundary tests (0, 1, 10, 11, letters, blank), container count tests, usability check on mobile-width browser.

### M6 – Pricing, VAT and Discount Engine

**Must achieve:** every displayed and stored amount is correct to the cent.

- **AC-6.1** Drink price equals the configured flavour + topping + consistency fees (A-02) for 100% of combinations (7 × 6 × 4 = 168).
- **AC-6.2** VAT is calculated at the configured rate (15%) and rounded to 2 decimals using a documented rule.
- **AC-6.3** Frequent-customer discount applies the highest tier for which both order-count and drinks-per-order thresholds are met; 0 is shown when none applies.
- **AC-6.4** Discount never exceeds the configured maximum discount amount.
- **AC-6.5** Server-side total always equals the total displayed to the patron; the server value is authoritative.
- **AC-6.6** Automated test suite of at least 50 calculation cases, signed off against a stakeholder-checked spreadsheet, passes 100%.

**Evidence:** calculation test report, reconciliation spreadsheet. **Test before sign-off:** table-driven unit tests, tier boundary tests (threshold −1, =, +1), cap tests.

### M7 – Order Processing and Database Transactions

**Must achieve:** orders are stored completely or not at all, and never change after the fact.

- **AC-7.1** An order and all its drink lines are saved in a single transaction; a forced failure leaves no partial order.
- **AC-7.2** Each line stores snapshot names, fees, VAT rate and discount; changing a fee afterwards leaves existing order totals unchanged \[URS §3.4.6\].
- **AC-7.3** Order status follows the documented lifecycle (e.g. Pending Payment → Paid/Confirmed, Payment Failed, Cancelled); invalid transitions are rejected.
- **AC-7.4** Order is not marked Confirmed until payment succeeds \[URS §2.2\].
- **AC-7.5** Order creation and every status change are audited.
- **AC-7.6** A patron can retrieve only their own orders.

**Evidence:** integration test results, DB inspection screenshots. **Test before sign-off:** rollback test, config-change-after-order test, status transition tests, cross-patron access test.

### M8 – Payment Gateway Integration

**Must achieve:** online payment through a gateway, with no order confirmed unless the gateway verifies payment \[URS §2.2, §3.1, §4\].

- **AC-8.1** Patron is taken from order summary to the gateway and back; the amount sent equals the server-calculated order total.
- **AC-8.2** Successful payment sets the order to Paid/Confirmed only after server-side verification of the gateway notification (signature/hash and amount check).
- **AC-8.3** Failed, cancelled and abandoned payments leave the order unconfirmed and show the patron a clear message with a retry option.
- **AC-8.4** Duplicate or replayed callbacks never create a second payment or a second confirmation (idempotency verified by test).
- **AC-8.5** Every payment attempt is stored with gateway reference, amount, status and timestamps, linked to its order, and audited.
- **AC-8.6** No card data is stored or logged by the application.

**Evidence:** sandbox transaction log, test report. **Test before sign-off:** sandbox success, decline, cancel, timeout, tampered-amount and duplicate-callback tests.

### M9 – Email Notifications and Receipts

**Must achieve:** confirmation of order and payment, and a final receipt, reach the patron’s registered email \[URS §3.1, §4\].

- **AC-9.1** On payment success, the patron receives an order confirmation and a payment confirmation (separate or combined, per Q-08).
- **AC-9.2** The final receipt shows order number, drinks with flavour/consistency/topping, unit prices, discount, VAT, total, restaurant, pickup time and payment reference.
- **AC-9.3** Email values come from the order snapshot, not current configuration.
- **AC-9.4** A failed send is logged and retried automatically; it never rolls back a successful payment.
- **AC-9.5** Managers can see delivery status and resend an email.

**Evidence:** sample emails, delivery log. **Test before sign-off:** send tests to multiple mail clients, simulated provider outage and retry.

### M10 – Lookup Management and Configuration

**Must achieve:** managers maintain every lookup and fixed value without developer help, and changes affect only new transactions \[URS §3.4.3, §3.4.6, §4\].

- **AC-10.1** Managers can add, edit and deactivate flavours, toppings, consistencies and restaurants, each with name, type, value/fee and last-updated date (per URS example screen).
- **AC-10.2** Managers can maintain configuration values: minimum and maximum drinks, VAT percentage, and all five frequent-customer discount settings \[URS §3.4.5\].
- **AC-10.3** Invalid configuration is rejected (e.g. min > max, negative fee, VAT outside 0–100%, overlapping discount tiers).
- **AC-10.4** After a change, new orders use the new value while existing open, completed and concluded orders keep their original values (verified by test).
- **AC-10.5** Deactivated items disappear from the order screen but remain visible in history and reports.
- **AC-10.6** Every lookup and configuration change is audited with user, old value, new value and timestamp.
- **AC-10.7** Only the Manager role can access lookup management.

**Evidence:** test report, audit records for sample changes. **Test before sign-off:** change-while-order-open test, deactivate-in-use test, validation tests, role tests.

### M11 – Auditing and Transaction History

**Must achieve:** a full audit of every field change and transaction, searchable by management \[URS §3.3, §4\].

- **AC-11.1** 100% of insert, update and delete operations on audited tables create an audit record with user, entity, field, old value, new value, action and timestamp.
- **AC-11.2** Payment, order status and email events are recorded as transaction audit entries.
- **AC-11.3** Audit records cannot be edited or deleted through the application.
- **AC-11.4** Audit query supports filters for date range, user, entity/table, action and free-text search, returning results within 3 seconds for 100,000 records \[Rec target\].
- **AC-11.5** An automated test enumerates audited tables and fails if any write path skips the audit.

**Evidence:** audit coverage test results, sample audit report. **Test before sign-off:** coverage test, tamper attempt, filter combinations.

### M12 – Reporting and Management Dashboard

**Must achieve:** the patron and management reports listed in URS §3.4.7 and §4, with user-operable filters.

- **AC-12.1** Patron sees their own order history with date, drinks, total and payment status, and nobody else’s.
- **AC-12.2** Manager sees orders for a single date or date range with date, time, flavour, topping, consistency and payment status (per URS example screen).
- **AC-12.3** Manager sees customer order volumes by day of the week for a selected period (trend analysis).
- **AC-12.4** Manager sees lookup changes showing who changed what from what to what, and when.
- **AC-12.5** Manager sees the audit report with the filters in AC-11.4.
- **AC-12.6** Reports support search, paging and export; report totals reconcile with database queries for a seeded dataset.

**Evidence:** report screenshots, reconciliation sheet. **Test before sign-off:** seeded-data reconciliation, filter tests, role tests.

### M13 – System Integration and End-to-End Testing

**Must achieve:** proof that the integrated system meets every mandatory requirement before users test it.

- **AC-13.1** 100% of planned E2E scenarios executed; 0 open Critical or High defects.
- **AC-13.2** Every mandatory requirement (MR-01 to MR-10) has at least one passing E2E test.
- **AC-13.3** Security testing (OWASP Top 10 checklist and an automated scan) shows no unresolved High or Critical findings.
- **AC-13.4** Performance smoke test: order and payment pages respond within 2 seconds at 50 concurrent users \[Rec target, to confirm\].
- **AC-13.5** Database integrity checks show no orphaned orders, lines, payments or audit gaps.

**Evidence:** test summary report, defect log, scan reports. **Test before sign-off:** full regression run on the test environment.

### M14 – User Acceptance Testing and Deployment

**Must achieve:** stakeholder acceptance and a verified production release.

- **AC-14.1** UAT scripts cover every requirement in Section 11 and are executed by stakeholder testers.
- **AC-14.2** UAT sign-off is received with 0 open Critical/High defects; Medium/Low defects have an agreed plan.
- **AC-14.3** Production is deployed from the CI pipeline with live gateway and email credentials; a smoke test (register, order, pay a low-value live transaction, receive emails, view reports) passes.
- **AC-14.4** Backups, monitoring and a rollback plan are in place and tested once.
- **AC-14.5** User guides (patron, manager) and technical/operations guide are delivered.

**Evidence:** signed UAT form, deployment log, smoke-test record. **Test before sign-off:** production verification checklist.

### M15 – Bonus: Standalone Mobile Application

**Must achieve:** an Android/iOS app replicating the website’s patron functionality against the existing backend \[URS §5.2\].

- **AC-15.1** App supports registration, login, ordering with all URS field rules, pickup selection, payment, confirmation and order history.
- **AC-15.2** App uses the same backend APIs; no business rule (pricing, discount, validation) is duplicated as the source of truth on the device.
- **AC-15.3** Prices, configuration and lookups shown in the app match the website for the same patron and moment.
- **AC-15.4** App passes testing on at least 2 Android and 2 iOS devices/OS versions agreed with the stakeholder.
- **AC-15.5** Store-ready builds and a deployment guide are delivered.

**Evidence:** device test report, builds. **Test before sign-off:** cross-channel consistency test (same order on web and app gives same total).

## 6. Developer Task Breakdown

The 87 tasks below are sized for one person each (0.5–10 person-days) and sum to each milestone’s effort in Section 4. Type: **PM** planning, **Arch** architecture, **FE** frontend, **BE** backend, **DB** database, **API** API/integration, **Ops** DevOps, **Test** testing.

| Task | Milestone | Type | Task description | Effort (person-days) |
| --- | --- | --- | --- | --- |
| T1.1 | M1 | PM | Build requirements catalogue from URS §2–§5; classify Mandatory / Functional / Bonus | 1.5 |
| T1.2 | M1 | PM | Assumptions and questions log; stakeholder clarification session (Section 12) | 1 |
| T1.3 | M1 | Arch | Evaluate and select stack, hosting and payment gateway; request sandbox account | 1.5 |
| T1.4 | M1 | PM | Project plan, sprint calendar, backlog in tracking tool | 1 |
| T2.1 | M2 | Arch | Architecture and deployment view: web client, backend API, database, gateway, email service | 2 |
| T2.2 | M2 | DB | ERD and data dictionary incl. versioned lookups, fee versions and order-line snapshots | 3 |
| T2.3 | M2 | API | API contract for auth, lookups, pricing, orders, payments, reports, audit | 1.5 |
| T2.4 | M2 | Arch | RBAC matrix; authentication approach; OWASP-based security design | 1.5 |
| T2.5 | M2 | DB | Audit design: mechanism, audited tables, stored fields | 1 |
| T2.6 | M2 | FE | Wireframes and visual style (“funky and modern”, own look and feel) for all screens | 1 |
| T3.1 | M3 | Ops | Repository, branching strategy, PR rules, CI build/lint/test pipeline | 2 |
| T3.2 | M3 | Ops | Dev and test environments; secrets management | 1.5 |
| T3.3 | M3 | DB | Migration framework, baseline schema, seed URS lookup values | 1.5 |
| T3.4 | M3 | BE | Application skeleton, layering, global error handling, structured logging | 1.5 |
| T3.5 | M3 | BE | Baseline audit hook on the data-access layer | 1 |
| T3.6 | M3 | FE | Frontend skeleton: routing, layout, theme, shared form components | 1 |
| T3.7 | M3 | API | Configure gateway sandbox and email service credentials in test environment | 0.5 |
| T4.1 | M4 | DB | Users, roles and session tables | 1 |
| T4.2 | M4 | BE | Sign-up API: validation, unique email, password hashing | 2 |
| T4.3 | M4 | BE | Login, logout, session/token management, failed-login throttling | 2 |
| T4.4 | M4 | BE | RBAC middleware on every endpoint; seed Manager account | 1.5 |
| T4.5 | M4 | FE | Sign-up and login screens | 2 |
| T4.6 | M4 | FE | Profile screen; role-aware navigation | 1.5 |
| T4.7 | M4 | Test | Auth and role-boundary tests | 2 |
| T5.1 | M5 | API | Read APIs for active flavours, toppings, consistencies, restaurants and order limits | 1.5 |
| T5.2 | M5 | FE | Order screen with number-of-drinks field and validation | 2 |
| T5.3 | M5 | FE | Dynamic Drink Detail containers, compulsory drop-downs, Done-button logic | 3 |
| T5.4 | M5 | FE | Restaurant and pickup-time selection | 1.5 |
| T5.5 | M5 | BE | Server-side order validation incl. limits and pickup-time rules | 2 |
| T5.6 | M5 | FE | Live price, discount and total display wired to price-quote API | 1.5 |
| T5.7 | M5 | Test | Field-rule, boundary and container tests | 2.5 |
| T6.1 | M6 | DB | Fee-version and discount-tier tables | 1 |
| T6.2 | M6 | BE | Drink price calculation from flavour, topping and consistency fees | 1.5 |
| T6.3 | M6 | BE | Frequent-customer eligibility, tier selection and max-discount cap | 2.5 |
| T6.4 | M6 | BE | VAT and rounding rules; order total | 1 |
| T6.5 | M6 | API | Price-quote endpoint | 1 |
| T6.6 | M6 | Test | Table-driven calculation tests; stakeholder reconciliation spreadsheet | 3 |
| T7.1 | M7 | DB | Orders and order-line tables with snapshot columns and status | 1.5 |
| T7.2 | M7 | BE | Transactional order creation with server-side re-pricing | 2.5 |
| T7.3 | M7 | BE | Order status state machine | 1.5 |
| T7.4 | M7 | API | Order history and detail endpoints with ownership checks | 1.5 |
| T7.5 | M7 | Test | Rollback, snapshot and status-transition tests | 3 |
| T8.1 | M8 | DB | Payments table with gateway reference and idempotency key | 1 |
| T8.2 | M8 | API | Gateway client and checkout initiation | 3 |
| T8.3 | M8 | API | Callback/notification endpoint: signature and amount verification, idempotent processing | 3 |
| T8.4 | M8 | FE | Payment success, failure, cancel and retry pages | 1.5 |
| T8.5 | M8 | BE | Pending-payment timeout and reconciliation job | 1.5 |
| T8.6 | M8 | Test | Sandbox success, decline, cancel, timeout, tamper and duplicate tests | 4 |
| T9.1 | M9 | BE | Templates: order confirmation, payment confirmation, final receipt | 1.5 |
| T9.2 | M9 | API | Email service integration with queue, retry and delivery log | 2 |
| T9.3 | M9 | FE | Manager view of delivery status with resend | 1 |
| T9.4 | M9 | Test | Rendering, content and failure/retry tests | 1.5 |
| T10.1 | M10 | DB | Versioning and soft delete for lookups and configuration | 1.5 |
| T10.2 | M10 | BE | Lookup CRUD APIs with validation | 3 |
| T10.3 | M10 | BE | Configuration and discount-tier APIs with validation | 2 |
| T10.4 | M10 | FE | Lookup screens: flavours, toppings, consistencies, restaurants | 3 |
| T10.5 | M10 | FE | Configuration screens: min/max drinks, VAT, discount settings | 2 |
| T10.6 | M10 | Test | “New transactions only”, validation and role tests | 2.5 |
| T11.1 | M11 | DB | Append-only audit table and indexes | 1 |
| T11.2 | M11 | BE | Generic field-level audit capture on all write paths | 3 |
| T11.3 | M11 | BE | Transaction audit events: payments, status changes, emails, logins | 1.5 |
| T11.4 | M11 | API | Audit query endpoint with filters and paging | 2 |
| T11.5 | M11 | Test | Automated audit-coverage and tamper tests | 2.5 |
| T12.1 | M12 | API | Report queries: orders per period, day-of-week volumes, lookup changes | 3 |
| T12.2 | M12 | FE | Patron order history screen | 1.5 |
| T12.3 | M12 | FE | Manager Orders tab: single date/range filter, search, paging, copy/export | 3 |
| T12.4 | M12 | FE | Trends tab: day-of-week and period charts | 2 |
| T12.5 | M12 | FE | Audit and lookup-change tab with filters | 2 |
| T12.6 | M12 | Test | Report reconciliation against seeded data | 2.5 |
| T13.1 | M13 | Test | E2E test plan and scenarios mapped to Section 11 | 2 |
| T13.2 | M13 | Test | Automated and manual E2E execution | 5 |
| T13.3 | M13 | Test | Security testing: OWASP checklist and automated scan | 3 |
| T13.4 | M13 | Test | Performance smoke test | 2 |
| T13.5 | M13 | BE/FE | Defect fixing | 4 |
| T14.1 | M14 | Test | UAT scripts and facilitated UAT sessions | 3 |
| T14.2 | M14 | BE/FE | UAT defect fixing | 3 |
| T14.3 | M14 | Ops | Production environment, live credentials, backups, monitoring | 3 |
| T14.4 | M14 | Ops | Production deployment and verification | 1.5 |
| T14.5 | M14 | PM | Patron guide, manager guide, technical/operations guide | 3.5 |
| T15.1 | M15 | Arch | Backend reuse assessment; mobile API gaps | 3 |
| T15.2 | M15 | FE | Mobile app shell, navigation, sign-up and login | 6 |
| T15.3 | M15 | FE | Mobile ordering, drink containers, pickup selection, price display | 10 |
| T15.4 | M15 | API | Mobile payment flow with the chosen gateway | 6 |
| T15.5 | M15 | FE | Mobile order history and profile | 4 |
| T15.6 | M15 | BE | Backend changes for mobile (token auth, deep-link return from payment) | 4 |
| T15.7 | M15 | Test | Device and cross-channel testing | 8 |
| T15.8 | M15 | Ops | Store packaging and deployment guide | 4 |

## 7. Dependencies and Critical Path

The critical path is **M1 → M2 → M3 → M4 → M5 → M7 → M8 → M13 → M14**, 66 working days; any slip on these milestones moves go-live day for day.

&#91;embedded content: milestone dependencies · critical path highlighted\]

M6, M9 and M12 have almost no float, so treat them as near-critical; M10 and M11 have the most slack (M11 finishes 13 days before M12 needs it).

### 7.1 What runs in parallel and what must be sequential

| Working days | Run in parallel | Condition for overlap |
| --- | --- | --- |
| 16–24 | M4 Auth and M11 Auditing | Audit hook and data-access layer from M3 are in place |
| 23–32 | M5 Ordering UI, M10 Lookup management, M6 Pricing | M10’s configuration tables are migrated before M6 starts on day 25; M5 uses seeded lookups until M10 screens are live |
| 37–46 | M8 Payment, M9 Email, M12 Reporting | M7 order model and status lifecycle are stable; M9 builds templates first and wires the payment-success trigger last |
| 67–96 | M15 Mobile, post-go-live support | Core web application accepted in M14 |

**Strictly sequential:** M1 → M2 → M3 (design before build); M7 → M8 (an order must exist to be paid); M13 → M14 → M15 (tested before UAT; core before bonus).

### 7.2 Critical-path activities to protect

- Gateway selection and sandbox request (T1.3) – longest external lead time.
- Versioned-configuration and snapshot data model (T2.2) – every money feature depends on it.
- Dynamic drink containers and server validation (T5.3, T5.5).
- Transactional order creation (T7.2) and payment callback verification (T8.3).
- E2E execution and defect fixing (T13.2, T13.5).

## 8. Estimated Project Timeline

The core web application goes live on **working day 66 (≈13 weeks)**, or **day 76** with a 15% contingency; the bonus mobile app adds 30 working days, finishing on day 96 (≈111 with contingency).

&#91;embedded content: suggested schedule · 15 milestones in working days\]

Blue bars form the critical path; grey bars run in parallel and can absorb small slips without moving go-live.

| Scope | Effort (person-days) | Duration (working days) | With 15% contingency |
| --- | --- | --- | --- |
| Core web application (M1–M14) | 158 | 66 | 76 |
| Bonus mobile app (M15) | 45 | 30 | 35 |
| **Total** | **203** | **96** | **111** |

### 8.1 Estimating assumptions

- Team per A-01: 2 full-stack developers, 1 QA/tester, part-time tech lead/PM; 1 mobile developer for M15. **A single developer** should plan on roughly the effort figure: about 158 working days (≈7–8 months) for the core web app.
- 1 working day = about 6 productive hours; weekends and public holidays are excluded.
- Estimates are technology-neutral and include unit testing, code review and documentation within each milestone, plus dedicated integration (M13) and UAT (M14) effort.
- Gateway sandbox access is available by day 15 and live merchant credentials by day 60.
- Stakeholder answers to Q-01 to Q-07 arrive by day 4; Q-05, Q-06 and Q-08 to Q-11 by day 10.
- No major scope changes after M1 sign-off; changes go through the change log and re-estimation.
- Contingency of 15% covers gateway onboarding delays, learning curve and rework from clarifications.

## 9. Risk Assessment

Payment integration and pricing/discount correctness are the two highest risks; both are mitigated by starting early and testing against an independent, stakeholder-checked reference.

| ID | Risk | Likelihood | Impact | Consequence | Mitigation | Owner |
| --- | --- | --- | --- | --- | --- | --- |
| R-01 | Payment gateway integration delays (account approval, sandbox access, callback behaviour, documentation gaps) | High | High | M8 slips and pushes M13/M14; orders confirmed without verified payment | Select gateway and request sandbox in M1; spike the callback flow during M3; verify every notification server-side; idempotency keys; reconciliation job; keep a second shortlisted provider | Tech lead |
| R-02 | Incorrect pricing or VAT calculations (rounding, VAT-inclusive vs exclusive) | Medium | High | Wrong charges, receipts and reports; loss of trust | Confirm A-02/A-03 in M1; single server-side pricing service; decimal (not floating-point) arithmetic; one documented rounding rule; 168-combination and boundary test suite; stakeholder reconciliation sheet | BE developer |
| R-03 | Frequent-customer discount rules ambiguous (no discount % per tier, cap semantics, which orders count) | High | Medium | Rework of M6 and M10; disputes in UAT | Raise Q-01 to Q-04 in M1; make every rule configurable; worked examples signed by stakeholder before M6 starts | PM |
| R-04 | Configuration changes alter existing transactions | Medium | High | Breaches URS §3.4.6; historical totals and reports change | Snapshot values on order lines; versioned, soft-deleted lookups; dedicated regression tests (AC-7.2, AC-10.4) | Architect |
| R-05 | Data integrity failures (partial orders, orphaned payments, concurrent updates) | Medium | High | Lost or duplicated orders and payments | Database transactions; foreign keys and constraints; optimistic locking on configuration; integrity checks in M13 | BE developer |
| R-06 | Audit incomplete (some write paths bypass audit) | Medium | High | Fails mandatory requirement MR-04 | Central audit mechanism from M3; append-only table; automated coverage test (AC-11.5); code-review checklist item | Tech lead |
| R-07 | Security vulnerabilities (injection, broken access control, weak sessions, exposed secrets) | Medium | High | Data breach, fraud, failed security review | OWASP-based design; parameterised queries; RBAC tests on every endpoint; secrets outside code; dependency and vulnerability scanning in CI; security test in M13 | Tech lead |
| R-08 | Email delivery failures or spam-filtering | Medium | Medium | Patrons do not receive confirmation or receipt | Reputable transactional email service; sender domain authentication (SPF/DKIM/DMARC); retry queue; delivery log and resend; emails never block payment confirmation | BE developer |
| R-09 | Scope creep (extra features, early mobile work, redesigns) | High | Medium | Core delivery slips | Signed scope baseline in M1; change-request log; bonus app only after M14 \[constraint 2\]; label additions \[Rec\] and prioritise separately | PM |
| R-10 | Pickup-time rules undefined (opening hours, lead time, restaurant capacity) | Medium | Medium | Patrons choose impossible times; UAT failures | Raise Q-05/Q-06; configurable minimum lead time \[Rec\]; validate on server | PM |
| R-11 | Stakeholder unavailable for clarifications or UAT | Medium | Medium | Blocked decisions; delayed sign-off | Agree response times (A-12); book UAT dates in M1; weekly demo | PM |
| R-12 | Team unfamiliar with chosen stack or gateway | Medium | Medium | Lower velocity in M3–M8 | Prefer known technology in M1; spikes in M3; 15% contingency | Tech lead |

## 10. Definition of Done

A task, and therefore a milestone, is Done only when every item below is true; the milestone additionally needs all its acceptance criteria met and, where marked, stakeholder sign-off.

| # | Criterion | Measure / evidence |
| --- | --- | --- |
| 1 | Development completed | All tasks for the milestone merged to the main branch; builds cleanly in CI |
| 2 | Code reviewed | Every pull request approved by at least one other developer, using a checklist covering security, audit coverage, validation and error handling |
| 3 | Automated tests written and passing | Unit and integration tests for new logic pass in CI; pricing, discount, audit and payment logic have table-driven tests |
| 4 | Functional testing passed | QA has executed the milestone’s test cases on the test environment with results recorded |
| 5 | Acceptance criteria satisfied | Each AC in Section 5 marked passed with linked evidence |
| 6 | Audit and security verified | New tables/fields are covered by the audit mechanism; new endpoints are covered by RBAC tests; no secrets in code |
| 7 | “New transactions only” preserved | Any change touching pricing, lookups or configuration passes the snapshot regression tests |
| 8 | No outstanding critical defects | 0 open Critical or High defects; Medium/Low logged with an agreed plan |
| 9 | Documentation updated | API spec, data dictionary, README, traceability matrix and user/technical guides reflect the change |
| 10 | Deployed to test environment | Milestone build running on the shared test environment and demonstrated |
| 11 | Stakeholder sign-off where applicable | Required for M1, M2, M6 (calculation reference), M10, M12, M14 and M15 |

## 11. Requirements Traceability Matrix

All 40 catalogued requirements (10 mandatory, 28 functional, 2 bonus) trace to at least one milestone and one acceptance criterion; none is uncovered.

| Req ID | Requirement | Source | Type | Milestone(s) | Acceptance criteria |
| --- | --- | --- | --- | --- | --- |
| MR-01 | Use of a database | URS §4, §3.2 | Mandatory | M2, M3, M7 | AC-2.1, AC-3.3, AC-7.1 |
| MR-02 | Configurable fees | URS §4 | Mandatory | M6, M10 | AC-6.1, AC-10.1, AC-10.2, AC-10.4 |
| MR-03 | Lookup table manager | URS §4 | Mandatory | M10 | AC-10.1 – AC-10.7 |
| MR-04 | Full auditing on any field changes and transactions | URS §4 | Mandatory | M3, M11, M12 | AC-2.5, AC-11.1 – AC-11.5, AC-12.5 |
| MR-05 | Confirmation email of orders and payments | URS §4 | Mandatory | M9 | AC-9.1, AC-9.4 |
| MR-06 | Use of a payment gateway | URS §4 | Mandatory | M8 | AC-8.1 – AC-8.6 |
| MR-07 | Two+ user profiles with two logins | URS §4 | Mandatory | M4 | AC-4.3, AC-4.4 |
| MR-08 | Final receipt by email | URS §4 | Mandatory | M9 | AC-9.2, AC-9.3 |
| MR-09 | User/patron report(s) | URS §4 | Mandatory | M12 | AC-12.1 |
| MR-10 | Management report(s) | URS §4 | Mandatory | M12 | AC-12.2 – AC-12.6 |
| FR-01 | Create account with mobile number, first name, email | URS §3.1 | Functional | M4 | AC-4.1, AC-4.2 |
| FR-02 | Log in with existing account | URS §3.1, §3.4.2 | Functional | M4 | AC-4.3, AC-4.5 |
| FR-03 | Place an order for milkshake(s) | URS §3.1 | Functional | M5, M7 | AC-5.3, AC-7.1 |
| FR-04 | Select pickup restaurant | URS §3.1 | Functional | M5, M10 | AC-5.7, AC-10.1 |
| FR-05 | Select suitable pickup time | URS §3.1 | Functional | M5 | AC-5.7 |
| FR-06 | Payment made before order is confirmed | URS §2.2, §3.1 | Functional | M7, M8 | AC-7.4, AC-8.2, AC-8.3 |
| FR-07 | On successful payment, email payment receipt and order confirmation | URS §3.1 | Functional | M9 | AC-9.1, AC-9.2 |
| FR-08 | All details stored and updated in a database | URS §3.2 | Functional | M2, M7 | AC-2.1, AC-7.1 |
| FR-09 | Full audit of transactions with report and user-operable filters | URS §3.3 | Functional | M11, M12 | AC-11.4, AC-12.5 |
| FR-10 | Number of drinks: compulsory, numeric, 1–10, max configurable | URS §3.4 | Functional | M5, M10 | AC-5.1, AC-5.2, AC-10.2 |
| FR-11 | Flavour drop-down: 7 values | URS §3.4 | Functional | M3, M5 | AC-3.3, AC-5.4 |
| FR-12 | Thick-or-not drop-down: 4 values | URS §3.4 | Functional | M3, M5 | AC-3.3, AC-5.4 |
| FR-13 | Topping drop-down: 6 values | URS §3.4 | Functional | M3, M5 | AC-3.3, AC-5.4 |
| FR-14 | Read-only price per drink incl. topping and consistency | URS §3.4 | Functional | M5, M6 | AC-5.5, AC-6.1 |
| FR-15 | Read-only frequent-customer discount; 0 if none | URS §3.4 | Functional | M5, M6 | AC-5.5, AC-6.3 |
| FR-16 | Read-only order total with VAT included | URS §3.4 | Functional | M5, M6 | AC-5.5, AC-6.2, AC-6.5 |
| FR-17 | Done button available only when container complete | URS §3.4 | Functional | M5 | AC-5.6 |
| FR-18 | One Drink Detail container per drink ordered | URS §3.4 | Functional | M5 | AC-5.3 |
| FR-19 | Fees per flavour, per topping, per consistency; VAT 15% | URS §3.4 | Functional | M6, M10 | AC-6.1, AC-6.2, AC-10.1, AC-10.2 |
| FR-20 | Frequent-customer discount configuration (5 settings) | URS §3.4.5 | Functional | M6, M10 | AC-6.3, AC-6.4, AC-10.2 |
| FR-21 | Manage lookups and fixed values (min/max drinks, VAT, etc.) | URS §3.4.3, §3.4.6 | Functional | M10 | AC-10.1, AC-10.2 |
| FR-22 | Changes affect new transactions only | URS §3.4.6 | Functional | M2, M7, M10 | AC-2.2, AC-7.2, AC-10.4 |
| FR-23 | Audit of lookup management changes | URS §3.4.6 | Functional | M10, M11 | AC-10.6, AC-11.1 |
| FR-24 | Patron can check order history | URS §3.4.7 | Functional | M7, M12 | AC-7.6, AC-12.1 |
| FR-25 | Orders per selected period | URS §3.4.7 | Functional | M12 | AC-12.2 |
| FR-26 | Customer order volumes per day of week (trend) | URS §3.4.7 | Functional | M12 | AC-12.3 |
| FR-27 | Lookup change report: who changed what to what | URS §3.4.7 | Functional | M12 | AC-12.4 |
| FR-28 | Funky, modern website with own look and feel | URS §2.2, §3.4 | Functional | M2, M14 | AC-14.1, AC-14.2 |
| B-01 | Standalone Android/iOS app replicating website functionality | URS §5.2 | Bonus | M15 | AC-15.1, AC-15.4, AC-15.5 |
| B-02 | App works with the management-controlled backend | URS §5.2 | Bonus | M15 | AC-15.2, AC-15.3 |

## 12. Outstanding Questions and Recommendations

Fifteen questions need stakeholder answers; Q-01 to Q-06 block Milestones 5 and 6 and should be closed in Milestone 1.

### 12.1 Questions requiring clarification

| ID | Question | Why it matters | Needed by | Blocks |
| --- | --- | --- | --- | --- |
| Q-01 | What discount percentage (or amount) applies at each of the three frequent-customer tiers? URS §3.4.5 gives thresholds only. | Discount cannot be calculated without it | Day 4 | M6, M10 |
| Q-02 | Is “maximum amount of discount” a currency amount per order, a percentage, or a lifetime total? | Defines the cap rule | Day 4 | M6 |
| Q-03 | Which past orders count towards tiers: paid only, collected only, within a rolling period or all time? | Eligibility logic | Day 4 | M6 |
| Q-04 | Is the discount applied before or after VAT, and does it apply to the whole order or per drink? | Totals and receipts | Day 4 | M6 |
| Q-05 | Which restaurants exist, and what are their opening hours? Should management maintain them as a lookup? | Pickup selection and validation | Day 10 | M5, M10 |
| Q-06 | What pickup-time rules apply: minimum lead time (URS §2.1 cites 10–15 minutes per custom shake), time-slot size, latest pickup, same-day only? | Prevents impossible pickup times | Day 10 | M5 |
| Q-07 | Are configured fees VAT-inclusive or VAT-exclusive? What rounding rule applies? | Pricing accuracy | Day 4 | M6 |
| Q-08 | Should order confirmation, payment confirmation and final receipt be separate emails or one combined email? Is a “final receipt” sent at payment or at collection? | Template count and trigger | Day 10 | M9 |
| Q-09 | Sign-up fields: URS §3.1 lists mobile number, first name, email; the §3.4.2 example shows full name, email, password. Which set is required? | Registration form and data model | Day 4 | M4 |
| Q-10 | Can patrons cancel an order or request a refund? (“Cancelled” appears in the example report.) | Status model and gateway refund integration | Day 10 | M7, M8 |
| Q-11 | Does each drink in a multi-drink order carry its own flavour, consistency and topping (assumed yes), and can one order mix restaurants (assumed no)? | Order model | Day 10 | M2, M5 |
| Q-12 | Is there a preferred payment gateway, hosting platform or technology stack? | Architecture and estimates | Day 4 | M2, M3 |
| Q-13 | Are more than two management roles needed (e.g. restaurant staff vs head-office manager)? | RBAC design | Day 10 | M4 |
| Q-14 | What data-retention and privacy obligations apply to patron personal data (e.g. South Africa’s POPIA)? | Consent text, retention, export/delete | Day 10 | M2, M4 |
| Q-15 | What performance and availability targets apply (concurrent users, uptime)? | Test targets in M13 | Day 45 | M13 |

### 12.2 Recommendations (not URS requirements)

- **\[Rec\] Version configuration, don’t overwrite it.** Store fee and setting changes as new effective-dated versions and copy applicable values into each order line; this is the simplest reliable way to satisfy URS §3.4.6.
- **\[Rec\] Soft-delete lookups.** Deactivate instead of physically deleting, so history, reports and audit stay readable.
- **\[Rec\] Make the server the single source of truth for prices.** The UI shows quotes; the order and payment amount are recalculated on the server.
- **\[Rec\] API-first backend.** Design the backend API in M2 so the web client and the bonus mobile app share it, reducing M15 effort.
- **\[Rec\] Configurable minimum pickup lead time** defaulting to 15 minutes, based on the 10–15 minute preparation time in URS §2.1.
- **\[Rec\] Add “Ready” and “Collected” statuses** later if restaurants need a preparation workflow; not part of current scope.
- **\[Rec\] Request the gateway sandbox in week 1** and run a callback spike during M3 to de-risk the critical path.
- **\[Rec\] Weekly stakeholder demo** from M4 onwards to catch misunderstandings early and reduce UAT defects.
