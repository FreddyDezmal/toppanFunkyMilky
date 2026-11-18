# Milestone 10 — Lookup and Configuration Management

## Role

Act as my Senior .NET Solutions Architect, Full-Stack Engineer, PostgreSQL Database Architect, and Technical Mentor.

We are continuing the **Order Milkshake for Pickup** application for Milky Shaky Custom Drinks.

I am the sole developer. Guide me through implementing Milestone 10 incrementally, with complete code, exact file paths, Windows 11 PowerShell commands, testing procedures and explanations of architectural decisions.

Do not rebuild functionality already implemented.

## 1. Current Project State

Milestone 1 and Milestone 4 are complete.

**Backend:** .NET 10, ASP.NET Core controllers, layered API/Application/Domain/Infrastructure architecture, EF Core with Npgsql, Supabase PostgreSQL.

**Frontend:** React, TypeScript, Vite and Tailwind CSS v4.

**Authentication:** ASP.NET Core Identity, HTTP-only secure cookies, CSRF protection, Patron and Manager roles, manager-only authorisation policy, same-origin Vite proxy.

**Database:** Existing `milkshake` schema with flavours, toppings, consistencies, restaurants, system settings, discount tiers, effective-dated price tables, audit logs and security events.

**Auditing:** Transactional EF Core save interceptor with field-level changes and sensitive-value redaction.

**Tests:** 50 backend, 13 frontend and 2 Playwright tests reported passing.

Use the existing URS v1.1 as the authoritative requirements source.

Before implementing anything, request the relevant current source files and inspect them. Never assume exact entity names, namespaces, properties or configuration that have not been verified.

## 2. Primary Objective

Build a secure, fully audited management interface allowing authorised managers to maintain the application's configurable business data.

The system must preserve historical data and ensure that configuration changes apply to **new orders only**, without altering existing open, completed or concluded transactions.

## 3. Required Management Features

### A. Flavour Management

Managers must be able to view, create, edit, activate and deactivate flavours.

Initial URS flavours:

- Strawberry
- Vanilla
- Chocolate
- Coffee
- Banana
- Oreo
- Bar one

Do not hard-delete referenced catalogue entries.

### B. Topping Management

Support the same management operations for:

- Frozen Strawberries
- Freeze-dried banana
- Oreo crumbs
- Bar one syrup
- Coffee powder with chocolate
- Chocolate vermicelli

### C. Consistency Management

Support management of:

- Double thick
- Thick
- Milky
- Icy

### D. Restaurant Management

Allow managers to maintain restaurant records and their active status.

Inspect the existing restaurant model before defining fields. Do not invent addresses, trading hours or pickup capabilities as confirmed business requirements.

### E. Price Management

Allow managers to configure fees for flavours, toppings and consistencies.

Use the existing effective-dated price history architecture.

Requirements:

- Store money as decimal values, never floating point.
- Preserve currency information.
- Validate price inputs.
- Prevent invalid or overlapping effective periods.
- Preserve historical price records.
- Apply new prices prospectively.
- Ensure edits do not silently rewrite existing prices or transactions.
- Display current prices and price history to managers.

Do not assume the final milkshake pricing formula or VAT-inclusive/exclusive treatment without confirming the relevant business decisions.

### F. System Settings

Build management functionality for the settings already present in the database, including:

- Minimum drink quantity.
- Maximum drink quantity.
- VAT rate.
- Maximum discount.
- Pickup lead time.
- Currency.
- Display locale.

Inspect existing settings and validation rules before implementation.

Clearly distinguish settings mandated by the URS from project-specific recommendations.

### G. Discount Tier Management

Allow managers to view and maintain the existing discount tier structure.

However, the following rules remain unresolved:

- Discount percentages.
- Meaning of the maximum discount cap.
- Qualifying-order criteria.
- Discount calculation relative to VAT.

Do not invent these rules or activate placeholder discount tiers.

Implement only the configuration functionality that can be safely completed before those decisions are confirmed.

## 4. Critical Data Integrity Requirements

Treat these as non-negotiable:

1. Existing orders must retain their original pricing and configuration snapshots.
2. New configuration values must not retroactively alter historical transactions.
3. Configuration changes must be fully auditable.
4. Each change must record the actor, timestamp and relevant before/after values.
5. Database updates and audit records must commit atomically.
6. Concurrent manager edits must not silently overwrite one another.
7. Deactivated lookup values must remain available for historical reporting.
8. Public or Patron accounts must never be able to modify configuration.
9. The API must validate all changes independently of frontend validation.
10. Existing authentication, CSRF and security protections must remain intact.

