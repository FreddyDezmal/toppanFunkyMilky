# Milestone 13 — End-to-End Integration, Security and Reliability Testing

## Your Role

Act as my Senior Software Architect, ASP.NET Core Integration Engineer, Application Security Engineer, QA Automation Engineer, PostgreSQL Database Specialist, Payment Systems Engineer and Technical Mentor.

We are continuing development of **Order Milkshake for Pickup**, a demonstration application for **Milky Shaky Custom Drinks**.

I am the sole developer, working on Windows 11 using Visual Studio Code and PowerShell.

Your responsibility is to help me complete **Milestone 13**, following the actual Development Roadmap and User Requirement Specification.

This is an integration, security, reliability and regression-testing milestone.

**Do not start coding immediately. First inspect the repository, authoritative requirements, existing tests and progress reports. Identify the gaps and obtain my approval.**

Work incrementally. Explain each proposed change and stop for approval before proceeding to the next stage.

---

# 1. Technology Stack

## Backend

- ASP.NET Core 10 Web API
- C#
- Entity Framework Core 10
- Npgsql
- PostgreSQL 17 through Testcontainers
- Supabase PostgreSQL for the demonstration environment
- Database schema `milkshake`
- ASP.NET Core Identity
- Layered architecture:
  - Milkshake.Domain
  - Milkshake.Application
  - Milkshake.Infrastructure
  - Milkshake.Api

## Frontend

- React 19
- TypeScript
- Vite 8
- Tailwind CSS v4

## Testing

- xUnit
- Testcontainers PostgreSQL 17
- Vitest
- Playwright
- axe accessibility testing

## External Integrations

- Paystack hosted checkout, permanently in test mode
- Paystack transaction verification and webhooks
- Paystack refunds
- SMTP/Mailpit for development email capture
- Resend for approved email delivery tests

## Security Architecture

- ASP.NET Core Identity
- HTTP-only, Secure, SameSite=Strict session cookies
- Global CSRF protection for state-changing authenticated requests
- PatronOnly and ManagerOnly authorisation
- Default-deny authorisation
- Rate limiting
- Strict JSON and request validation
- Safe ProblemDetails responses
- Audit logging
- Server-side secret management

Preserve these architectural and security decisions.

---

# 2. Current Milestone Status

According to the latest progress reports:

- M1 — Foundation complete
- M4 — Authentication and security complete
- M5 — Ordering complete
- M6 — Core pricing complete, with stakeholder-dependent rules outstanding
- M7 — Order persistence complete
- M8 — Paystack integration complete
- M8b — Cancellations and refunds complete
- M9 — Email notifications complete
- M10 — Manager configuration complete
- M11 — Audit integrity complete
- M12 — Management reporting built and tested, pending stakeholder sign-off

Some M2/M3 formal sign-offs remain unresolved.

M14 — User Acceptance Testing and Deployment is still ahead.

M15 — Optional mobile application is outside the current scope.

---

# 3. Authoritative Documents

Before implementing anything, read:

1. User Requirement Specification v1.1.
2. Development Roadmap, especially the exact M13 section.
3. M4 Authentication and Security report.
4. M5 Ordering report.
5. M6 Pricing report.
6. M7 Order Persistence report.
7. M8 Payment Integration report.
8. M8b Cancellation and Refunds report.
9. M9 Email Notifications report.
10. M10 Manager Configuration report.
11. M11 Audit Integrity report.
12. M12 Management Reporting report.
13. Existing security and architecture documentation.
14. Relevant stakeholder decision registers.

Extract the exact M13 acceptance criteria and tasks.

**Do not invent AC-13.x or T13.x requirements.**

If the authoritative roadmap or URS is unavailable, request the missing sections before finalising scope.

Create a traceability matrix with:

- Requirement identifier.
- Exact requirement.
- Existing implementation.
- Existing test evidence.
- Missing test or implementation work.
- Risk.
- Proposed verification.
- Final result.

Do not assume that a feature is integrated simply because its unit tests pass.

---

# 4. Existing Test Baseline

The M12 progress report dated 21 November 2026 records:

| Suite | Passing |
|---|---:|
| Backend unit and integration | 1,176 |
| Vitest | 205 |
| Playwright | 9 |
| **Total** | **1,390** |

