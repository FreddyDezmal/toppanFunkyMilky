# Milestone 8 — Payment Gateway Integration

## Your Role

Act as my **Senior ASP.NET Core Solutions Architect, Payment Integration Engineer, PostgreSQL Database Architect, Full-Stack Engineer, Application Security Engineer, and Technical Mentor**.

We are continuing development of **Order Milkshake for Pickup**, a production-quality application for **Milky Shaky Custom Drinks**.

I am the **sole developer**, working with:

- Windows 11.
- Visual Studio Code.
- PowerShell.
- .NET 10.
- ASP.NET Core Web API.
- EF Core 10.
- Supabase PostgreSQL.
- React 19.
- TypeScript.
- Vite.
- Tailwind CSS v4.

Guide me through implementing Milestone 8 incrementally, using my existing codebase.

For every step, provide:

1. The objective and architectural explanation.
2. The exact existing files to inspect.
3. Complete code for new or modified files.
4. Windows PowerShell commands.
5. Automated tests.
6. Manual verification steps.
7. Expected results.
8. Security, concurrency and financial-integrity considerations.

**Work one manageable step at a time and wait for my confirmation before continuing.**

Do not rebuild completed milestones.

---

# 1. Authoritative Project Documents

Use the existing:

- User Requirement Specification v1.1.
- Development Roadmap.
- Milestone 6 Progress Report.
- Milestone 7 Progress Report.

Verify the exact Milestone 8 acceptance criteria against the roadmap.

If the URS or roadmap is not available in the repository or current conversation, ask me to provide the relevant sections.

Do not invent requirements or claim a requirement is confirmed without checking its source.

Distinguish:

- Confirmed requirements.
- Approved developer decisions.
- Provisional technical choices.
- Unresolved stakeholder questions.
- Features deferred to later milestones.

---

# 2. Completed Milestones

## Milestone 1 — Foundation

The project has a working layered architecture, database integration, EF Core migrations, testing infrastructure and frontend.

## Milestone 4 — Authentication and Roles

Implemented:

- ASP.NET Core Identity.
- Secure HTTP-only session cookies.
- Patron and Manager roles.
- CSRF protection.
- Role-based authorisation.
- Rate limiting.
- Authentication security events.

## Milestone 5 — Milkshake Ordering

Implemented:

- Drink quantity selection.
- Individual milkshake customisation.
- Active catalogue choices.
- Restaurant selection.
- Pickup time validation.
- Server-side draft validation.

## Milestone 6 — Pricing

Implemented:

- Server-authoritative pricing.
- Effective-dated component fees.
- VAT calculation.
- Per-drink and order totals.
- Live customer price estimates.
- Discount calculation infrastructure.

The discount engine remains deliberately disabled because stakeholder rules are unresolved.

## Milestone 7 — Orders and Transactions

Implemented:

- Persistent orders and order items.
- Immutable pricing snapshots.
- Atomic database transactions.
- Public order references.
- Idempotent order placement.
- Price-change acknowledgement.
- Patron-only order retrieval.
- Order status lifecycle.
- Transactional audit logging.

### Existing Order Statuses

- `PendingPayment`
- `Confirmed`
- `PaymentFailed`
- `Cancelled`

The currently approved transitions are:

- `PendingPayment → Confirmed`
- `PendingPayment → PaymentFailed`
- `PendingPayment → Cancelled`
- `PaymentFailed → Confirmed`
- `PaymentFailed → Cancelled`

No other transitions are allowed unless the business rules are explicitly revised.

M7 does not expose an endpoint that marks an order as paid.

### Existing Order Data

Orders already contain:

- Internal order ID.
- Public order reference.
- Patron ID.
- Restaurant and pickup information.
- Currency.
- Immutable subtotal.
- Discount.
- VAT.
- Final payable total.
- Pricing timestamp.
- Order status.
- Idempotency key.
- Request fingerprint.

### Current Test Baseline

The M7 report records:

- **549 backend tests** passing.
- **110 frontend tests** passing.
- **9 Playwright end-to-end tests** passing.

Total: **668 automated tests**.

Preserve all existing tests.

Do not claim tests have passed without executing them.

---

# 3. Primary Objective

Implement a secure payment workflow that allows Patrons to pay for existing unpaid orders using an approved payment provider.

The system must:

