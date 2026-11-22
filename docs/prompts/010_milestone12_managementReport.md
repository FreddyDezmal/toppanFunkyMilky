# Milestone 12 — Management Reporting and Order History

## Your Role

Act as my Senior ASP.NET Core Architect, PostgreSQL Database Architect, Full-Stack Engineer, Reporting and Data Analyst, Application Security Engineer, QA Engineer, and Technical Mentor.

We are continuing **Order Milkshake for Pickup** for **Milky Shaky Custom Drinks**.

I am the sole developer, using Windows 11, Visual Studio Code and PowerShell.

Your task is to implement **Milestone 12**, following the actual requirements and architecture of my existing project.

**Do not start coding immediately. First review the roadmap, requirements, existing implementation and M11 integration notes. Present a gap analysis and obtain my approval.**

Work in small, testable steps. Explain each step clearly, provide exact file paths and PowerShell-compatible commands, and wait for my confirmation before proceeding.

---

## 1. Technology Stack

**Backend**
- ASP.NET Core 10 Web API
- C#
- Entity Framework Core 10
- Npgsql and PostgreSQL
- Supabase database, schema `milkshake`
- ASP.NET Core Identity
- Domain, Application, Infrastructure and API layers

**Frontend**
- React 19
- TypeScript
- Vite 8
- Tailwind CSS v4

**Testing**
- xUnit
- Testcontainers PostgreSQL 17
- Vitest
- Playwright
- axe accessibility testing

**Security**
- Secure HTTP-only authentication cookies
- CSRF protection
- PatronOnly and ManagerOnly authorisation
- Rate limiting
- Strict input validation
- Safe ProblemDetails responses
- Audited manager actions
- Server-side secrets

Preserve established project conventions.

---

## 2. Completed Work

The following milestones have been reported complete:

- M1 — Foundation
- M4 — Authentication and security
- M5 — Ordering
- M6 — Core pricing
- M7 — Order persistence
- M8 — Paystack payments
- M8b — Cancellations and refunds
- M9 — Email notifications
- M10 — Manager configuration
- M11 — Auditing and transaction history

Some stakeholder-dependent M6 requirements and formal M2/M3 sign-offs remain unresolved.

### Latest Test Baseline

The M11 report dated 20 November 2026 records:

- 1,050 backend tests passing
- 164 Vitest tests passing
- 9 Playwright tests passing

**Total: 1,223 automated tests passing.**

Preserve these tests.

Do not claim a test passed unless it was actually executed.

---

## 3. Authoritative Documents

Before implementation, inspect:

1. User Requirement Specification v1.1.
2. Development Roadmap — exact M12 section.
3. M7 Order Placement report.
4. M8 Payment Gateway report.
5. M8b Cancellation and Refunds report.
6. M9 Email Notifications report.
7. M10 Manager Configuration report.
8. M11 Auditing and Transaction History report.
9. M11 audit architecture documentation.
10. Relevant decision registers.

If the roadmap or URS is missing, request the relevant sections.

**Extract the exact AC-12.x acceptance criteria and T12.x tasks. Do not invent missing criteria.**

The M11 report explicitly identifies:

- AC-12.1 — Patron order history with pagination and filters.
- AC-12.4 — Lookup-change reporting.
- AC-12.5 — Management audit interface.
- AC-12.6 — Export using masked API output.

Determine the actual requirements for AC-12.2, AC-12.3 and any additional M12 criteria by reading the roadmap.

Create a requirements traceability table showing each requirement, current implementation, missing work, proposed tests and completion evidence.

---

## 4. Existing Features to Reuse

### Patron Order History

Already available:

- `/orders` — My Orders.
- `/orders/:reference` — Order details.
- `GET /api/ordering/orders`
- `GET /api/ordering/orders/{reference}`

The existing list returns the 20 newest orders.

M11 verified that historical order values remain unchanged after catalogue, restaurant, VAT and pricing changes.

Extend the existing implementation rather than creating a parallel history system.

### Manager Orders

M8b introduced manager order access, cancellation requests and refund attention workflows.

Inspect and reuse the existing manager services and pages.

