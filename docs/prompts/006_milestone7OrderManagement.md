# Milestone 7 — Order Processing and Transactions

## Your Role

Act as my **Senior ASP.NET Core Solutions Architect, PostgreSQL Database Architect, Full-Stack Engineer, Transaction Processing Specialist, Software Security Engineer and Technical Mentor**.

We are continuing development of **Order Milkshake for Pickup**, a production-quality application for **Milky Shaky Custom Drinks**.

I am the **sole developer**, working on Windows 11 with Visual Studio Code and PowerShell.

Guide me through Milestone 7 incrementally, using my existing codebase.

For each step:

1. Explain the objective and design decisions.
2. Request and review the relevant existing source files.
3. Provide exact file paths and complete implementation code.
4. Supply Windows PowerShell commands.
5. Include automated tests and manual verification.
6. Explain security, concurrency and transaction considerations.
7. Wait for my confirmation before continuing.

**Do not rebuild completed milestones or advance through the entire implementation in one response.**

---

## 1. Project Context

### Authoritative Documents

- Order Milkshake for Pickup — User Requirement Specification v1.1.
- Order Milkshake for Pickup — Development Roadmap.
- Milestone 5 Progress Report.
- Milestone 6 Progress Report.

Use the URS and roadmap as the source of truth. Verify the exact M7 acceptance criteria against those documents before implementing.

Do not invent stakeholder requirements.

Clearly distinguish confirmed requirements, approved implementation decisions, provisional rules and unresolved questions.

## 2. Completed Architecture

### Backend

- .NET 10 ASP.NET Core Web API.
- C#.
- EF Core 10 with Npgsql.
- Supabase-hosted PostgreSQL.
- Dedicated `milkshake` schema.
- Four-layer solution:
  - `Milkshake.Api`
  - `Milkshake.Application`
  - `Milkshake.Domain`
  - `Milkshake.Infrastructure`.
- Controller-based API.
- RFC 9457 ProblemDetails.
- Existing dependency injection conventions.
- Transactional audit interceptor.

### Authentication

- ASP.NET Core Identity.
- HTTP-only Secure SameSite=Strict authentication cookie.
- Patron and Manager roles.
- PatronOnly authorisation.
- CSRF protection.
- Per-patron ordering rate limit.
- Strict JSON request contracts.
- Request-body size limits.

### Frontend

- React 19.
- TypeScript.
- Vite 8.
- Tailwind CSS v4.
- Feature-based structure.
- Existing authenticated API client.
- Four-step customer ordering workflow.
- Vitest, Testing Library, Playwright and axe.

### Milestone 5

Implemented:

- Quantity selection.
- Individual milkshake customisation.
- Active catalogue retrieval.
- Restaurant selection.
- Provisional pickup rules.
- Stateless server-side draft validation.
- Browser-tab draft persistence using `sessionStorage`.
- Patron-only ordering endpoints.

The existing draft request contains:

- `restaurantId`
- `pickupLocal`
- `drinks[]`

Each drink contains identifiers for its flavour, consistency and topping.

### Milestone 6

Implemented:

- Pure domain pricing calculator.
- Effective-dated price retrieval.
- Server-authoritative quote generation.
- Per-drink prices.
- VAT calculation.
- Order subtotal and total.
- React live price estimates.
- Pricing response timestamp.
- Discount engine with activation gate.

Existing application service:

`IOrderPricingService`

Relevant operation:

`PriceDraftAsync`

The server calculates the amount from catalogue prices in force at one pricing instant.

The browser does not submit trusted prices, VAT rates, discounts or totals.

### Current Test Baseline

The latest M6 report records:

- **470 backend tests** passing.
- **100 frontend tests** passing.
- **8 Playwright tests** passing.

Total: **578 automated tests**.

Preserve this baseline.

Do not claim tests have passed unless they have been executed.

---

# 3. Primary Objective

Implement secure, persistent order creation and transaction management.

By the end of M7, a Patron should be able to:

1. Complete a valid milkshake draft.
2. Review its current server-calculated price.
3. Submit the order.
4. Have the order validated again by the backend.
5. Have the latest applicable prices calculated on the server.
6. Create an order in PostgreSQL.
7. Preserve immutable pricing details.
8. Receive an order identifier or reference.
9. View the resulting order and its status.
10. Continue toward payment in M8.

The system must prevent duplicate orders, unauthorised access, incorrect totals and partial database writes.

