# Milestone 5 — Milkshake Ordering and Pickup Selection

## Your Role

Act as my **Senior Full-Stack Engineer, ASP.NET Core Solutions Architect, PostgreSQL Database Architect, Software Security Engineer, and Technical Mentor**.

We are continuing development of **Order Milkshake for Pickup**, a production-quality web application for **Milky Shaky Custom Drinks**.

I am the **sole developer**, working on Windows 11 with Visual Studio Code and PowerShell.

Your responsibility is to guide me through implementing Milestone 5 incrementally, using my existing application rather than rebuilding completed functionality.

For every step, provide complete implementation code, exact file paths, PowerShell commands, explanations, tests, and verification instructions.

**Work one manageable step at a time. Wait for my confirmation before proceeding.**

---

## 1. Project Background

The application allows registered customers (Patrons) to:

1. Log into their accounts.
2. Choose how many milkshakes they want.
3. Customise each milkshake individually.
4. Select a restaurant for collection.
5. Choose an appropriate pickup time.
6. Review their order before proceeding to payment.
7. Eventually pay, receive confirmation and view order history.

Managers configure the available milkshake options, restaurants, prices, settings and discount tiers.

The authoritative requirements are documented in:

**Order Milkshake for Pickup — User Requirement Specification v1.1**

Use that document as the source of truth whenever available.

Do not invent business requirements. Clearly distinguish confirmed requirements, existing project decisions and implementation recommendations.

## 2. Existing Architecture — Already Implemented

### Backend

- .NET 10 ASP.NET Core Web API using controllers.
- Layered architecture:
  - `Milkshake.Api`
  - `Milkshake.Application`
  - `Milkshake.Domain`
  - `Milkshake.Infrastructure`
- Dependency injection through `AddApplication()` and `AddInfrastructure()`.
- RFC 9457 ProblemDetails error handling.
- OpenAPI and Scalar in Development.
- Configurable CORS.
- Existing health checks.

### Database

- Supabase-hosted PostgreSQL.
- EF Core 10 and Npgsql.
- Dedicated `milkshake` schema.
- Existing Identity, catalogue, pricing, restaurant, settings, discount and audit tables.
- Effective-dated prices for flavours, toppings and consistencies.
- `Money` value type with decimal amounts and ISO 4217 currency codes.
- EF Core migrations.
- Transactional audit interceptor.
- PostgreSQL `xmin` optimistic concurrency on editable configuration.
- Case-insensitive unique catalogue names.

### Authentication and Security

Milestone 4 is complete.

- ASP.NET Core Identity.
- HTTP-only, Secure, SameSite=Strict session cookie.
- Same-origin development through the Vite proxy.
- Global CSRF protection on state-changing requests.
- `Patron` and `Manager` roles.
- Secure-by-default authorisation.
- Manager-only policies.
- Secure manager provisioning.
- Authentication security-event logging.

Do not introduce Supabase Auth, custom JWT authentication or browser-stored authentication tokens.

### Frontend

- React 19 with TypeScript.
- Vite 8.
- Tailwind CSS v4.
- Feature-based folder structure.
- Existing HTTP client and CSRF integration.
- Authentication provider and protected routes.
- Accessible forms and error handling.
- Manager interface with shared UI components.

### Completed Milestone 10

Managers can:

- Create, edit, activate and deactivate flavours.
- Create, edit, activate and deactivate toppings.
- Create, edit, activate and deactivate consistencies.
- Manage restaurants.
- Configure effective-dated prices.
- View price histories.
- Manage system settings.
- Maintain discount tiers without activating unconfirmed discount rules.

Important existing decisions:

- Prices cannot be backdated.
- Effective prices are immutable.
- Future prices may be scheduled.
- The latest future price may be cancelled.
- Catalogue entries are deactivated rather than deleted.
- Currency is read-only in M10.
- Price times are stored in UTC.
- Configuration changes must not retroactively change existing orders.

### Existing Tests

The latest progress report records:

- **144 backend tests** passing.
- **46 frontend component tests** passing.
- **4 Playwright end-to-end tests** passing.

Total baseline: **194 automated tests**.

Preserve all existing tests and security controls.

Do not claim tests have passed unless they have actually been executed.

---

## 3. Primary Milestone Objective

Implement the customer-facing **milkshake configuration and pickup selection workflow**.

By the end of M5, an authenticated Patron should be able to:

1. Start a new milkshake order.
2. Select a quantity of drinks within the configured limits.
3. Configure each drink individually.
4. Select exactly one flavour per drink.
5. Select exactly one consistency per drink.
6. Select exactly one topping per drink.
7. Choose an active pickup restaurant.
8. Select a pickup date and time according to confirmed rules.
9. Review a clear summary of their selections.
10. Edit their selections before proceeding to the next milestone.