1. Create payment attempts for existing orders.
2. Initiate a provider-hosted or otherwise approved secure checkout.
3. Charge exactly the order's stored total and currency.
4. Record provider transaction references.
5. Verify payment outcomes through trusted server-side mechanisms.
6. Prevent duplicate payment processing.
7. Handle payment success, failure, cancellation and retries.
8. Update order status only after verified payment outcomes.
9. Record payment activity and relevant audit events.
10. Display accurate payment status to the Patron.
11. Support recovery from lost responses, delayed notifications and duplicate webhooks.
12. Preserve all existing order and pricing integrity guarantees.

**Do not implement fake payment success or rely on browser redirects as proof of payment.**

---

# 4. Payment Provider Decision — Mandatory Before Coding

The payment provider has not yet been confirmed.

Before implementing provider-specific functionality, inspect the URS and roadmap for any specified provider or payment method.

Then present a concise comparison of suitable options for a South African business, such as:

- PayFast.
- Yoco.
- Peach Payments.
- Another provider explicitly required by the stakeholder.

Do not assume the business has an account with any provider.

Compare:

- South African rand support.
- Hosted checkout availability.
- Sandbox or test environment.
- API integration approach.
- Webhook or server notification support.
- Payment status verification.
- Idempotency capabilities.
- Refund support.
- Transaction fees, where verifiable.
- Developer integration complexity.
- Production onboarding requirements.

Use current official provider documentation for provider-specific claims.

**Ask me to select or confirm the provider before writing provider-specific integration code.**

Do not invent credentials, merchant identifiers, signing secrets, callback URLs or endpoint formats.

Do not select a provider solely because it appears easiest.

---

# 5. Payment Architecture

Design a payment integration architecture consistent with the existing layers.

## Domain

Owns:

- Payment state invariants.
- Valid payment transitions.
- Order-payment consistency rules.
- Amount and currency invariants.

Avoid provider SDK dependencies in the Domain layer.

## Application

Owns:

- Payment initiation use case.
- Payment verification workflow.
- Payment reconciliation workflow.
- Payment service interfaces.
- Idempotency contracts.
- Result types.

## Infrastructure

Owns:

- EF Core payment persistence.
- Provider API integration.
- Signature verification implementation.
- Provider-specific request and response mapping.
- Secure HTTP client configuration.
- External provider reference handling.

## API

Owns:

- Authentication and authorisation.
- Patron payment-initiation endpoints.
- Provider webhook endpoints.
- Input validation.
- HTTP status handling.
- ProblemDetails responses.

## Frontend

Owns:

- Initiating checkout.
- Redirecting to the provider.
- Displaying pending, failed and confirmed payment states.
- Polling or refreshing order status safely.
- Allowing permitted retries.

The frontend must never mark an order paid.

---

# 6. Payment Database Model

M7 deliberately left payment tables for M8.

Inspect existing entities and naming conventions before proposing a schema.

Consider a `payments` table with:

- Internal payment ID.
- Order ID.
- Provider identifier.
- Provider payment reference.
- Internal payment attempt reference.
- Idempotency key.
- Amount.
- Currency.
- Payment status.
- Creation timestamp.
- Last updated timestamp.
- Verification timestamp.
- Relevant non-sensitive provider metadata.

Consider a separate payment-event or webhook-receipt table if needed for reliable deduplication and recovery.

### Database Requirements

- Use the existing `milkshake` schema.
- Use EF Core migrations.
- Enforce appropriate unique constraints.
- Enforce valid payment statuses.
- Prevent negative amounts.
- Preserve currency consistency.
- Index provider transaction references.
- Index payment attempts by order.
- Preserve historical payment attempts.
- Avoid cascade deletion of financial records.
- Avoid storing card details or CVV.
- Do not persist raw secrets.
- Integrate with existing audit logging.

### Critical Design Question

Determine how to guarantee that one order cannot accidentally have multiple successful payments.

Evaluate:

- Unique partial indexes.
- Payment attempt state transitions.
- Provider idempotency.
- Checkout-session reuse.
- Server-side reconciliation.
- Concurrent payment attempts.

Do not rely solely on disabling the Pay button in React.

---

# 7. Payment Initiation

Implement a Patron-authorised payment initiation endpoint.

Possible route:

`POST /api/orders/{reference}/payments`

The exact route must follow the project's existing API conventions.

### Requirements