**M7 must not implement actual payment gateway processing.**

---

# 4. Requirements Review Before Coding

Before implementation, inspect the URS and roadmap to confirm:

- Required order fields.
- Required order statuses.
- Order reference format, if specified.
- Whether orders are created before payment.
- Whether an unpaid order reserves a pickup time.
- Cancellation rules.
- Whether Patrons can edit submitted orders.
- Whether managers can update order status.
- Whether orders expire when unpaid.
- Whether failed payments can be retried.
- Whether submitted orders can be deleted.
- Whether the system distinguishes pending, confirmed, cancelled and completed orders.

Do not invent business rules.

If the documents do not answer these questions, present the unresolved decisions and recommend the smallest safe provisional design.

Obtain my approval before implementing rules that affect order lifecycle or payment eligibility.

---

# 5. Order Domain Model

Design order entities consistent with the existing Domain layer.

Consider:

### Order

Potential fields:

- Internal order ID.
- Customer/Patron ID.
- Public order reference.
- Restaurant ID.
- Restaurant name snapshot.
- Pickup instant in UTC.
- Pickup time zone.
- Order status.
- Payment status, if justified by confirmed requirements.
- Creation timestamp.
- Pricing timestamp.
- Currency.
- Subtotal.
- Discount amount.
- Taxable amount.
- VAT rate.
- VAT amount.
- Final payable total.
- Applied discount tier information.
- Concurrency token where appropriate.

### Order Item

Potential fields:

- Internal item ID.
- Order ID.
- Drink sequence number.
- Flavour ID.
- Flavour name snapshot.
- Flavour price snapshot.
- Consistency ID.
- Consistency name snapshot.
- Consistency price snapshot.
- Topping ID.
- Topping name snapshot.
- Topping price snapshot.
- Individual drink total.

Use actual confirmed requirements to decide which fields are necessary.

Do not add unnecessary columns merely because they appear in this suggested list.

### Historical Integrity

Order snapshots must preserve the original transaction values.

If managers later:

- Rename a flavour.
- Deactivate a topping.
- Change a consistency price.
- Change the VAT rate.
- Update discount settings.
- Rename a restaurant.

Existing orders must still display the original names, amounts, tax details and restaurant information captured when placed.

Do not calculate historical order totals from today's catalogue prices.

Do not rely exclusively on foreign keys to reconstruct historical orders.

---

# 6. Database Design and EF Core

Implement appropriate PostgreSQL tables and EF Core configurations.

Requirements:

- Use the existing `milkshake` schema.
- Follow current naming conventions.
- Use existing ID conventions where appropriate.
- Use appropriate numeric precision for money.
- Use UTC timestamps for stored instants.
- Use explicit foreign keys and indexes.
- Preserve referential integrity.
- Use the existing audit interceptor.
- Use EF Core migrations.
- Avoid cascade deletion that destroys order history.
- Define constraints for invalid amounts and quantities where appropriate.
- Avoid database changes outside approved migrations.

### Migration Procedure

1. Design the entities and relationships.
2. Review the schema with me.
3. Generate the migration.
4. Inspect generated SQL.
5. Run migration tests against disposable PostgreSQL.
6. Verify existing data remains intact.
7. Apply to Supabase only after approval.

Do not automatically apply an unreviewed migration to the hosted database.

---

# 7. Order Creation Workflow

Design a single authoritative order placement operation.

Suggested sequence:

1. Authenticate the Patron.
2. Validate CSRF.
3. Validate the request shape.
4. Check idempotency.
5. Load current catalogue and settings.
6. Validate all selected milkshakes.
7. Validate the restaurant.
8. Revalidate the pickup time.
9. Calculate the current authoritative price.
10. Compare it with the last price acknowledged by the Patron, if an approved price-confirmation mechanism is used.
11. Create the order and order items.
12. Persist immutable snapshots.
13. Commit the database transaction.
14. Return the order reference and current status.

The precise ordering of idempotency checks, price calculation and transaction boundaries must be reviewed for correctness and concurrency.

### Critical Requirements

- No client-submitted amount may determine the order total.
- No client-submitted discount eligibility may be trusted.
- All order items must be saved atomically.
- An order must never be partially persisted.
- The same submission must not create multiple orders.
- A failure before commit must not leave an incomplete order.
- A retry after an ambiguous network failure must safely return the original result.
- A database exception must not leak internal information to the client.

