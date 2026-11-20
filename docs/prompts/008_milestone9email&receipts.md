# Milestone 9 — Email Notifications and Transactional Receipts

## Your Role

Act as my **Senior ASP.NET Core Solutions Architect, Transactional Email Engineer, PostgreSQL Database Architect, Full-Stack Engineer, Application Security Engineer, and Technical Mentor**.

We are continuing development of **Order Milkshake for Pickup**, a production-quality demonstration application for **Milky Shaky Custom Drinks**.

I am the **sole developer**, working with Windows 11, Visual Studio Code and PowerShell.

Guide me through Milestone 9 incrementally using my existing codebase.

For each implementation step:

1. Explain the objective and design decisions.
2. Request and inspect the relevant existing source files.
3. Provide exact file paths and complete implementation code.
4. Supply Windows PowerShell commands.
5. Include automated tests and manual verification.
6. Explain security, reliability and transaction considerations.
7. Wait for my confirmation before continuing.

**Do not rebuild completed milestones or implement the entire milestone in one response.**

---

# 1. Authoritative Project Documents

Use the existing:

- User Requirement Specification v1.1.
- Development Roadmap.
- Milestone 7 Progress Report.
- Milestone 8 Payment Gateway Integration Report.
- Milestone 8b Order Cancellation and Refunds Report.

Verify the exact Milestone 9 acceptance criteria against the roadmap before implementation.

The M8 report references:

- AC-9.2: Email content.
- AC-9.3: Payment reference and order details.
- AC-9.4: Email failure must not roll back payment.

Verify the complete AC-9.1 through AC-9.x requirements rather than assuming this is the entire list.

If the URS or roadmap is unavailable in the repository or current conversation, request the relevant sections.

Do not invent stakeholder requirements.

Clearly distinguish confirmed requirements, approved developer decisions, provisional assumptions and unresolved questions.

---

# 2. Existing Project Architecture

## Backend

- .NET 10 ASP.NET Core Web API.
- C#.
- EF Core 10.
- Npgsql.
- Supabase PostgreSQL.
- Dedicated `milkshake` schema.
- Layered architecture:
  - `Milkshake.Api`
  - `Milkshake.Application`
  - `Milkshake.Domain`
  - `Milkshake.Infrastructure`.
- Existing dependency injection conventions.
- Existing audit interceptor.
- Existing background payment reconciliation service.

## Frontend

- React 19.
- TypeScript.
- Vite 8.
- Tailwind CSS v4.
- Authenticated API client.
- Patron and Manager experiences.

## Security

- ASP.NET Core Identity.
- Secure HTTP-only session cookies.
- PatronOnly and ManagerOnly authorisation.
- CSRF protection.
- Rate limiting.
- Strict JSON request validation.
- Request-body limits.
- Safe ProblemDetails.
- Server-side secrets management.

---

# 3. Completed Business Functionality

## Ordering

Patrons can:

- Configure milkshakes.
- Select a restaurant.
- Choose a pickup time.
- Review server-calculated pricing.
- Place an order.
- View their own orders.

## Payments

Paystack hosted checkout is implemented in **test mode**.

Payments are verified server-side through:

- Signed webhooks.
- Provider verification API.
- Patron-initiated status refresh.
- Background reconciliation.

Orders become Confirmed only after verified payment success.

Payment attempts and events are persisted and audited.

## Cancellation and Refunds

M8b implemented:

- Free cancellation of eligible unpaid orders.
- Paid cancellation before pickup with a configurable cancellation fee.
- Manager cancellation requests with reasons.
- Patron acceptance or rejection of manager requests.
- Full refunds when Patrons accept manager-requested cancellations.
- Automatic refunds for certain payments that cannot be accepted.
- Paystack refund verification.
- Refund reconciliation.
- Manager attention lists for failed or problematic refunds.

### Existing Refund Rules

The current implementation uses:

- `Refund.FeeRate`, initially 10%.
- Fee calculated from the VAT-exclusive amount.
- Refund equal to amount paid minus applicable fee.
- Full refunds for accepted manager cancellation requests.
- Refund status determined by verified Paystack outcomes.

Do not recalculate refund amounts in the email system.

Use the stored refund records.

Some cancellation rules remain provisional pending stakeholder confirmation.

---

# 4. Current Automated Test Baseline

The latest M8b report records:

- **777 backend tests** passing.
- **156 frontend tests** passing.
- **9 Playwright end-to-end tests** passing.

Total: **942 automated tests**.

Preserve this baseline.

Do not claim tests passed without executing them.

---

# 5. Primary Objective

Implement reliable transactional email notifications.

The application should send the appropriate email when a verified business event occurs.

Potential notification types include:

1. Successful payment and order confirmation.
2. Manager cancellation request.
3. Order cancellation.
4. Refund processed.
5. Refund failed or requiring customer attention.

The M8 reports propose one combined order and payment confirmation email.

**Verify the exact email requirements and obtain approval for the final notification matrix before coding.**

Additional email types must be justified by the URS, roadmap or approved business decisions.

---

# 6. Fundamental Reliability Requirement

**An email failure must never undo a successful payment, order confirmation, cancellation or refund.**

Do not send transactional emails synchronously inside the payment verification transaction.

Do not call an external email provider before committing the corresponding business state.

Use the **transactional outbox pattern**.

### Required Behaviour

When a business event occurs:

1. Update the relevant business entity.
2. Create the corresponding email outbox record.
3. Persist both changes in the same database transaction.
4. Commit successfully.
5. Process the email asynchronously.
6. Retry temporary delivery failures.
7. Preserve a record of delivery attempts and outcomes.

A provider outage must not block payment confirmation.

A process crash after committing the transaction must not lose the notification.

---

# 7. Email Provider Selection

Before implementing provider-specific code, inspect the URS and roadmap for a required provider.

If none is specified, compare suitable options such as:

- Brevo.
- Resend.
- SendGrid.
- SMTP through an approved email service.

Compare:

- Free or test-tier availability.
- Support for transactional email.
- API versus SMTP.
- Sender-domain verification.
- Delivery status tracking.
- Rate limits.
- Retry behaviour.
- Developer setup complexity.
- Production suitability.
- Availability for a South African demonstration project.

Use current official provider documentation for provider-specific claims.

Do not assume an email provider account already exists.

Ask me to approve the provider before implementing its adapter.

### Demonstration-Store Consideration

Paystack remains in test mode.

Determine whether email delivery should:

- Use a real transactional email provider in test mode.
- Send only to approved test recipients.
- Use a local email-capture tool during development.
- Support both local capture and provider delivery.

Recommend a safe approach that avoids accidentally emailing real customers during testing.

Do not send real email until I approve the sender, recipient policy and provider configuration.

---

# 8. Email Notification Matrix

Propose a notification matrix before coding.

At minimum, evaluate the following events.

| Business event | Proposed recipient | Proposed notification |
|---|---|---|
| Verified payment success and order confirmation | Patron | Combined order and payment receipt |
| Manager cancellation request created | Patron | Cancellation request requiring a response |
| Patron cancels an order | Patron | Cancellation confirmation |
| Patron accepts manager cancellation request | Patron | Cancellation confirmation with full-refund details |
| Refund becomes Processed | Patron | Refund completion confirmation |
| Refund becomes Failed | Patron | Refund failure and support instructions |

Consider whether a refund marked `NeedsAttention` warrants an email, and verify the existing business rules.

Do not send a refund-completed email when the refund has merely been requested or submitted.

Do not send a payment receipt when a checkout redirect succeeds but payment verification is still pending.

Do not send duplicate confirmation emails when the same Paystack event is processed multiple times.

### Recipient Privacy

- Email the Patron associated with the order.
- Never trust a recipient address supplied by the browser.
- Do not email a manager cancellation reason to unrelated recipients.
- Do not expose another Patron's order information.
- Avoid including unnecessary personal data.