### Audit Query API

M11 introduced:

- `GET /api/manager/audit/changes`
- `GET /api/manager/audit/security`

Existing capabilities include:

- Date filters.
- User filters.
- Actor filters.
- Entity/table filters.
- Action filters.
- Free-text search.
- Opaque keyset pagination.
- Personal-data masking.
- ManagerOnly authorisation.
- Rate limiting.
- Append-only audit protection.

The M11 report states that eight query combinations completed in under three seconds over 100,000 seeded records.

**Do not rebuild these endpoints. Use them for the M12 reporting interfaces wherever appropriate.**

### Email Notifications

M9 introduced transactional email receipts and manager email status.

Historical receipts use immutable snapshots.

Do not reconstruct historical prices from current configuration.

---

## 5. Milestone Objective

Complete the roadmap-defined reporting and history functionality.

Likely work areas, subject to exact roadmap verification:

1. Patron order history.
2. Management reporting.
3. Order and transaction reporting.
4. Lookup-change reporting.
5. Management audit interface.
6. Data exports.
7. Security and privacy.
8. Automated verification.

Do not introduce a new analytics platform or reporting database unless requirements justify it.

Prefer secure, efficient queries against existing PostgreSQL records.

---

## 6. Patron Order History — AC-12.1

Extend My Orders so patrons can access their required historical orders.

Inspect the roadmap for the exact filtering and pagination requirements.

Propose:

- Bounded pagination.
- Stable sorting.
- Status filtering.
- Date-range filtering, if required.
- Restaurant filtering, if required.
- Clear empty states.
- Loading and error states.
- Links to order details.
- Responsive and accessible controls.

Reuse existing APIs and components.

### Security

- Derive Patron identity from the authenticated session.
- Never accept a browser-supplied Patron ID as authority.
- Prevent cross-Patron access.
- Bound page sizes.
- Validate filters.
- Prevent information leakage through counts or error messages.

### Historical Accuracy

Use stored order snapshots.

Changing a flavour, topping, restaurant, VAT rate or price must not alter historical display values.

Preserve the M11 historical accuracy tests.

---

## 7. Management Reporting — Verify Exact AC-12.2 and AC-12.3

Before designing reports, extract the actual requirements from the roadmap.

Inspect the existing data model for:

- Orders.
- Order items.
- Payments.
- Payment attempts and events.
- Refunds.
- Cancellation requests.
- Restaurants.
- Audit logs.
- Security events.

Identify which metrics the requirements actually call for.

Potential metrics to consider **only if required or approved**:

- Order counts by period.
- Revenue by period.
- Orders by restaurant.
- Orders by status.
- Popular flavours, consistencies and toppings.
- Payment success and failure counts.
- Refund totals.
- Cancellation counts.
- VAT totals.
- Discount totals.

### Financial Definitions Must Be Approved

Do not treat every created order as revenue.

Define reporting rules for:

- Pending payments.
- Verified successful payments.
- Failed payments.
- Cancelled orders.
- Full refunds.
- Partial refunds.
- Refund processing.
- Refund failures.
- Cancellation fees.
- Late or duplicate payments requiring refunds.

Clarify whether reports represent:

- Gross order value.
- Verified cash collected.
- Net sales.
- Refund liabilities.
- VAT amounts.
- Operational order volume.

Do not mix these definitions.

Use stored financial snapshots and verified payment/refund records.

Do not use current prices to calculate historical sales.

If financial semantics are not specified in the roadmap, present options and obtain approval before implementing them.

---

## 8. Lookup-Change Report — AC-12.4

Use the existing M11 audit query endpoint where possible.

The M11 report identifies the relevant entities:

- Flavours.
- Toppings.
- Consistencies.
- Their effective-dated price tables.
- Restaurants.
- System settings.
- Discount tiers.

The report should show, as required:

- Entity.
- Field changed.
- Previous value.
- New value.
- Actor.
- Timestamp.
- Operation.

Respect existing masking rules.

Do not expose secrets or unnecessarily reveal personal information.

Ensure historical changes remain searchable even if the underlying catalogue item is deactivated.

Avoid duplicate reporting tables.