---

# 8. Idempotency and Duplicate Prevention

Implement a robust strategy for duplicate submissions.

Consider:

- Client-generated idempotency keys.
- A unique database constraint scoped to the Patron.
- Request fingerprints.
- Stored outcomes for completed requests.
- Concurrent identical submissions.
- Repeated requests after timeouts.
- Reuse of a key with a different request.

### Required Behaviour

- Same Patron + same key + same request must not create another order.
- Same Patron + same key + different request must be rejected.
- Concurrent requests with the same key must not create duplicate orders.
- Different Patrons must not share idempotency scope.
- Database constraints must enforce the invariant, not only application-level checks.

Explain whether the idempotency key belongs on the order or in a separate submission table.

Choose the simplest correct design.

Add tests for concurrent requests and retries.

---

# 9. Price Confirmation and Race Conditions

A Patron may review a quote and submit an order after a manager changes prices.

M6 already distinguishes estimates from final order amounts.

M7 must not silently charge or commit an unexpected amount.

Recommend a price-acknowledgement strategy.

Possible approaches:

- A short-lived server-issued quote token.
- A server-side quote record.
- A client-acknowledged quote fingerprint.
- An expected total and currency used only for comparison, never as authoritative pricing input.

If an expected total is submitted, it may be used to detect a mismatch but must never be used to calculate the payable amount.

### Required Behaviour

- Reprice on the server at submission.
- Detect meaningful differences from the amount acknowledged by the Patron.
- Return a clear conflict response if prices changed.
- Show the revised amount.
- Require the Patron to confirm the new price before retrying.
- Avoid creating an order on the mismatch path.

Do not invent a price-lock guarantee.

---

# 10. Order Status and Lifecycle

Define a small, explicit order state machine based on confirmed requirements.

Potential states might include:

- PendingPayment.
- Confirmed.
- Cancelled.
- Completed.

These are examples, not approved business rules.

Determine:

- Which transitions are permitted.
- Which transitions require successful payment.
- Which transitions may be initiated by a Patron.
- Which transitions may be initiated by a Manager.
- Which transitions are prohibited.
- Whether unpaid orders expire.
- Whether payment failures change order status.
- Whether cancellation is allowed after payment.

Do not allow a Patron to mark an order as paid or completed.

Do not treat an order as paid merely because the browser reports payment success.

If payment-related transitions belong to M8, implement only the minimal safe lifecycle necessary for M7.

---

# 11. API Design

Review existing endpoint conventions before adding new endpoints.

Potential routes:

- `POST /api/ordering/orders`
- `GET /api/ordering/orders/{id}`
- `GET /api/ordering/orders`

The exact route names should follow existing conventions and roadmap requirements.

### API Requirements

- PatronOnly authorisation.
- Ownership checks.
- CSRF on state-changing operations.
- Rate limiting.
- Request-size limits.
- Strict JSON validation.
- Appropriate HTTP status codes.
- ProblemDetails for errors.
- No EF Core entities returned directly.
- No internal exception details.
- No exposure of other Patrons' orders.
- No trust in browser-supplied prices.

### Order Creation Response

Include the minimum necessary information, such as:

- Order identifier or reference.
- Order status.
- Price snapshot.
- Restaurant and pickup details.
- Next permitted action.

Use HTTP 201 Created where appropriate.

For an idempotent replay, return a consistent representation without creating another order.

---

# 12. Frontend Order Placement

Extend the existing React review screen.

Currently, M5 and M6 provide:

- Draft selection.
- Server-side validation.
- Live price estimates.
- Review summary.
- A disabled payment continuation action.

M7 should add:

1. A clear Place Order action.
2. A final price confirmation experience.
3. Submission loading state.
4. Duplicate-click protection.
5. Idempotency key management.
6. Clear handling of network timeouts.
7. Clear handling of changed prices.
8. Order confirmation or pending-payment screen.
9. Safe navigation to the created order.
10. Draft cleanup after successful creation.

### Important UX Rules

- Do not clear the draft before successful persistence.
- Do not regenerate the idempotency key merely because a request timed out.
- Do not silently retry a different request using the same key.
- Do not imply that an order is paid when it is only created.
- Display the stored order snapshot, not a fresh catalogue-based recalculation.
- Make payment status and order status distinguishable.

Reuse existing React components and API infrastructure.

---

# 13. Transaction Integrity

Order creation must be atomic.

Evaluate:

- EF Core transaction boundaries.
- Unique constraints.
- PostgreSQL isolation levels.
- Concurrent order submissions.
- Price changes during order creation.
- Time-dependent price selection.
- Audit interceptor behaviour.
- Connection resilience and retries.
- Ambiguous transaction commit outcomes.

Use the simplest correct transaction strategy.

Do not assume that a pre-insert duplicate check is sufficient.

Do not retry non-idempotent writes without a reliable idempotency mechanism.

### Audit Requirements

Record meaningful changes to order entities through the existing audit infrastructure.

Ensure audit entries are committed in the same transaction as the corresponding changes.

Do not expose passwords, tokens or sensitive payment information in audit records.

---

# 14. Order History Foundations

M11 will implement the full customer order-history and audit experience.

M7 should establish the necessary order data and ownership rules.

At minimum, determine whether the Patron needs to:

- View a newly created order.
- Retrieve their own order details.
- View a basic list of their own orders.

Implement only what is needed for M7 acceptance criteria and immediate post-submission usability.

Leave advanced history filtering, searching, reporting and audit views for M11/M12.

---

# 15. Discount Eligibility Dependency

M6 implemented the discount engine, but live discounts remain disabled.

Current state:

- `DiscountPolicy.RulesConfirmed = false`.
- `NoOrderHistoryYet` is the eligibility source.
- Placeholder discount rates are not active.
- Q-01 to Q-04 remain unresolved.

M7 introduces persistent orders, which may provide the basis for future discount eligibility.

However:

- Do not automatically activate discounts.
- Do not assume that every created order qualifies.
- Do not count unpaid orders as completed purchases unless the stakeholder explicitly approves that rule.
- Do not invent eligibility criteria.
- Design order status and data so a future eligibility source can query qualifying orders efficiently.
- Implement the eligibility source only after Q-03 is confirmed.

The application must continue showing a zero discount while the gate remains closed.

---

# 16. Payment Integration Boundary

M8 will implement payment processing.

M7 must prepare for payment without pretending payment has occurred.

Requirements:

- Persist the authoritative payable amount.
- Persist the currency.
- Give each order a stable identifier.
- Clearly identify unpaid orders.
- Prevent unauthorised status changes.
- Avoid storing card details.
- Avoid trusting browser payment-success messages.
- Keep payment-provider logic out of the Domain layer.
- Do not create a fake successful payment.

Do not implement gateway checkout, webhooks or payment reconciliation yet.

---

# 17. Automated Testing

Preserve the existing 578-test baseline.

## Domain Unit Tests

Test:

- Valid order state transitions.
- Invalid order state transitions.
- Order snapshot invariants.
- Quantity consistency.
- Immutable pricing semantics.
- Money and currency consistency.
- Invalid status changes.

## Application Tests

Test:

- Valid order placement.
- Invalid draft rejection.
- Revalidation of catalogue options.
- Pickup-time revalidation.
- Current pricing use.
- Price mismatch handling.
- Duplicate idempotency keys.
- Reused keys with different payloads.
- Concurrent submissions.
- Ownership enforcement.
- Failure before commit.

## Database Integration Tests

Use xUnit and Testcontainers PostgreSQL 17.

Test:

- Order and items persisted atomically.
- Price snapshots survive manager price changes.
- Price snapshots survive catalogue renames.
- VAT snapshots survive VAT-setting changes.
- Order audit rows are persisted transactionally.
- Unique idempotency constraints.
- Concurrent identical requests.
- Failed transactions leave no partial order.
- Foreign-key and check constraints.
- No accidental cascade deletion of historical orders.

## API Security Tests

Test:

- Anonymous 401.
- Manager-only account 403 on PatronOnly endpoints.
- Patron ownership isolation.
- CSRF enforcement.
- Strict JSON.
- Forged totals rejected.
- Forged order status rejected.
- Forged customer ID rejected.
- Rate limiting.
- Request-body limits.
- Safe ProblemDetails responses.

## Frontend Tests

Test:

- Place Order button behaviour.
- Submission loading state.
- Duplicate-click protection.
- Stable idempotency key.
- Timeout and retry behaviour.
- Price-change conflict.
- Updated price acknowledgement.
- Draft cleanup after success.
- Order confirmation display.
- Unpaid status messaging.
- Navigation and accessibility.

## End-to-End Tests

Use Playwright and axe.

Test a full Patron journey:

1. Log in.
2. Select quantity.
3. Customise milkshakes.
4. Choose restaurant.
5. Choose pickup time.
6. Review server-calculated price.
7. Place order.
8. Receive order reference.
9. See stored order details.
10. Confirm unpaid status.
11. Verify the draft is cleared.
12. Refresh and confirm the order remains available.

Also test:

- Duplicate submission.
- Network retry.
- Price change before placement.
- Unauthorised access to another Patron's order.

Avoid creating excessive permanent test data in the development database. Use isolated test data or explicit cleanup where practical.

---

# 18. Recommended Implementation Sequence

## Step 1 — Existing Code and Requirements Review

Request the smallest relevant file set covering:

- Domain entities and value types.
- EF Core DbContext.
- Entity configurations.
- Audit interceptor.
- Ordering draft validator.
- Pricing service.
- Pricing breakdown models.
- Ordering controller.
- React order review.
- React draft persistence.
- Existing integration test setup.

Review M7's exact roadmap acceptance criteria.

Identify reusable code and unresolved decisions.

**Do not begin coding yet.**

## Step 2 — Order Lifecycle and Data Model

Propose:

- Order entity.
- Order item entity.
- Snapshot structure.
- Order statuses.
- Relationships.
- Constraints.
- Indexes.
- Idempotency strategy.
- Price acknowledgement strategy.

Explain trade-offs and obtain approval.

## Step 3 — Database Migration

Implement approved entities and EF Core mappings.

Generate and inspect migration SQL.

Test with disposable PostgreSQL.

Apply to Supabase only after approval.

## Step 4 — Order Placement Service

Implement server-authoritative order placement.

Reuse `IOrderPricingService.PriceDraftAsync`.

Add idempotency and transaction handling.

## Step 5 — Order Retrieval and Ownership

Implement secure retrieval of a Patron's own order.

Add API security tests.

## Step 6 — Price Conflict and Retry Handling

Implement changed-price detection and safe retry behaviour.

Test concurrent requests and network failures.

## Step 7 — React Order Placement

Connect the existing review screen to the order creation endpoint.

Add confirmation and pending-payment views.

## Step 8 — Full Integration and Security Testing

Run backend, frontend and Playwright suites.

Test historical integrity, concurrency, ownership and failure recovery.

## Step 9 — Documentation and Sign-Off

Update:

- README.
- Database architecture documentation.
- Order lifecycle decision register.
- M7 progress report.
- M8 integration notes.
- Open stakeholder questions.
- Git commit history.

---

# 19. Definition of Done

M7 is complete when:

- [ ] Patrons can submit valid drafts.
- [ ] Orders are persisted in PostgreSQL.
- [ ] Order items are persisted atomically.
- [ ] Each order has a stable identifier/reference.
- [ ] Every submitted selection is revalidated.
- [ ] Pickup time is revalidated at placement.
- [ ] Current prices are recalculated on the server.
- [ ] A changed price requires customer acknowledgement.
- [ ] Component prices and names are snapshotted.
- [ ] VAT rate and amount are snapshotted.
- [ ] Discount details are snapshotted.
- [ ] Order total and currency are immutable.
- [ ] Manager catalogue changes do not alter existing orders.
- [ ] Duplicate submissions do not create duplicate orders.
- [ ] Concurrent submissions are handled safely.
- [ ] Transaction failures do not leave partial orders.
- [ ] Patrons can retrieve their own orders.
- [ ] Patrons cannot access other customers' orders.
- [ ] New orders are not marked paid without verified payment.
- [ ] The frontend displays a successful order placement.
- [ ] The draft is cleared only after successful creation.
- [ ] Audit entries are transactionally persisted.
- [ ] Existing security controls remain intact.
- [ ] All existing and new tests pass.
- [ ] Documentation is updated.
- [ ] No credentials or confidential documents are committed.

If a criterion depends on an unresolved stakeholder decision, document the dependency rather than inventing the rule.

---

# 20. Start Here

Begin with **Step 1 — Existing Code and Requirements Review**.

Ask me for the minimum set of source files required to inspect the existing pricing service, draft validator, DbContext, audit interceptor and React order review.

Also request the relevant M7 acceptance criteria from the roadmap if the full document is unavailable.

Review those files before proposing the order model or database migration.

Then present a concise architecture proposal and the business decisions that must be confirmed.

**Do not begin coding until the design has been reviewed and approved.**