---

# 9. Transactional Email Outbox Design

Design an outbox table consistent with existing PostgreSQL conventions.

Potential fields:

- Internal ID.
- Notification type.
- Related order ID.
- Related payment ID, where applicable.
- Related refund ID, where applicable.
- Related cancellation request ID, where applicable.
- Recipient email address.
- Recipient name, where needed.
- Email subject.
- Template identifier or version.
- Immutable payload snapshot.
- Delivery status.
- Attempt count.
- Next attempt timestamp.
- Last attempt timestamp.
- Sent timestamp.
- Provider message identifier.
- Safe error category.
- Creation timestamp.
- Concurrency information.

These are design candidates, not mandatory columns.

Use the minimum schema needed to satisfy the requirements.

### Important Design Decisions

Determine:

1. Whether to store a rendered email or a structured payload.
2. Whether recipient email is snapshotted at event time.
3. How template changes affect queued emails.
4. How to identify unique business notifications.
5. How to support multiple email types for the same order.
6. How to avoid duplicate outbox records.
7. How to retry after uncertain provider outcomes.
8. How to avoid exposing personal data in audit logs.
9. How long email records should be retained.

### Deduplication

Use database-enforced uniqueness for each logical notification.

Examples:

- One payment-confirmation notification per confirmed order.
- One cancellation-request notification per request.
- One cancellation notification per cancellation event.
- One refund-completion notification per refund.
- One refund-failure notification per refund.

Do not rely only on application-level checks.

---

# 10. Email Delivery Semantics

Understand the difference between:

- Exactly-once outbox creation.
- At-least-once delivery attempts.
- Provider-side idempotency.
- Exactly-once customer receipt.

A database transaction can guarantee that an outbox row is created atomically with a business event.

It cannot, by itself, guarantee exactly-once delivery by an external email provider.

If the provider accepts an email and the application crashes before recording success, the email may be sent again.

Design the safest practical strategy using:

- Stable notification identifiers.
- Provider idempotency keys, if supported.
- Message metadata.
- Delivery status lookups, if available.
- Retry limits.
- Manual reconciliation for ambiguous outcomes.

Do not falsely promise exactly-once external delivery.

---

# 11. Background Email Worker

Implement an email dispatcher using the simplest architecture appropriate for this project.

Consider an ASP.NET Core `BackgroundService` using existing infrastructure.

### Requirements

- Poll for due outbox messages.
- Process bounded batches.
- Avoid unbounded memory use.
- Support graceful shutdown.
- Handle provider timeouts.
- Retry transient failures.
- Distinguish permanent failures.
- Use exponential backoff with a maximum delay.
- Respect provider rate limits.
- Avoid endless immediate retries.
- Avoid blocking application startup if the email provider is unavailable.
- Keep the worker safe when the application restarts.

### Concurrency

If more than one application instance runs, workers must not claim the same message simultaneously.

Evaluate:

- PostgreSQL `FOR UPDATE SKIP LOCKED`.
- Lease-based claims.
- Optimistic concurrency.
- Single-worker deployment assumptions.

Choose a strategy and document its guarantees.

### Recovery

Handle:

- Crash before sending.
- Crash after provider acceptance.
- Database failure after provider acceptance.
- Temporary provider outage.
- Permanent invalid recipient.
- Application restart.
- Multiple worker instances.
- Poison messages.

---

# 12. Email Templates

Create professional, accessible transactional email templates consistent with Milky Shaky Custom Drinks.

Use responsive HTML with a plain-text alternative.

Do not depend on JavaScript in email clients.

Do not include external tracking resources unless explicitly approved.

### A. Order and Payment Confirmation

Include confirmed requirements such as:

- Order reference.
- Restaurant name and pickup location.
- Pickup date and time.
- Milkshake quantities.
- Flavour, consistency and topping selections.
- Per-drink prices.
- Subtotal.
- Discount.
- VAT rate and amount.
- Final paid amount.
- Currency.
- Payment reference.
- Confirmation status.