---

## 9. Management Audit Interface — AC-12.5

Create a manager-facing audit interface using the existing M11 APIs.

Follow the project's existing Management UI patterns.

Proposed route:

`/manager/audit`

Confirm naming and routing conventions before implementation.

### Required Behaviour

Expose the roadmap-defined filters and pagination.

Reuse:

- `/api/manager/audit/changes`
- `/api/manager/audit/security`

Display explicit system actors such as:

- Paystack webhook.
- Reconciliation worker.
- Email dispatcher.
- Seeder.
- Manager provisioning.

For historical records with no actor, display **Not recorded** rather than inventing one.

### Security

- ManagerOnly authorisation.
- No Patron access.
- No raw unmasked audit payloads.
- No direct database access from the browser.
- Safe errors.
- Bounded queries.
- No editing or deleting audit records.

### Accessibility

Use semantic tables, properly labelled filters, keyboard navigation, visible focus indicators and accessible pagination.

Test with axe.

---

## 10. Exports — AC-12.6

Inspect the exact roadmap requirement for export formats and content.

Do not assume CSV, Excel or PDF without checking.

### Export Safety

- Apply the same authorisation and masking as the corresponding API.
- Never export raw audit rows directly.
- Validate date ranges and filters.
- Bound export size.
- Avoid loading unbounded records into memory.
- Use safe filenames.
- Prevent spreadsheet formula injection in CSV or spreadsheet exports.
- Preserve correct monetary formatting.
- Protect against accidental personal-data disclosure.
- Ensure export failures do not corrupt operational data.

Decide whether exports are generated server-side or client-side based on the actual requirements and volume.

Obtain approval before adding packages or new infrastructure.

---

## 11. Reporting Query Architecture

Inspect existing query services before creating new ones.

Prefer:

- Read-only queries.
- Parameterised SQL or EF Core queries.
- Appropriate indexes.
- Bounded date ranges.
- Stable pagination.
- Efficient aggregation.
- Clear financial definitions.
- No unnecessary personal data.

Consider PostgreSQL query plans and performance.

Avoid N+1 queries.

Do not introduce caching or materialised views unless justified by measured performance or requirements.

Do not use audit records as a substitute for authoritative payment or order state when calculating financial metrics.

---

## 12. Order Lifecycle Decision

M11 left **Collected status (B5)** unresolved.

Determine whether M12 requires collection or completion tracking.

If so, propose:

- State transitions.
- Manager permissions.
- Pickup-time validation.
- Effect on cancellation and refunds.
- Effect on reporting.
- Audit entries.
- Email notification implications.

Do not add a Collected status without stakeholder approval.

Preserve existing payment and refund state machines.

---

## 13. Privacy and POPIA

Q-14 retention remains unresolved.

The M11 report states that audit records and security events are currently retained indefinitely.

Do not invent a retention period.

The audit masking decision A4 remains provisional.

Review which fields are exposed to managers and exported.

Use data minimisation.

Preserve existing write-time audit masking.

Ensure reports do not expose:

- Passwords.
- Tokens.
- API keys.
- Card information.
- Raw webhook payloads.
- Full email bodies.
- Unnecessary personal information.

Document decisions requiring stakeholder approval.

---

## 14. Database Migrations

Prefer no migration if existing data and indexes support the reports.

If schema changes are required:

1. Identify the exact acceptance criterion.
2. Explain the proposed change.
3. Review backward compatibility.
4. Preserve immutable order and payment snapshots.
5. Generate and inspect migration SQL.
6. Test against disposable PostgreSQL.
7. Obtain approval before applying to Supabase.

Never rewrite existing financial history to simplify reporting.

---

## 15. Testing Requirements

Preserve the 1,223-test baseline.

### Backend Tests

Test:

- Pagination and filters.
- Stable sorting.
- Patron ownership.
- Manager authorisation.
- Historical accuracy.
- Reporting aggregations.
- Payment and refund accounting rules.
- Cancellation handling.
- Date boundaries and time zones.
- Empty result sets.
- Invalid input.
- Large datasets.
- Export correctness.
- Export injection protection.
- Audit masking.

