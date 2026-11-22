# Milestone 11 — Order History and Audit

## Your Role

Act as my **Senior ASP.NET Core Architect, PostgreSQL Database Architect, Full-Stack Engineer, Application Security Engineer, QA Engineer, and Technical Mentor**.

We are continuing development of **Order Milkshake for Pickup**, a demonstration application for **Milky Shaky Custom Drinks**.

I am the sole developer, using Windows 11, Visual Studio Code and PowerShell.

Your task is to help me complete **Milestone 11 — Order History and Audit**, using the existing repository, established architecture and authoritative project requirements.

**Important: M11 is partially implemented already. Do not rebuild existing features. First determine precisely what remains unfinished.**

Work incrementally. Review the existing code, explain your proposed changes, obtain approval and implement in small, testable steps.

---

# 1. Project Technology Stack

### Backend

- ASP.NET Core 10 Web API
- C#
- Entity Framework Core 10
- Npgsql
- Supabase PostgreSQL
- Database schema: `milkshake`
- ASP.NET Core Identity
- Layered architecture:
  - `Milkshake.Domain`
  - `Milkshake.Application`
  - `Milkshake.Infrastructure`
  - `Milkshake.Api`

### Frontend

- React 19
- TypeScript
- Vite 8
- Tailwind CSS v4

### Testing

- xUnit
- Testcontainers PostgreSQL 17
- Vitest
- Playwright
- axe accessibility testing

### Security

- Secure HTTP-only authentication cookies
- PatronOnly and ManagerOnly authorisation
- CSRF protection
- Rate limiting
- Strict request validation
- Safe ProblemDetails responses
- Existing audit interceptor
- Server-side secrets management

Preserve these conventions.

---

# 2. Current Project Status

The following milestones are complete according to their progress reports:

- M1 — Project foundation
- M4 — Authentication and security
- M5 — Milkshake ordering
- M6 — Core pricing
- M7 — Order placement and transactions
- M8 — Paystack payment gateway
- M8b — Cancellation and refunds
- M9 — Email notifications and receipts
- M10 — Manager configuration and lookups

Some M2/M3 formal sign-offs and M6 stakeholder-dependent requirements remain unresolved.

### Latest Test Baseline

The M9 progress report dated 20 November 2026 records:

- **944 backend tests passing**
- **164 Vitest tests passing**
- **9 Playwright tests passing**

**Total: 1,117 automated tests passing.**

Preserve this baseline and report exact results after changes.

Do not claim tests passed unless they were executed.

---

# 3. Authoritative Documents

Before implementing anything, review:

1. User Requirement Specification v1.1.
2. Development Roadmap — Milestone 11.
3. M7 Order Placement Progress Report.
4. M8 Payment Gateway Progress Report.
5. M8b Cancellation and Refunds Progress Report.
6. M9 Email Notifications Progress Report.
7. Existing audit architecture and decision documentation.
8. Relevant stakeholder decision registers.

Extract the **exact M11 acceptance criteria and test requirements**.

Do not invent acceptance criteria from general assumptions.

If the roadmap or URS is unavailable, request the relevant sections.

Create a requirements traceability table showing:

| Requirement | Existing implementation | Missing work | Planned tests |
|---|---|---|---|
| Exact roadmap requirement | Evidence from repository | Gap, if any | Test coverage |

Do not mark something complete merely because a similar feature exists.

---

# 4. Existing Functionality to Inspect

## 4.1 Patron Order History

M8b introduced:

- A **My Orders** page at `/orders`.
- Navigation back to individual orders.
- Patron access to their own orders.
- Cancellation access from order details.

M7 introduced:

- `GET /api/ordering/orders`
- `GET /api/ordering/orders/{reference}`

The original M7 order list returned the 20 newest orders.

Inspect whether this has changed.

Determine whether M11 requires:

- Full historical access.
- Pagination.
- Sorting.
- Filtering.
- Order status display.
- Pickup date and restaurant information.
- Payment and refund information.
- Historical pricing.
- Order detail access.

Only implement these if supported by the authoritative requirements or explicitly approved.

## 4.2 Manager Order Access

M8b introduced:

- Management → Orders.
- Manager order details.
- Manager cancellation requests.
- Customer contact details for approved workflows.

Inspect existing manager APIs and frontend pages.

Determine whether M11 requires additional manager access to order history or audit records.

Avoid duplicating functionality planned for M12 reporting.

## 4.3 Existing Audit Logging

M1 introduced a field-level audit interceptor.

M7 added order and order-item auditing.

M8 added payment and payment-event records.

M8b added cancellation and refund auditing.

M9 added email notification auditing with masking of recipient information and email payloads.

Inspect the actual audit implementation, including:

- Audit entity.
- Database mapping.
- SaveChanges interceptors.
- Transaction boundaries.
- Actor identification.
- Timestamp handling.
- Before and after values.
- Sensitive-field masking.
- Failure handling.
- Audit queries and access restrictions.

Determine which M11 requirements are already satisfied.

---

# 5. Primary Objective

Complete the roadmap-defined M11 functionality for:

1. Patron order history.
2. Historical order details.
3. Appropriate manager history access.
4. Field-level audit completeness.
5. Audit integrity and access control.
6. Historical financial accuracy.
7. Relevant frontend interfaces.
8. Automated test coverage.

Do not expand scope into a general reporting system.

M12 is responsible for management reporting unless the roadmap explicitly assigns a feature to M11.

---

# 6. Historical Order Accuracy

Historical orders must reflect what was actually purchased.

Do not calculate old order totals using current menu prices, VAT settings or discount tiers.

Use immutable snapshots stored when the order was placed.

Verify that order history correctly displays:

- Order reference.
- Creation timestamp.
- Restaurant and pickup details.
- Each configured drink.
- Quantity.
- Flavour.
- Consistency.
- Topping.
- Stored item prices.
- Subtotal.
- Discount.
- VAT.
- Final total.
- Currency.
- Order status.

Display payment and refund information from their persisted records where required.

### Historical Changes

Test that:

- Renaming a flavour does not change the historical order's flavour name.
- Deactivating a topping does not remove it from previous orders.
- Changing a restaurant does not incorrectly rewrite historical pickup details.
- Changing VAT or prices does not recalculate old receipts.
- Changing cancellation fees does not alter stored refund amounts.

If the existing schema does not snapshot a required value, identify the gap before proposing a migration.

Do not silently fabricate historical values.

---

# 7. Order Status and Lifecycle

Inspect the existing order state machine.

M7 originally defined:

- PendingPayment
- Confirmed
- PaymentFailed
- Cancelled

M8 and M8b extended payment and cancellation handling.

Do not introduce new statuses without checking the roadmap and decision register.

In particular, investigate whether M11 requires:

- Completed
- Collected
- ReadyForPickup
- Expired

These statuses were previously unresolved.

If new statuses are required, first propose:

- Allowed transitions.
- Who may trigger them.
- Whether they are reversible.
- Whether they affect payments or refunds.
- Whether they trigger email notifications.
- Whether they require new audit entries.
- How existing orders are handled.

Obtain approval before changing the lifecycle.

---

# 8. Audit Requirements

## 8.1 Audit Coverage

Review whether all relevant operations are audited, including:

- Order creation.
- Order status transitions.
- Payment verification outcomes.
- Refund status transitions.
- Cancellation requests.
- Cancellation acceptance and rejection.
- Manager configuration changes.
- Price changes.
- Email notification status changes.
- Manager resend actions.

Determine which operations are required by M11 and which are already covered.

Avoid duplicate audit entries.

## 8.2 Field-Level Change History

Where required, audit entries should identify:

- Entity type.
- Entity reference or identifier.
- Operation.
- Changed field.
- Previous value.
- New value.
- Actor.
- Timestamp.

Use the existing schema and conventions where possible.

Do not invent previous values for records that were never audited.

## 8.3 Audit Transaction Integrity

Where audit records must be transactional:

- Business changes and their audit records should commit together.
- Failed transactions should not leave misleading successful-change audit entries.
- Concurrent updates must not corrupt history.
- Audit failures must follow the existing documented failure policy.

Inspect how the existing interceptor handles these cases.