Use immutable order and payment snapshots.

Do not fetch today's catalogue fees to reconstruct the receipt.

### B. Manager Cancellation Request

Include:

- Order reference.
- Manager-provided reason.
- Instructions to accept or decline.
- A link to the order or cancellation-request page.
- Relevant response deadline.

Never embed authentication tokens or one-click state-changing actions in an email.

The Patron must authenticate before responding.

### C. Cancellation Confirmation

Include:

- Order reference.
- Cancellation status.
- Whether the order was paid.
- Refund amount, if applicable.
- Cancellation fee, if applicable.
- Whether the refund is still pending.

Avoid describing a pending refund as completed.

### D. Refund Completion

Include:

- Order reference.
- Refund reference.
- Amount refunded.
- Currency.
- Applicable cancellation fee.
- Verified refund completion status.

### E. Refund Failure

Include:

- Order reference.
- Refund reference.
- Refund amount.
- Current verified failure status.
- Appropriate support instructions.

Do not request bank details or card information by email.

---

# 13. Transaction Integration Points

Inspect existing services and identify the exact transaction boundaries.

## Payment Confirmation

M8 identifies:

`Payment.ApplyVerifiedOutcome`

This can be reached through:

- Signed webhook handling.
- Patron payment refresh.
- Background payment reconciliation.

The email outbox must be written when payment success is verified and the order becomes Confirmed.

The outbox record must be saved in the same transaction as the order and payment state changes.

Do not enqueue the email separately after the transaction commits.

Do not enqueue another confirmation email when an already-confirmed payment is reverified.

## Order Cancellation

M8b identifies:

`OrderCancellationService`

Create the cancellation notification in the same transaction as the successful cancellation.

Do not send it before cancellation commits.

## Manager Cancellation Request

M8b identifies:

`ManagerOrderService`

Create the request notification in the same transaction as the cancellation request.

Use the stored reason and correct Patron recipient.

## Refund Completion or Failure

M8b identifies refund webhook and reconciliation handling.

Create notifications only on verified terminal transitions.

Do not send a completion email while a refund is still Pending or NeedsAttention.

Ensure duplicate webhook processing does not create duplicate notifications.

---

# 14. Email Address and Identity Considerations

Review the existing ASP.NET Core Identity implementation.

M4 deferred some identity-email features to M9.

Verify whether the roadmap requires:

- Email confirmation.
- Password reset.
- Email address changes.
- Account recovery notifications.

Do not automatically treat these as equivalent to order notification emails.

Identity emails may require different templates, tokens, expiry rules and security protections.

### Security Requirements

- Use ASP.NET Identity token providers where appropriate.
- Never expose reset tokens in logs.
- Never send plaintext passwords.
- Avoid account enumeration.
- Apply rate limits.
- Use HTTPS links.
- Ensure token expiry.
- Use one-time or appropriately invalidated reset flows.
- Do not place sensitive tokens in analytics or tracking URLs.

If these features are part of M9, propose a separate implementation phase.

If not, document them as deferred.

---

# 15. API and Management Visibility

Determine whether the roadmap requires email delivery status visibility.

Potential options:

- Internal structured logging.
- Administrative delivery-status endpoint.
- Manager-only failed-notification list.
- A protected retry operation.
- An operational command for reprocessing failed emails.

Avoid building an unnecessary email administration interface.

If a manager endpoint is required:

- Use ManagerOnly.
- Require CSRF for mutations.
- Apply rate limits.
- Validate requests strictly.
- Do not expose full email payloads unnecessarily.
- Do not permit arbitrary recipient substitution.
- Audit manual retries.

---

# 16. Security and Privacy

Preserve all existing security controls.

### Secrets

- Store provider API keys only in server-side configuration or .NET user secrets.
- Never expose keys in React.
- Never commit credentials.
- Do not log API keys or full provider responses.

### Email Content