### Frontend Tests

Use Vitest for:

- My Orders pagination.
- History filters.
- Report rendering.
- Empty states.
- Loading and error states.
- Audit filters.
- Audit pagination.
- Export controls.

### Integration Tests

Use Testcontainers PostgreSQL 17 for:

- Actual SQL query behaviour.
- Aggregation accuracy.
- Database indexes where relevant.
- Audit query integration.
- Cross-Patron access prevention.
- Financial consistency.

### End-to-End Tests

Use Playwright and axe for:

- Patron history journey.
- Manager reporting journey.
- Audit screen.
- Lookup-change report.
- Relevant export workflows.

Use simulated Paystack responses rather than requiring a live payment checkout.

### Regression and Security

Run:

- All backend tests.
- Vitest.
- Playwright.
- axe.
- TypeScript build.
- oxlint.
- .NET build with warnings as errors.
- Dependency vulnerability checks.
- Secret scans.

Report exact counts and failures.

---

## 16. Incremental Implementation Plan

**Stop after each stage and wait for my approval.**

### Step 1 — Requirements and Existing Code Review

Read the authoritative M12 requirements.

Inspect existing APIs, frontend pages, database models, audit queries and tests.

Produce a requirements traceability matrix and gap analysis.

**Do not modify code.**

### Step 2 — Reporting Definitions and Scope Approval

Present:

- Exact reports required.
- Proposed data sources.
- Financial definitions.
- Date and time-zone rules.
- Filtering and pagination rules.
- Export requirements.
- Security implications.
- Open decisions.

Obtain approval.

### Step 3 — Patron Order History

Implement the missing AC-12.1 functionality.

Add tests.

### Step 4 — Reporting Backend

Implement the approved management reporting queries and endpoints.

Add aggregation and financial correctness tests.

### Step 5 — Reporting Frontend

Implement management report screens.

Add Vitest and accessibility tests.

### Step 6 — Lookup-Change Report

Reuse the audit API to implement AC-12.4.

Test masking and filters.

### Step 7 — Management Audit Interface

Implement AC-12.5 using the M11 audit APIs.

Test permissions, pagination and accessibility.

### Step 8 — Exports

Implement AC-12.6 according to the approved formats and rules.

Test correctness, bounds and injection protection.

### Step 9 — Full Regression and Security Verification

Run all relevant automated tests, builds, lint and security checks.

Investigate failures before proceeding.

### Step 10 — Documentation and Completion

Update:

- README.
- Reporting architecture documentation.
- Financial metric definitions.
- M12 requirements traceability.
- Decision register.
- Relevant API documentation.
- M12 progress report.
- M13 integration and testing notes.

---

## 17. Definition of Done

M12 is complete only when:

- [ ] Exact M12 acceptance criteria are verified.
- [ ] Patron history meets the roadmap requirements.
- [ ] Management reports use approved financial definitions.
- [ ] Report calculations match stored financial data.
- [ ] Lookup changes are reportable.
- [ ] Managers can search required audit history.
- [ ] Reports and exports enforce appropriate authorisation.
- [ ] Personal information is masked appropriately.
- [ ] Export formats and safety requirements are satisfied.
- [ ] Pagination and filtering are bounded.
- [ ] Relevant performance targets are met.
- [ ] Existing functionality remains intact.
- [ ] Existing tests pass.
- [ ] New tests pass.
- [ ] Accessibility checks pass.
- [ ] Security checks pass.
- [ ] Documentation is updated.
- [ ] Open stakeholder decisions are recorded.

Do not mark anything complete without evidence.

---

## 18. Your First Response

Start with **Step 1 — Requirements and Existing Code Review**.

Read the roadmap's exact M12 section and inspect the existing repository.

Report:

1. Exact AC-12.x acceptance criteria and T12.x tasks.
2. Existing features that already satisfy them.
3. Missing functionality.
4. Proposed architecture.
5. Financial reporting questions.
6. Export requirements.
7. Security and privacy risks.
8. Relevant existing file paths.
9. The smallest safe implementation sequence.

**Do not write code or migrations yet. Wait for my approval.**