## 8.4 Actor Attribution

Distinguish actions performed by:

- Patrons.
- Managers.
- Background reconciliation workers.
- Verified Paystack webhook processing.
- Email dispatcher.
- System maintenance processes.

Use explicit, reliable actor identification.

Do not attribute a background action to the currently signed-in browser user.

## 8.5 Sensitive Information

Never expose:

- Password hashes.
- Authentication tokens.
- API keys.
- Connection strings.
- Payment credentials.
- Card information.
- Raw sensitive webhook payloads.
- Full email bodies.
- Unnecessary personal data.

Verify that audit masking covers newly introduced M8, M8b and M9 entities.

Respect unresolved POPIA retention question Q-14.

Do not invent a retention or anonymisation period.

---

# 9. Audit Read Access

Inspect the roadmap to determine who is authorised to view audit history.

Potential access models include:

- Manager-only audit history.
- Entity-specific audit history.
- Patron-visible order status history.
- Internal-only audit records.

Do not assume Patrons should see raw internal audit entries.

If manager audit APIs are required:

- Enforce ManagerOnly.
- Validate filters.
- Apply pagination and bounded page sizes.
- Avoid unbounded queries.
- Prevent sensitive-field disclosure.
- Apply rate limiting where appropriate.
- Return safe errors.
- Use stable sorting.
- Add appropriate database indexes.

Do not expose raw audit JSON without reviewing its contents.

---

# 10. Order History API Design

Reuse existing endpoints where appropriate.

Prefer extending existing services over creating duplicate APIs.

If pagination is required, propose a stable pagination strategy.

Consider:

- Page size limits.
- Stable ordering.
- Created timestamp and unique identifier tie-breakers.
- Keyset versus offset pagination.
- Filtering by status.
- Filtering by date range.
- Ownership enforcement.
- Database indexes.

Do not allow arbitrary user IDs in Patron order-history requests.

Derive Patron identity from the authenticated session.

Another Patron's order must remain inaccessible.

---

# 11. Frontend Requirements

Inspect the existing React pages before changing them.

## Patron My Orders

Preserve:

- Existing navigation.
- Order detail links.
- Cancellation controls.
- Existing payment states.
- Responsive layout.
- Accessibility.

Implement only missing roadmap requirements.

Potential improvements, if required:

- Pagination or Load More.
- Status filters.
- Clear payment/refund state labels.
- Pickup details.
- Empty state.
- Loading and error states.
- Historical totals.

## Manager Views

Inspect existing Management → Orders and Management → Emails pages.

If M11 requires an audit interface, propose a manager-only page consistent with existing styling.

Avoid displaying unnecessary personal information.

### Accessibility

Maintain:

- Semantic headings.
- Keyboard navigation.
- Visible focus indicators.
- Accessible table labels.
- Proper status announcements.
- Clear error messages.
- Responsive layouts.

Run axe checks for relevant new or modified pages.

---

# 12. Database Changes

Prefer **no migration** if the existing schema already supports M11.

If a migration is necessary:

1. Explain the exact requirement it satisfies.
2. Propose the schema change.
3. Assess compatibility with existing data.
4. Preserve historical financial records.
5. Review generated SQL.
6. Test against disposable PostgreSQL.
7. Obtain approval before applying to Supabase.

Never destructively rewrite financial snapshots or audit history.

Follow existing `milkshake` schema and naming conventions.

---

# 13. Testing Strategy

Preserve the **1,117-test baseline**.

## Domain Tests

Test any new order lifecycle or history logic.

Verify historical data remains unchanged after configuration changes.

## Integration Tests

Using Testcontainers PostgreSQL 17, test:

- Patron order ownership.
- Manager authorisation.
- Historical snapshot accuracy.
- Pagination boundaries.
- Filtering and sorting.
- Audit persistence.
- Audit transaction rollback.
- Audit actor attribution.
- Sensitive-field masking.
- Concurrent updates.
- Relevant database constraints.

## API Security Tests

Test:

- Anonymous access.
- Patron-only restrictions.
- Manager-only restrictions.
- Cross-Patron access.
- Invalid filters.
- Excessive page sizes.
- Safe ProblemDetails.
- No personal-data leakage.
- Rate limiting where applicable.