- HTML-encode dynamic values.
- Treat cancellation reasons as untrusted text.
- Prevent HTML injection.
- Prevent email header injection.
- Avoid leaking internal IDs.
- Do not expose sensitive customer details unnecessarily.
- Use trusted application URLs.
- Prevent malicious redirect links.

### Privacy

- Minimise personal data in outbox records.
- Avoid storing unnecessary raw provider responses.
- Do not include email bodies in routine logs.
- Restrict access to outbox records.
- Document retention and deletion requirements.

The existing Q-14 POPIA retention question remains unresolved.

Do not invent a retention policy.

---

# 17. Database Migration

Create a reviewed, additive EF Core migration.

Requirements:

- Use the existing `milkshake` schema.
- Follow current naming conventions.
- Add required outbox tables and indexes.
- Add appropriate status and attempt-count constraints.
- Preserve existing order, payment and refund data.
- Avoid destructive schema changes.
- Avoid cascading deletion of financial notification history.
- Verify transactional audit behaviour.

### Migration Process

1. Propose the schema.
2. Obtain approval.
3. Generate the migration.
4. Inspect generated SQL.
5. Run tests with Testcontainers PostgreSQL.
6. Verify existing data.
7. Apply to Supabase only after approval.

---

# 18. Testing Strategy

Preserve the existing **942-test baseline**.

## Domain and Application Tests

Test:

- Correct notification type for each business event.
- Correct recipient selection.
- Correct snapshot data.
- Duplicate-event suppression.
- No notification on failed business transactions.
- No notification on unverified payment.
- No refund-complete notification for pending refunds.
- Correct cancellation reason handling.
- Template rendering and HTML escaping.
- Valid monetary formatting.

## PostgreSQL Integration Tests

Test:

- Outbox creation in the same transaction as payment confirmation.
- Outbox creation in the same transaction as cancellation.
- Outbox creation with manager cancellation requests.
- Outbox creation on verified refund terminal states.
- Unique notification constraints.
- Transaction rollback removes the outbox row.
- Duplicate webhooks create one logical notification.
- Worker concurrency.
- Lease expiry and recovery.
- Restart recovery.
- Failed delivery retries.
- Permanent failure handling.

## Provider Tests

Use a simulated email provider or mocked HTTP transport.

Test:

- Successful send.
- Temporary provider error.
- Rate limiting.
- Timeout.
- Invalid recipient.
- Provider rejection.
- Ambiguous acceptance.
- Duplicate delivery attempt.
- Safe handling of provider errors.

## API Security Tests

Where email-related endpoints are added, test:

- Anonymous access.
- Patron versus Manager authorisation.
- CSRF.
- Ownership.
- Strict JSON.
- Rate limits.
- Request-size limits.
- Safe ProblemDetails.
- No arbitrary email recipient injection.

## Frontend Tests

Only add frontend functionality required by the approved M9 scope.

Possible cases:

- Email sent or queued status messaging.
- Failed notification messaging, if exposed.
- Password reset forms, if included.
- Email confirmation flows, if included.

Do not invent unnecessary frontend features.

## End-to-End Tests

Use Playwright and axe where applicable.

Test the relevant customer journeys and assert that the expected outbox notification was created.

Use a local email capture service or simulated provider for automated tests.

Where a real sandbox email is approved, verify the rendered email and recipient manually.

Do not send test emails to real customer addresses.

---

# 19. Implementation Sequence

## Step 1 — Requirements and Existing Code Review

Inspect the smallest relevant set of files:

- M9 roadmap acceptance criteria.
- URS email requirements.
- Payment verification and settlement service.
- Refund completion service.
- OrderCancellationService.
- ManagerOrderService.
- DbContext.
- Audit interceptor.
- Existing background reconciliation worker.
- Identity email configuration.
- Relevant integration test fixtures.

Identify the exact transaction boundaries and where email outbox rows must be inserted.

**Do not begin coding yet.**

## Step 2 — Email Provider and Notification Decisions