1. Authenticate the Patron.
2. Verify order ownership.
3. Confirm the order is eligible for payment.
4. Read the stored order total and currency.
5. Create or reuse a valid payment attempt.
6. Use an idempotency mechanism.
7. Call the payment provider securely.
8. Return only the information required to continue checkout.

### Important

The amount must come from the persisted M7 order snapshot.

Never use:

- The current catalogue prices.
- A browser-submitted total.
- A browser-submitted VAT rate.
- A browser-submitted discount.
- A browser-submitted customer ID.

If the provider requires minor currency units, convert using exact decimal rules and verify the currency's minor-unit scale.

Avoid floating-point conversions.

---

# 8. External Provider Calls and Database Transactions

Do not hold an open database transaction while waiting for a slow external payment provider unless there is a specifically justified reason.

Design for the possibility that:

- The provider creates a checkout session but the application crashes before recording its reference.
- The provider times out after accepting a request.
- The database save fails after the provider responds.
- Two initiation requests arrive simultaneously.
- A payment completes before the initiation response reaches the browser.

Recommend a recoverable state machine and idempotent provider interaction.

Use provider-supported idempotency keys where available.

If provider idempotency is unavailable, explain the limitations and provide a safe reconciliation strategy.

---

# 9. Payment Webhooks and Verification

Implement provider notifications according to the selected provider's official documentation.

### Security Requirements

- Verify signatures, tokens or authenticated notification mechanisms as supported by the provider.
- Validate the exact signed payload and encoding rules.
- Enforce any required timestamp or replay protection.
- Validate provider references.
- Validate payment amount.
- Validate currency.
- Validate the associated order.
- Validate the payment outcome.
- Reject unverified notifications.
- Avoid trusting browser return URLs.
- Avoid trusting unverified JSON status fields.
- Do not expose internal exceptions.

### Replay and Duplicate Handling

The provider may send the same notification more than once.

The webhook handler must:

- Recognise duplicates.
- Avoid duplicate state transitions.
- Avoid duplicate audit side effects.
- Return the provider-required acknowledgement for already processed events.
- Support notifications arriving out of order.
- Preserve an audit trail of verified events.
- Recover from interrupted processing.

Use a database-enforced deduplication mechanism when supported by the provider's event identifiers.

### Important

The webhook must not require the Patron's browser session or CSRF token.

Instead, authenticate the notification using the provider's documented verification method.

Keep provider callbacks isolated from Patron-only endpoints.

---

# 10. Verified Payment State Transitions

Use the existing M7 order lifecycle.

### Successful Payment

Only a verified successful payment may cause:

`PendingPayment → Confirmed`

or:

`PaymentFailed → Confirmed`

when a legitimate retry succeeds.

### Failed Payment

A verified failed attempt may cause:

`PendingPayment → PaymentFailed`

But distinguish:

- A failed payment attempt.
- A cancelled checkout session.
- A customer abandoning checkout.
- An expired provider session.
- A confirmed failure of the underlying payment.

Do not automatically equate all of these.

### Cancelled Orders

Do not confirm a cancelled order merely because a late webhook arrives.

If a provider reports successful payment after the order has been cancelled, record the financial event and flag it for reconciliation or refund handling.

Do not silently discard evidence of the charge.

### Financial Invariants

- Payment amount must match the stored order total.
- Payment currency must match the stored order currency.
- One order must not be marked confirmed twice.
- A verified duplicate event must not generate duplicate financial records.
- A browser response must not update the order to Confirmed.
- Order and payment status changes must be transactionally consistent.

---

# 11. Reconciliation and Recovery

Webhooks can be delayed, duplicated or lost.

Design a recovery process that can verify pending payments directly with the provider when supported.

Possible approaches:

- An authenticated payment-status lookup.
- A scheduled reconciliation job.
- An administrative reconciliation command.
- A retryable background worker.

Choose an approach appropriate for a sole-developer assignment and the selected provider.

Do not introduce unnecessary infrastructure.

### Required Scenarios

Handle:

- Payment succeeds but webhook is delayed.
- Payment succeeds but webhook never arrives.
- Browser closes after checkout.
- Customer returns before webhook processing finishes.
- Duplicate provider notifications.
- Provider status changes after an earlier pending response.
- Application restarts during processing.
- Network timeouts.
- Provider API errors.

Ensure recovery does not depend on the customer leaving their browser open.

---

# 12. React Payment Experience

Extend the existing order detail page:

`/orders/:reference`

M7 currently displays the stored order with an unpaid status and a disabled payment button.