Build, lint and dependency audits also passed according to that report.

This is the starting baseline.

At the beginning of M13:

1. Execute the current test suites.
2. Record the actual results.
3. Identify failures, skips and flaky tests.
4. Confirm the development environment.
5. Preserve logs.

Do not claim the baseline has been independently reproduced until the commands have actually run.

---

# 5. Primary Objective

Prove that the complete application works correctly across component boundaries.

Prioritise:

1. Authentication and authorisation.
2. Patron registration and login.
3. Milkshake configuration.
4. Server-authoritative pricing.
5. Order persistence and idempotency.
6. Paystack payment processing.
7. Payment webhook verification.
8. Refund and cancellation workflows.
9. Email notifications and receipts.
10. Audit trail completeness.
11. Manager configuration.
12. Patron history and reporting.
13. Database consistency.
14. Security boundaries.
15. Performance and resilience.

Use the roadmap's exact scope to determine the required acceptance tests.

Avoid unrelated feature development.

---

# 6. End-to-End Patron Journey

Verify the full Patron journey using Playwright and the real application architecture, with controlled external-provider simulations.

Suggested journey:

1. Register a Patron.
2. Authenticate.
3. Retrieve available milkshake options.
4. Select a flavour.
5. Select a consistency.
6. Select a topping.
7. Configure quantity.
8. Select a restaurant.
9. Select a valid pickup time.
10. Review the order.
11. Obtain a server-authoritative quote.
12. Place the order.
13. Initiate payment.
14. Simulate verified successful payment.
15. Verify order confirmation.
16. Verify email outbox creation.
17. Verify receipt delivery through Mailpit.
18. View the order in My Orders.
19. Open the order details.
20. Verify immutable historical prices.

Test both normal and failure paths.

Do not bypass application authorisation or directly manipulate business state merely to make the UI test pass.

Use approved test helpers and controlled provider fakes.

---

# 7. Authentication and Session Security

Test:

- Registration validation.
- Login success.
- Invalid credentials.
- Account lockout.
- Session expiry.
- Logout.
- Anonymous access.
- PatronOnly endpoints.
- ManagerOnly endpoints.
- CSRF enforcement.
- Cookie security attributes.
- Same-origin API access.
- Rate limits.
- Concurrent sessions where relevant.

### Cross-User Isolation

Verify that Patron A cannot:

- Read Patron B's order.
- Cancel Patron B's order.
- Access Patron B's payment information.
- Access Patron B's refund information.
- Reuse Patron B's order-history cursor.
- Access manager reports.
- Access audit history.

Use real HTTP requests against the application test host.

Do not rely only on mocked frontend tests.

---

# 8. Pricing Integration

Verify the complete flow:

Draft → Validation → Quote → Order Placement → Stored Snapshot → Payment.

Check that:

- The server calculates all prices.
- The browser cannot submit an authoritative amount.
- Prices are selected from the correct effective-dated records.
- Historical orders preserve original prices.
- VAT and discounts are applied according to current approved rules.
- Rounding is consistent.
- Payment amount matches the stored order amount.
- A changed catalogue price does not alter a placed order.

### Outstanding Decisions

Discount eligibility and certain VAT/pricing assumptions remain stakeholder-dependent.

Do not silently activate discount rules.

Keep `DiscountPolicy.RulesConfirmed=false` unless approved.

Do not mark stakeholder-dependent pricing acceptance criteria complete without sign-off.

---

# 9. Order Placement and Idempotency

Test:

- Valid order creation.
- Invalid draft rejection.
- Inactive catalogue options.
- Invalid pickup times.
- Quantity limits.
- Duplicate submission.
- Same idempotency key with identical request.
- Same key with different request.
- Concurrent requests using the same key.
- Order-reference uniqueness.
- Transaction rollback.
- Audit persistence.

Verify that an order and its immutable item snapshots are saved atomically.

No duplicate order should be created by a retry with the same valid idempotency key and request.

---

# 10. Paystack Payment Integration

Paystack remains in **test mode only**.

Never introduce live payment processing.

Test:

- Checkout initiation.
- Server-calculated amount in cents.
- Currency ZAR.
- Provider reference persistence.
- Safe checkout redirect.
- Payment success.
- Payment failure.
- Payment verification.
- Duplicate webhook delivery.
- Out-of-order events.
- Browser return before webhook.
- Webhook before browser return.
- Concurrent verification.
- Reconciliation retries.
- Wrong-amount payments.
- Late payments.
- Duplicate successful payments.
- Provider timeouts.

### Security

Verify:

- HMAC-SHA512 signature validation.
- Raw request-body verification.
- Constant-time signature comparison.
- Invalid signature rejection.
- No trust in browser-reported payment success.
- No provider secrets in API responses.
- No client-controlled payment amount.
- No unapproved redirect domains.

### Critical Invariant

**An order must never become Confirmed solely because the browser reports payment success.**

Confirmation requires the existing server-side verification rules.

---

# 11. Cancellation and Refund Integration

Test:

- Unpaid order cancellation.
- Paid order cancellation.
- Cancellation before pickup.
- Cancellation at or after pickup.
- Configurable cancellation fee.
- Correct VAT-exclusive fee basis.
- Correct refund calculation.
- Manager cancellation request.
- Patron acceptance.
- Patron rejection.
- Request expiry.
- Refund submission.
- Refund webhook processing.
- Refund reconciliation.
- Refund success.
- Refund failure.
- Duplicate refund events.
- Late payment after cancellation.
- Wrong-amount payment requiring attention.

### Financial Integrity

Verify:

- No double refund.
- No incorrect order-state transition.
- No refund greater than the eligible paid amount.
- Correct refund state persistence.
- Accurate audit records.
- Consistent patron and manager views.

Do not change cancellation fee rules without approval.

---

# 12. Email Integration

M9 introduced a transactional outbox and five notification types:

1. Order confirmation and payment receipt.
2. Manager cancellation request.
3. Order cancelled.
4. Refund processed.
5. Refund failed.

Test that:

- Notifications are created in the same transaction as the relevant business state change.
- Payment success generates only the appropriate confirmation notification.
- Failed payments do not generate successful-payment receipts.
- Duplicate webhooks do not create duplicate logical emails.
- Email provider failures do not roll back successful payments.
- The worker retries failed deliveries.
- Concurrent workers do not claim the same email simultaneously.
- Sensitive values are not included in templates.
- HTML content is encoded.
- Email links are correctly formed.
- Allow-list restrictions are respected.

Use Mailpit for automated delivery tests.

Do not send emails to real recipients during automated test runs.

Any real-inbox test requires my explicit approval.

---

# 13. Audit Integrity

M11 introduced append-only audit protection and actor attribution.

Verify:

- Order changes are audited.
- Payment changes are audited.
- Refund changes are audited.
- Email delivery changes are audited.
- Manager configuration changes are audited.
- System processes have explicit actors.
- Audit entries are transactionally consistent.
- Failed business transactions do not create misleading audit entries.
- Audit history cannot be modified through normal application access.
- Manager audit queries enforce permissions.
- Personal data is masked according to existing rules.

Preserve M11 coverage and bypass-detection tests.

Do not weaken database triggers or audit protections to make tests pass.

---

# 14. Reporting Integration

M12 introduced:

- Full Patron order history.
- Manager Orders report.
- Weekly, monthly and growth trends.
- Lookup-change report.
- Audit report.
- CSV exports.

Verify reporting consistency after:

- Order creation.
- Payment success.
- Payment failure.
- Cancellation.
- Refund submission.
- Refund completion.
- Catalogue changes.
- Historical price changes.

### Reconciliation

Compare reports with authoritative database queries.

Ensure that:

- Unpaid orders are not incorrectly reported as paid.
- Cancelled orders are labelled correctly.
- Refund outcomes are reflected accurately.
- Historical amounts remain unchanged.
- Pagination does not skip or duplicate records.
- Exports match the filtered report.
- Audit masking is preserved.

Preserve the M12 reconciliation tests.

---

# 15. Database and Transaction Integrity

Use disposable PostgreSQL 17 through Testcontainers.

Test:

- Foreign-key constraints.
- Unique constraints.
- Concurrent writes.
- Transaction rollback.
- EF Core SaveChanges behaviour.
- Audit interceptor execution.
- Notification interceptor execution.
- Database migrations.
- Idempotency constraints.
- Payment-event uniqueness.
- Refund-event uniqueness.
- Append-only audit triggers.

Check whether existing migrations can build a fresh database from zero.

Do not run destructive tests against Supabase.

Do not reset or drop the existing demonstration database.

Any Supabase migration requires separate approval.

---

# 16. Security Testing

Perform a structured security review.

## Authentication and Authorisation

- Broken access control.
- Horizontal privilege escalation.
- Vertical privilege escalation.
- Session handling.
- CSRF.
- Rate limiting.
- Secure cookie settings.

## Input Validation

- SQL injection.
- HTML injection.
- Stored XSS.
- Reflected XSS.
- CSV formula injection.
- Malformed JSON.
- Unexpected fields.
- Excessive payloads.
- Invalid dates.
- Invalid numeric values.

## Payment and Webhook Security

- Forged signatures.
- Replay attacks.
- Duplicate events.
- Wrong amounts.
- Currency mismatches.
- Untrusted provider status.
- Provider reference substitution.

## Sensitive Data

- Secret exposure.
- Personal information leakage.
- Unsafe logging.
- Excessive error details.
- Audit data exposure.
- Email payload exposure.
- Export privacy.

## Dependencies

Run the existing .NET and npm vulnerability checks.

Do not introduce new dependencies without approval.

Document findings with severity, reproduction steps, mitigation and retest results.

---

# 17. Reliability and Failure Injection

Where supported by the existing architecture, simulate:

- Database connection failures.
- Paystack timeouts.
- Duplicate webhook deliveries.
- Email provider unavailability.
- Background worker interruption.
- Concurrent requests.
- Retry after network failure.
- Refund-provider failures.

Verify that:

- Transactions remain consistent.
- Retries do not duplicate financial operations.
- Errors are safe and actionable.
- Background reconciliation recovers where designed.
- Failed email delivery does not reverse payment.
- Audit records remain accurate.

Do not claim recovery from a failure mode that has not been tested.

---

# 18. Performance Testing

Extract exact performance targets from the roadmap and URS.

Existing evidence includes:

- M11 audit searches under three seconds over 100,000 records.
- M12 reporting under three seconds over 50,000 orders.

Preserve these tests.

Investigate:

- Slow queries.
- N+1 query patterns.
- Unbounded result sets.
- Missing indexes.
- Excessive database round trips.
- Inefficient background worker queries.
- Large exports.
- Concurrent report requests.

Use repeatable measurements.

Record:

- Dataset size.
- Test environment.
- Query or operation.
- Execution time.
- Threshold.
- Result.

Do not extrapolate local Testcontainers performance into a production guarantee.

---

# 19. Accessibility and Browser Compatibility

Test core journeys using Playwright and axe.

Check:

- Keyboard navigation.
- Visible focus.
- Form labels.
- Error messages.
- Status announcements.
- Modal interactions.
- Responsive layouts.
- Accessible tables.
- Loading states.
- Empty states.

Review browser coverage required by the roadmap.

Do not assume testing only Chromium satisfies all browser requirements.

---

# 20. Investigate Existing Intermittent Failures

The M12 progress report recorded two intermittent backend test issues:

1. An `OrderPlacementTests` retry test once failed with HTTP 400 for both requests, then passed on reruns.
2. An integration test failed once during the Step 8 run; its name was not captured, and the next full run passed.

These must not be silently ignored.

At the start of M13:

- Locate the relevant tests.
- Review shared fixtures and test isolation.
- Examine parallel execution.
- Check clock handling.
- Check database state and transaction boundaries.
- Check rate-limit interactions.
- Check idempotency keys.
- Check test-host configuration.
- Check Docker/Testcontainers resource contention.

Run focused repeated tests to determine whether the failures reproduce.

Capture diagnostic logs.

Do not weaken assertions, introduce arbitrary delays or simply increase timeouts without identifying the cause.

If the failures cannot be reproduced, record the investigation and residual risk.

---

# 21. Security and Quality Gates

Before declaring M13 complete, execute the applicable commands.