The workflow must use real catalogue and restaurant data from Supabase through the ASP.NET Core API.

**Do not implement real payment processing, completed orders, payment confirmation or email receipts in M5.**

Those belong to subsequent milestones.

---

## 4. Functional Requirements

### A. Customer Ordering Catalogue

Create customer-facing API endpoints to retrieve the available:

- Flavours.
- Toppings.
- Consistencies.
- Restaurants.

Requirements:

- Return only active catalogue entries.
- Return only active restaurants.
- Respect the existing display ordering.
- Include appropriate current price information where needed.
- Use existing effective-dated pricing rules.
- Do not expose internal EF Core entities.
- Do not expose manager-only configuration operations.
- Avoid unnecessary database queries.
- Handle missing or invalid configuration safely.

If an active catalogue entry has no valid current price, the system must not silently treat its price as zero.

Recommend whether such entries should be excluded from the customer catalogue or cause a controlled availability error. Explain the trade-offs before implementing.

### B. Configurable Drink Quantity

Allow Patrons to select the number of milkshakes.

The URS initially specifies a quantity between **1 and 10**, but these limits are configurable by management.

Requirements:

- Retrieve the current minimum and maximum from the existing system settings.
- Validate quantity on the frontend.
- Validate quantity independently on the backend.
- Never hard-code 10 as the permanent maximum.
- Handle configuration changes while a customer is completing an order.
- Provide clear validation messages.

When the quantity changes:

- Increasing quantity should add blank drink configurations.
- Decreasing quantity should remove excess configurations only after an appropriate user experience decision.
- Existing valid configurations should be preserved where possible.

Do not silently discard customer selections.

### C. Individual Milkshake Customisation

Each drink must have its own configuration.

For every drink, require:

**Exactly one flavour:**
- Strawberry
- Vanilla
- Chocolate
- Coffee
- Banana
- Oreo
- Bar one

**Exactly one consistency:**
- Double thick
- Thick
- Milky
- Icy

**Exactly one topping:**
- Frozen Strawberries
- Freeze-dried banana
- Oreo crumbs
- Bar one syrup
- Coffee powder with chocolate
- Chocolate vermicelli

These are the initial URS values, not permanently hard-coded frontend options.

Load the actual options from the backend.

Requirements:

- Each drink is independently configurable.
- Every required choice must be selected.
- No duplicate drink configuration is required unless the user intentionally chooses identical drinks.
- Show clear labels and validation messages.
- Preserve selections when navigating between workflow steps.
- Handle options becoming inactive while the customer is editing.
- Reject invalid or tampered catalogue identifiers on the backend.

Do not add unsupported requirements such as multiple toppings, extra ingredients, size choices or substitutions.

### D. Restaurant Selection

Allow the Patron to select a pickup restaurant.

Requirements:

- Retrieve active restaurants from the backend.
- Display restaurant names and any available confirmed address information.
- Require a restaurant selection.
- Validate the restaurant on the backend.
- Reject inactive or nonexistent restaurants.
- Handle a restaurant being deactivated during the ordering process.

Do not invent restaurant opening hours, capacity limits or operating schedules.

### E. Pickup Date and Time

Implement pickup selection using the business rules that have actually been confirmed.

The current system contains a configurable pickup lead-time setting, but the exact pickup rules remain unresolved.

Open question **Q-06** must be addressed before finalising pickup validation.

Discuss:

- Earliest permitted pickup time.
- Whether pickup is available only during restaurant opening hours.
- Same-day versus future-day pickup.
- Maximum advance booking period.
- Time-zone handling.
- Whether pickup slots have capacity limits.
- What happens when a restaurant becomes unavailable.

Do not invent these rules and present them as URS requirements.

If stakeholder confirmation is unavailable, recommend a clearly labelled provisional implementation that can be replaced without substantial rework.

Use UTC for persisted timestamps and an explicit time-zone strategy for restaurant-local pickup times.

Avoid relying on the browser's time zone as the restaurant's time zone.

### F. Order Review

Build a review screen displaying:

- Selected restaurant.
- Selected pickup date and time.
- Number of drinks.
- Each drink's flavour.
- Each drink's consistency.
- Each drink's topping.
- Available price information, where appropriate.
- An option to return and edit selections.

Do not present an unverified amount as the final payable total.

M6 will implement the authoritative pricing engine, including VAT and discount calculations.

If provisional prices are displayed, clearly distinguish them from the final price.

### G. Draft State and Navigation

Recommend an appropriate approach to maintaining an in-progress order.

Evaluate:

1. React in-memory state.
2. React state with session storage.
3. Server-persisted draft orders.

Consider:

- Complexity for a solo developer.
- Browser refresh behaviour.
- Authentication.
- Sensitive data exposure.
- Database changes.
- Audit requirements.
- Future integration with M6 and M7.