## Frontend Tests

Use Vitest for:

- Order list rendering.
- Historical order details.
- Pagination.
- Filters.
- Empty states.
- Loading states.
- Error handling.
- Accessibility-related behaviour.

## End-to-End Tests

Use Playwright and axe.

Cover:

1. Patron places an order.
2. Patron pays using the existing simulated payment provider.
3. Patron sees the confirmed order in history.
4. Patron opens the historical order details.
5. Another Patron cannot access that order.
6. Manager views authorised history or audit information where required.

Do not rely on a live Paystack checkout for automated tests.

---

# 14. Incremental Implementation Plan

Follow this sequence.

**Stop after each step and wait for my approval.**

## Step 1 — Requirements and Existing Code Audit

Read the authoritative documents.

Inspect:

- Order history APIs.
- My Orders page.
- Manager Orders page.
- Domain order entity.
- Payment and refund entities.
- Audit interceptor.
- Audit mappings.
- Existing integration tests.
- Existing decision registers.

Create an exact M11 gap analysis.

Do not write code.

## Step 2 — Architecture and Scope Approval

Present:

- Requirements already met.
- Missing requirements.
- Proposed API changes.
- Proposed frontend changes.
- Audit coverage gaps.
- Any database changes.
- Open stakeholder decisions.

Obtain approval.

## Step 3 — Backend Order History

Implement only missing history functionality.

Add domain, application, infrastructure and API tests.

## Step 4 — Audit Completeness

Implement missing audit functionality.

Verify transactional integrity, actor attribution and masking.

## Step 5 — Manager Audit Access

Only if required by the roadmap.

Implement secure, bounded read APIs and appropriate manager interfaces.

## Step 6 — Frontend Order History

Extend existing My Orders and order detail pages.

Avoid rebuilding existing components.

## Step 7 — Lifecycle Changes

Only if required and approved.

Update domain transitions, payment/refund compatibility, persistence, notifications and tests.

## Step 8 — Security and Regression Testing

Run:

- Backend unit and integration tests.
- Vitest.
- Playwright.
- axe accessibility scans.
- TypeScript build.
- oxlint.
- .NET build with warnings as errors.
- Dependency vulnerability checks.
- Secret scans.

Report exact results.

## Step 9 — Documentation and Sign-Off

Update:

- README.
- Order history documentation.
- Audit architecture documentation.
- Requirements traceability.
- Decision register.
- M11 progress report.
- M12 integration notes.
- Git commit history.

---

# 15. Definition of Done

Milestone 11 is complete when:

- [ ] Exact M11 acceptance criteria are traced to evidence.
- [ ] Existing functionality has been reused.
- [ ] Patrons can view their required order history.
- [ ] Patrons cannot view other Patrons' orders.
- [ ] Historical order values remain accurate.
- [ ] Required order details are available.
- [ ] Required audit events are recorded.
- [ ] Field-level audit information is accurate.
- [ ] Audit actor attribution is correct.
- [ ] Audit records are protected against unauthorised access.
- [ ] Sensitive values are appropriately masked.
- [ ] Business transactions and audit entries remain consistent.
- [ ] Manager audit access is implemented if required.
- [ ] Any required lifecycle changes are approved and tested.
- [ ] Relevant frontend pages are accessible.
- [ ] Existing tests remain passing.
- [ ] New tests pass.
- [ ] Security checks pass.
- [ ] Documentation is complete.
- [ ] Remaining stakeholder decisions are recorded.

Do not mark any item complete without test or repository evidence.

---

# 16. Start Here

Begin with **Step 1 — Requirements and Existing Code Audit**.

Read the roadmap's exact M11 acceptance criteria and inspect the current repository.

Produce a structured gap analysis showing:

1. What is already complete.
2. What is partially complete.
3. What is missing.
4. Which existing files need modification.
5. Whether a database migration is needed.
6. Which security or audit risks exist.
7. Which stakeholder decisions remain open.
8. The smallest safe implementation sequence.

**Do not modify code yet. Wait for my approval before beginning Step 2.**