Examples:

```powershell
dotnet build Milkshake.slnx -warnaserror
dotnet test Milkshake.slnx

Set-Location frontend
npm run lint
npm run build
npm test
npx playwright test
npm audit
```

Use the actual repository paths and scripts.

Also run:

- .NET dependency vulnerability checks.
- Secret scans.
- Migration validation.
- Integration test suite.
- Security regression tests.
- Performance tests.

Report the actual results, not expected results.

---

# 22. Incremental Implementation Plan

**Stop after each stage and wait for my approval.**

## Step 1 — Requirements and Baseline Audit

Read the authoritative documents.

Inspect the repository and test configuration.

Run the baseline tests.

Produce:

- Exact M13 acceptance criteria.
- Requirements traceability matrix.
- Current test inventory.
- Integration coverage gaps.
- Security gaps.
- Flaky-test findings.
- Proposed implementation sequence.

Do not modify code.

## Step 2 — Test Architecture Approval

Present the proposed integration-test structure.

Explain:

- Test fixtures.
- Disposable database setup.
- Provider fakes.
- Mailpit integration.
- Playwright configuration.
- Security testing approach.
- Performance testing approach.

Obtain approval.

## Step 3 — Patron Journey Integration

Implement missing end-to-end tests.

Fix confirmed defects only with approval.

## Step 4 — Payment and Refund Integration

Add missing payment, webhook, cancellation and refund scenarios.

Verify idempotency and transactional consistency.

## Step 5 — Email, Audit and Reporting Integration

Test the complete chain from verified payment through email, audit and reporting.

## Step 6 — Security Testing

Perform and document the required security tests.

Address confirmed vulnerabilities.

## Step 7 — Reliability and Performance

Run failure-injection, concurrency and performance tests.

Investigate intermittent failures.

## Step 8 — Accessibility and Browser Testing

Run Playwright and axe.

Verify required browser coverage.

## Step 9 — Full Regression

Run the complete test suite.

Verify builds, lint, dependencies, migrations and security checks.

## Step 10 — Documentation and Sign-Off

Prepare:

- M13 progress report.
- Requirements traceability.
- Security test report.
- Integration test report.
- Performance results.
- Known issues and residual risks.
- M14 UAT readiness checklist.
- Updated decision register.
- Git commits.

---

# 23. Definition of Done

M13 is complete only when:

- [ ] Exact roadmap acceptance criteria are traced to evidence.
- [ ] Baseline test results are reproduced or discrepancies explained.
- [ ] Critical Patron journeys are covered.
- [ ] Payment verification cannot be bypassed.
- [ ] Duplicate payment events are handled safely.
- [ ] Refund workflows are verified.
- [ ] Email failures do not corrupt business transactions.
- [ ] Audit integrity is preserved.
- [ ] Reporting reconciles with stored data.
- [ ] Cross-Patron isolation is verified.
- [ ] Manager permissions are verified.
- [ ] CSRF and rate limiting are tested.
- [ ] Input validation and injection protections are tested.
- [ ] Relevant failure modes are tested.
- [ ] Database migrations work on a fresh database.
- [ ] Required performance targets are met.
- [ ] Required accessibility checks pass.
- [ ] Browser coverage matches the roadmap.
- [ ] Intermittent failures are investigated.
- [ ] All applicable regression tests pass.
- [ ] Security findings are documented and retested.
- [ ] Outstanding stakeholder decisions remain visible.
- [ ] M14 UAT readiness evidence is prepared.

Do not mark an item complete without evidence.

---

# 24. Your First Response

Begin with **Step 1 — Requirements and Baseline Audit**.

Read the actual M13 roadmap and URS requirements.

Inspect the repository, existing tests and recent milestone reports.

Run the current test baseline.

Then provide:

1. Exact M13 acceptance criteria and tasks.
2. A requirement-to-test traceability matrix.
3. Existing integration coverage.
4. Missing scenarios.
5. Security risks.
6. Intermittent-test investigation plan.
7. Proposed test architecture.
8. Expected files to add or modify.
9. Any decisions requiring my approval.
10. The smallest safe implementation sequence.

**Do not write code, create migrations or change configuration yet. Wait for my approval.**