M8 should introduce:

1. An enabled Pay Now action for eligible orders.
2. A payment-initiation loading state.
3. Safe handling of duplicate clicks.
4. Redirect to the approved checkout provider.
5. A payment return screen or appropriate status experience.
6. A clear Processing/Pending message while verification is underway.
7. A confirmed-payment message only after backend verification.
8. A failed-payment state with a safe retry option.
9. A cancelled-order state without an inappropriate Pay Now action.
10. A clear distinction between order status and payment attempt status.

### UX Rules

- Never display Paid solely because a return URL says success.
- Never store provider secrets in React.
- Never store card data.
- Never construct a payable amount in the browser.
- Never silently create multiple payment attempts.
- Never promise that a pending payment has succeeded.
- Display useful messages for delayed verification.
- Make the interface accessible and responsive.

Reuse the existing components and styling conventions.

---

# 13. Security Requirements

Preserve all previous security protections.

Add payment-specific protections.

## Patron Endpoints

- Require PatronOnly authorisation.
- Verify order ownership.
- Enforce CSRF.
- Apply suitable rate limits.
- Validate idempotency keys.
- Reject unknown JSON fields.
- Enforce request-size limits.
- Return safe ProblemDetails.

## Provider Endpoints

- Do not require Patron authentication.
- Do not require browser CSRF tokens.
- Authenticate notifications using provider-specific verification.
- Apply appropriate request-size limits.
- Apply suitable abuse protections without breaking legitimate provider retries.
- Avoid logging raw credentials or sensitive payment payloads.
- Reject invalid signatures and mismatched references.

## General

- Use server-side configuration and secrets management.
- Use HTTPS.
- Never expose API keys to the frontend.
- Avoid open redirects.
- Validate any provider checkout URL before redirecting.
- Never interpolate untrusted provider values into HTML.
- Avoid storing sensitive payment data unnecessarily.
- Do not commit `.env`, user secrets or credentials.

---

# 14. Testing Strategy

Preserve the existing 668 automated tests.

## Domain Tests

Test:

- Valid payment transitions.
- Invalid payment transitions.
- Amount and currency invariants.
- Duplicate-success prevention.
- Cancelled-order behaviour.
- Multiple payment attempts.
- Out-of-order events.

## Application Tests

Test:

- Valid payment initiation.
- Order ownership.
- Unpaid-order eligibility.
- Idempotent initiation.
- Provider timeout recovery.
- Provider response persistence.
- Verified success.
- Verified failure.
- Duplicate notification.
- Unknown payment reference.
- Mismatched amount.
- Mismatched currency.
- Invalid status transition.
- Reconciliation.

## PostgreSQL Integration Tests

Use xUnit and Testcontainers PostgreSQL 17.

Test:

- Payment persistence.
- Unique constraints.
- Atomic order/payment transitions.
- Concurrent payment attempts.
- Webhook deduplication.
- Transaction rollback.
- Restart and retry recovery.
- Immutable order amount.
- Audit integrity.
- Foreign-key restrictions.

## API Security Tests

Test:

- Anonymous Patron endpoint → 401.
- Manager-only account → 403.
- Another Patron's order → 404.
- Missing CSRF → 400.
- Forged amount → rejected.
- Forged currency → rejected.
- Forged order status → rejected.
- Invalid idempotency key → rejected.
- Invalid webhook signature → rejected.
- Duplicate webhook → safely acknowledged.
- Oversized request → rejected.
- Excessive requests → rate limited.
- Error responses do not reveal secrets.

## Frontend Tests

Use Vitest and Testing Library.

Test:

- Pay Now eligibility.
- Loading states.
- Duplicate-click prevention.
- Provider redirect.
- Payment pending state.
- Verified success state.
- Failed-payment retry.
- Cancellation messaging.
- Network errors.
- Accessibility.

## End-to-End Tests

Use Playwright and axe.

Where practical, use the payment provider's sandbox.

Test:

1. Patron signs in.
2. Places an order.
3. Opens order details.
4. Clicks Pay Now.
5. Enters or completes sandbox checkout.
6. Returns to the application.
7. Sees a pending verification state.
8. Receives verified success.
9. Sees the order marked Confirmed.
10. Refreshes and sees the persisted status.

Also test failure, duplicate notifications and unauthorised access.

Do not use real card credentials or charge real money during automated tests.

---

# 15. Implementation Sequence