Present:

- Provider comparison.
- Test-recipient policy.
- Proposed notification matrix.
- Identity-email scope.
- Open stakeholder decisions.

Obtain my approval.

## Step 3 — Outbox Architecture and Database Design

Propose:

- Outbox entity.
- Status model.
- Unique notification keys.
- Payload structure.
- Retry strategy.
- Worker concurrency strategy.
- Retention considerations.

Obtain approval before migration.

## Step 4 — Database Migration

Implement the approved schema.

Generate and inspect migration SQL.

Test with disposable PostgreSQL.

Apply to Supabase only after approval.

## Step 5 — Email Provider Adapter

Implement the selected provider or local capture adapter.

Keep provider-specific code in Infrastructure.

Add provider integration tests.

## Step 6 — Templates and Rendering

Implement responsive HTML and plain-text templates.

Use stored order, payment, cancellation and refund snapshots.

Test rendering and escaping.

## Step 7 — Business Transaction Integration

Add transactional outbox creation to:

- Verified payment confirmation.
- Order cancellation.
- Manager cancellation requests.
- Verified refund completion.
- Verified refund failure.

Test atomicity and duplicate suppression.

## Step 8 — Background Dispatcher

Implement reliable delivery, retries, concurrency protection and recovery.

Test process restarts and provider failures.

## Step 9 — Identity Emails, If Required

Implement approved email confirmation and password reset flows if they belong to M9.

Otherwise document the deferral.

## Step 10 — Full Regression and Security Testing

Run:

- Backend unit and integration tests.
- Frontend Vitest tests.
- Playwright.
- Accessibility scans.
- TypeScript checks.
- Lint.
- Backend build with warnings as errors.
- Dependency vulnerability checks.
- Secret scans.

## Step 11 — Documentation and Sign-Off

Update:

- README.
- Email architecture documentation.
- Email provider setup guide.
- Notification matrix.
- Email template documentation.
- Outbox recovery documentation.
- Decision register.
- M9 progress report.
- M11 integration notes.
- Git commit history.

---

# 20. Definition of Done

Milestone 9 is complete when:

- [ ] The roadmap's exact email acceptance criteria have been reviewed.
- [ ] Email provider and recipient policy are approved.
- [ ] Notification events and recipients are confirmed.
- [ ] Transactional email outbox is implemented.
- [ ] Outbox records are created atomically with business events.
- [ ] Verified payment confirmation creates one logical receipt notification.
- [ ] Manager cancellation requests create appropriate notifications.
- [ ] Successful cancellations create appropriate notifications.
- [ ] Verified refund completion creates appropriate notifications.
- [ ] Verified refund failure creates appropriate notifications.
- [ ] Duplicate events do not create duplicate logical notifications.
- [ ] Email delivery failures never roll back payments or refunds.
- [ ] Background delivery and retry handling work.
- [ ] Restart recovery works.
- [ ] Concurrent workers do not claim the same message simultaneously.
- [ ] Email templates use immutable transaction data.
- [ ] HTML and plain-text email versions are available.
- [ ] Email content is appropriately escaped.
- [ ] Customer privacy is preserved.
- [ ] No email credentials are exposed.
- [ ] Existing tests remain passing.
- [ ] New automated tests pass.
- [ ] Accessibility checks pass where applicable.
- [ ] Documentation is updated.
- [ ] Any unresolved stakeholder decisions are documented.

If email confirmation or password reset is required by the authoritative documents, add their approved acceptance criteria to this checklist.

Do not mark any criterion complete without evidence.

---

# 21. Start Here

Begin with **Step 1 — Requirements and Existing Code Review**.

Ask me for the minimum relevant source files and the exact Milestone 9 acceptance criteria.

Inspect the existing payment confirmation, refund completion, cancellation and manager-request transaction boundaries.

Then propose the notification matrix, email provider options and outbox design.

**Do not write implementation code until the provider, notification scope and architecture have been reviewed and approved.**