Prefer the simplest approach that meets the current requirements without creating unnecessary database tables.

If drafts are persisted, define ownership, expiration and cleanup rules before implementation.

Do not mistake a frontend draft for a completed or paid order.

---

## 5. Backend Validation and Security

The backend must be authoritative for every order selection.

Implement validation for:

- Authenticated access.
- Quantity limits.
- Required drink selections.
- Valid flavour identifiers.
- Valid topping identifiers.
- Valid consistency identifiers.
- Active catalogue entries.
- Valid active restaurant.
- Pickup rules that have been confirmed.
- Duplicate or malformed request data.
- Unexpected client-supplied fields.

Use request DTOs rather than binding database entities directly.

Maintain the existing secure-by-default authorisation policy.

Patrons must not gain access to management endpoints.

Do not trust prices or configuration values submitted by the browser.

When the authoritative pricing engine is implemented in M6, it must calculate prices from server-side data.

Continue using existing ProblemDetails and field-level validation conventions.

---

## 6. Database Design

Before adding any new database tables, inspect the current schema and explain whether M5 genuinely requires persistent order drafts.

The existing roadmap defers full order and transaction persistence to M7.

Prefer avoiding premature order/payment schema implementation unless a confirmed M5 requirement requires it.

If any schema changes are necessary:

1. Explain the reason.
2. Design the entities and relationships.
3. Review the impact on historical data.
4. Integrate with existing auditing.
5. Generate an EF Core migration.
6. Inspect the migration SQL.
7. Test against disposable PostgreSQL.
8. Apply to Supabase only after verification.

Do not introduce payment tables or mark orders as completed during M5.

---

## 7. React Ordering Experience

Create a polished, responsive, accessible customer ordering interface.

Suggested screens:

**Step 1 — Quantity**

Choose how many milkshakes to order.

**Step 2 — Customise Drinks**

Configure every milkshake individually.

**Step 3 — Pickup**

Select the restaurant and pickup date/time.

**Step 4 — Review**

Review all selections and edit before continuing.

The precise navigation design may be adjusted after reviewing the existing React structure.

### User Experience Requirements

- Clear progress indication.
- Accessible form labels.
- Keyboard navigation.
- Responsive mobile and desktop layouts.
- Visible validation errors.
- Focus management after validation failures.
- Loading states.
- Network error handling.
- Empty catalogue states.
- Safe handling of stale selections.
- Prevention of accidental duplicate submissions.
- No unnecessary page reloads.
- Clear distinction between an order draft and a submitted order.

Reuse existing components and styling patterns wherever practical.

Avoid adding unnecessary frontend dependencies.

---

## 8. Pricing Integration Boundary

M5 must prepare the ordering workflow for M6 without prematurely implementing the full pricing engine.

The existing database already stores prices for flavours, toppings and consistencies.

However, several pricing rules are unresolved:

- Whether fees are VAT-inclusive.
- How individual prices combine.
- Discount percentages and thresholds.
- Meaning of the maximum discount.
- Which orders count toward frequent-customer eligibility.
- Whether discounts apply before or after VAT.

Therefore:

- Do not invent a final pricing formula.
- Do not activate discount tiers.
- Do not calculate authoritative VAT or discounts in React.
- Do not allow the browser to determine payable amounts.
- Design clear DTOs that can be extended for M6.
- Keep the future order-price snapshot requirement in mind.

M7 must eventually preserve the actual prices, VAT and discount values used when an order was placed.

---

## 9. Automated Testing

Preserve the existing baseline of 194 tests.

### Backend Tests

Add tests for:

- Anonymous users being denied access to protected ordering endpoints.
- Active catalogue entries being returned.
- Inactive catalogue entries being excluded.
- Active restaurants being returned.
- Inactive restaurants being excluded.
- Configured quantity limits.
- Missing flavour selection.
- Missing topping selection.
- Missing consistency selection.
- Invalid catalogue identifiers.
- Deactivated catalogue selections.
- Invalid restaurant identifiers.
- Pickup-time validation.
- Configuration changing between retrieval and validation.
- Malformed or tampered requests.
- Safe handling of missing current prices.

Use xUnit and Testcontainers PostgreSQL for database-dependent integration tests.

### Frontend Tests

Add Vitest and Testing Library tests for:

- Quantity selection.
- Adding and removing drink configurations.
- Preserving existing drink selections.
- Required-field validation.
- Restaurant selection.
- Pickup date/time input.
- Order summary.
- Navigation between steps.
- Loading and error states.
- Stale catalogue data.
- Accessibility behaviour.

### End-to-End Tests

Use Playwright to verify:

1. A Patron logs in.
2. Starts a new milkshake order.
3. Selects multiple drinks.
4. Configures each drink.
5. Selects a restaurant.
6. Chooses a valid pickup time.
7. Reviews the draft.
8. Returns to edit a selection.
9. Sees the updated review.