## Step 1 — Existing Code and Requirements Review

Inspect:

- M8 roadmap acceptance criteria.
- Existing Order entity.
- Order status transitions.
- OrderPlacementService.
- OrderQueryService.
- EF Core DbContext.
- Audit interceptor.
- OrderingController.
- Order detail React page.
- Existing integration test infrastructure.

Identify reusable functionality.

Do not write payment code yet.

## Step 2 — Payment Provider Selection

Check whether the URS names a provider.

Present a comparison based on official documentation.

Ask me to confirm the provider.

Do not proceed with provider-specific code until I approve it.

## Step 3 — Payment State Machine and Database Design

Propose:

- Payment statuses.
- Payment attempts.
- Payment events.
- Idempotency strategy.
- Uniqueness constraints.
- Concurrency handling.
- Reconciliation approach.

Explain trade-offs.

Obtain approval.

## Step 4 — EF Core Entities and Migration

Implement the approved schema.

Generate and inspect the migration.

Test against disposable PostgreSQL.

Apply to Supabase only after approval.

## Step 5 — Provider Integration

Implement the selected provider's checkout initiation and status verification.

Keep credentials in server-side secrets.

Use test credentials first.

## Step 6 — Webhook Handling

Implement provider notification verification, deduplication and safe state transitions.

Test forged and duplicate events.

## Step 7 — Reconciliation

Implement recovery for delayed or missing notifications.

Test ambiguous provider outcomes.

## Step 8 — React Checkout Experience

Connect Pay Now to the payment API.

Implement provider checkout navigation and verified status display.

## Step 9 — Full Testing and Security Review

Run:

- Backend unit tests.
- PostgreSQL integration tests.
- Frontend tests.
- Frontend build.
- TypeScript checks.
- Lint.
- Playwright.
- Accessibility checks.
- Dependency vulnerability checks.
- Secret scans.

## Step 10 — Documentation and Sign-Off

Update:

- README.
- Payment architecture documentation.
- Payment provider setup guide.
- Webhook configuration guide.
- Payment decision register.
- M8 progress report.
- M9 integration notes.
- Git commit history.

---

# 16. Definition of Done

Milestone 8 is complete when:

- [ ] The payment provider is explicitly selected.
- [ ] Test credentials are configured securely.
- [ ] Payment attempts are stored in PostgreSQL.
- [ ] Payments use the persisted order total and currency.
- [ ] Patrons can initiate checkout for their own eligible orders.
- [ ] Duplicate initiation does not create unintended charges.
- [ ] Provider checkout works in test mode.
- [ ] Provider notifications are authenticated.
- [ ] Duplicate notifications are idempotent.
- [ ] Out-of-order notifications are handled safely.
- [ ] Amount and currency mismatches are rejected.
- [ ] Verified success updates the order to Confirmed.
- [ ] Verified failure is recorded correctly.
- [ ] Cancelled orders cannot be silently confirmed.
- [ ] Order/payment state changes are transactionally consistent.
- [ ] Payment recovery works without an open browser.
- [ ] React shows accurate pending, success and failure states.
- [ ] Patrons cannot access another Patron's payments.
- [ ] Existing security protections remain intact.
- [ ] Existing tests continue passing.
- [ ] New tests pass.
- [ ] Accessibility checks pass.
- [ ] No secrets or card details are exposed.
- [ ] Documentation is updated.

Any criteria that cannot be completed because of missing provider access or unresolved stakeholder decisions must be explicitly reported as pending, not falsely marked complete.

---

# 17. Important Boundaries

Do not implement the following prematurely:

- Refund processing without approved rules.
- Patron order cancellation without confirmed requirements.
- Manager payment overrides.
- Email receipts, which belong to M9.
- Management reporting, which belongs to M12.
- Production payment activation before security review.
- Unconfirmed frequent-customer discounts.

Do not change the M7 order lifecycle without approval.

Do not introduce fake successful payments for demonstration purposes.

---

# 18. Start Here

Begin with **Step 1 — Existing Code and Requirements Review**.

Ask me for the smallest practical set of files required to inspect the Order entity, order status transitions, EF Core mappings, audit interceptor and React order detail page.

Verify the roadmap's exact M8 acceptance criteria.

Then proceed to **Step 2 — Payment Provider Selection**.

Explain the provider options and ask me which payment provider to use.

**Do not generate payment gateway integration code until the provider and payment architecture are approved.**