Investigate optimistic concurrency using PostgreSQL/EF Core and recommend a suitable implementation.

## 5. API Implementation

Design clear, RESTful manager endpoints for:

- Listing catalogue items.
- Creating new entries.
- Updating entries.
- Activating/deactivating entries.
- Viewing current prices.
- Scheduling or changing prices.
- Viewing price history.
- Reading and updating system settings.
- Viewing and maintaining discount tiers.

Use DTOs and validation rather than exposing EF Core entities directly.

Enforce Manager authorisation at the API boundary.

Use appropriate HTTP status codes and ProblemDetails responses.

Support pagination, sorting and filtering where the data or use case warrants them, without adding unnecessary complexity.

## 6. React Management Interface

Extend the existing manager area with:

- A management navigation layout.
- Flavour management.
- Topping management.
- Consistency management.
- Restaurant management.
- Pricing and price-history screens.
- System settings.
- Discount tier configuration.

Include:

- Accessible forms.
- Field-level validation.
- Loading and error states.
- Confirmation for deactivation.
- Clear success feedback.
- Optimistic concurrency conflict handling.
- Responsive layout.
- Protection against accidental duplicate submissions.

Follow the established frontend design and API client conventions.

## 7. Auditing

Reuse the existing transactional audit interceptor.

Verify that all relevant configuration changes capture:

- Entity type.
- Record identifier.
- Action.
- Changed field.
- Previous value.
- New value.
- Acting user.
- Timestamp.
- Trace identifier.

Do not create a duplicate auditing framework.

If the existing audit implementation has gaps, explain and address them with focused changes.

A full searchable audit-report interface may remain in M11, but M10 changes must already generate complete audit records.

## 8. Testing

Preserve all 65 existing automated tests.

Add tests for:

- Manager CRUD operations.
- Patron access denied.
- Anonymous access denied.
- Invalid input handling.
- Duplicate lookup names.
- Activation and deactivation.
- Price-history integrity.
- Effective-date boundaries.
- Concurrent modifications.
- Atomic auditing.
- CSRF enforcement.
- React form behaviour.
- Manager navigation and end-to-end workflows.

Use xUnit with Testcontainers PostgreSQL for database-dependent backend integration tests, Vitest for frontend components and Playwright for end-to-end tests.

Do not claim tests have passed until they have been executed.

## 9. Implementation Sequence

Proceed one step at a time.

**Step 1 — Existing Code Review:** Inspect the current domain entities, DbContext, configurations, services, audit interceptor, manager endpoints and React manager area.

**Step 2 — Requirements and API Design:** Define management operations, DTOs, validation rules and unresolved business decisions.

**Step 3 — Catalogue Management Backend:** Implement flavours, toppings, consistencies and restaurants.

**Step 4 — Effective-Dated Pricing:** Implement price maintenance and history safely.

**Step 5 — Settings and Discount Configuration:** Implement validated management operations without inventing unresolved discount rules.

**Step 6 — Concurrency and Auditing:** Confirm atomic changes, historical preservation and conflict handling.

**Step 7 — React Manager Screens:** Build the management interface and integrate the API.

**Step 8 — Automated Tests and Security Review:** Run existing and new tests, investigate failures and verify role enforcement.

**Step 9 — Documentation and Sign-Off:** Update README, progress report and implementation notes; prepare Git commits.

## 10. Response Format

For each step, provide:

- Objective.
- Architectural explanation.
- Exact files to inspect, create or modify.
- Complete implementation code.
- PowerShell commands.
- Verification instructions.
- Expected results.
- Security and data-integrity considerations.
- Next step.

Wait for my confirmation before advancing to the next major step.

Never request passwords, connection strings or secrets.

## 11. Definition of Done

Milestone 10 is complete when:

- Managers can maintain all required lookup categories.
- Managers can maintain restaurants.
- Managers can configure prices with preserved history.
- Managers can maintain supported system settings.
- Discount configuration respects unresolved business rules.
- Changes are validated and audited.
- Historical data remains intact.
- Concurrent changes are handled safely.
- Patrons and anonymous users cannot access management operations.
- The React management interface works end to end.
- Existing and new automated tests pass.
- No credentials or confidential documents are committed.
- Documentation and progress reports are updated.

## Start Here

Begin with **Step 1 — Existing Code Review**.

Request the smallest practical set of source files necessary to inspect the existing catalogue entities, price history, DbContext, auditing and manager interface.

Do not generate replacement entities or migrations until you've inspected what already exists.