Include negative tests for invalid or unavailable selections.

Run accessibility checks using the existing axe integration.

Do not require payment to complete the M5 end-to-end test.

---

## 10. Recommended Implementation Sequence

Guide me through the following steps.

### Step 1 — Existing Code Review

Request the minimum relevant files needed to inspect:

- Catalogue domain entities.
- Effective-dated pricing implementation.
- Restaurant entity and management services.
- System settings.
- Existing API controllers and DTO conventions.
- Authentication and authorisation configuration.
- React routing and API client.
- Existing manager UI patterns.
- Current tests and test infrastructure.

Identify reusable components and architectural constraints.

Do not generate new entities or code before reviewing the existing implementation.

### Step 2 — Requirements and Workflow Design

Define:

- Confirmed M5 requirements.
- Open business questions.
- Customer journey.
- Draft state strategy.
- API endpoint contracts.
- Validation boundaries.
- Pickup-time strategy.
- M6/M7 integration points.

Get my approval before proceeding.

### Step 3 — Customer Catalogue API

Implement read-only, authenticated customer endpoints for active catalogue options, restaurants and relevant ordering settings.

Add backend tests.

### Step 4 — Order Selection Validation

Implement server-side validation of drink quantity, individual configurations, active catalogue identifiers and restaurant selection.

Add tests for tampered and stale selections.

### Step 5 — Pickup Selection Rules

Implement only confirmed pickup validation rules.

Clearly identify provisional rules and obtain approval before implementing them.

### Step 6 — React Order Draft State

Implement typed draft models and state management following the approved design.

Ensure edits do not unexpectedly erase customer selections.

### Step 7 — React Ordering Screens

Implement quantity, customisation, pickup and review screens.

Reuse the existing authentication and API infrastructure.

### Step 8 — Integration and Security Testing

Run backend, frontend and Playwright tests.

Verify authorisation, validation, accessibility, stale selections and failure handling.

### Step 9 — Documentation and Sign-Off

Update:

- README.
- Relevant architecture documentation.
- M5 progress report.
- Open business questions.
- Test results.
- Next-milestone integration notes.

Prepare clean Git commits without including credentials or confidential documents.

---

## 11. Implementation Standards

For each step:

1. Explain what we are building and why.
2. Identify exact files to inspect or modify.
3. Provide complete code with correct namespaces and imports.
4. Provide PowerShell commands compatible with Windows 11.
5. Explain how to run and test the changes.
6. Describe expected successful results.
7. Explain security, concurrency and data-integrity considerations.
8. Wait for confirmation before continuing.

Additional rules:

- Preserve the existing layered architecture.
- Keep domain rules independent of ASP.NET Core.
- Keep EF Core and database operations in Infrastructure.
- Use existing DI registration conventions.
- Avoid unnecessary abstractions.
- Avoid unnecessary dependencies.
- Do not bypass the existing audit interceptor.
- Do not weaken CSRF or role-based security.
- Do not expose database credentials to React.
- Do not store trusted pricing information in client-controlled state.
- Do not invent requirements absent from the URS.
- Avoid implementing future milestones prematurely.
- Do not claim tests pass without running them.

---

## 12. Definition of Done

Milestone 5 is complete when:

- [ ] Authenticated Patrons can start a new milkshake draft.
- [ ] Available flavours are loaded from the backend.
- [ ] Available toppings are loaded from the backend.
- [ ] Available consistencies are loaded from the backend.
- [ ] Only active options are available for new selections.
- [ ] Drink quantity follows configurable limits.
- [ ] Each drink requires exactly one flavour.
- [ ] Each drink requires exactly one consistency.
- [ ] Each drink requires exactly one topping.
- [ ] Patrons can configure multiple drinks independently.
- [ ] Patrons can select an active restaurant.
- [ ] Pickup selection follows confirmed or explicitly approved provisional rules.
- [ ] Patrons can review and edit their draft.
- [ ] Backend validation rejects invalid or stale selections.
- [ ] React ordering screens are accessible and responsive.
- [ ] Existing authentication and security protections remain intact.
- [ ] No unconfirmed pricing or discount rules are invented.
- [ ] No real payment is processed.
- [ ] No completed order is created prematurely.
- [ ] All existing tests continue to pass.
- [ ] New backend, frontend and end-to-end tests pass.
- [ ] Documentation is updated.
- [ ] No credentials or confidential documents are committed.

---

## 13. Start Here

Begin with **Step 1 — Existing Code Review**.

Ask me for the smallest practical set of existing source files needed to understand the catalogue, pricing, restaurants, settings, API conventions and frontend structure.

After reviewing them, propose the M5 implementation design.

**Do not start coding immediately, do not recreate existing infrastructure, and do not advance to Step 2 until we have reviewed the current code.**