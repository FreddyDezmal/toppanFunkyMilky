# Milestone 4: Authentication and Role-Based Access Control

## Your Role

Act as my **Senior ASP.NET Core Security Architect, Full-Stack Engineer, and Technical Mentor**.

We are continuing development of **Order Milkshake for Pickup**, a production-quality web application for Milky Shaky Custom Drinks.

I am the sole developer. Guide me through implementation one manageable step at a time, providing complete code, exact file paths, executable PowerShell commands, verification procedures, and explanations of important architectural and security decisions.

Do not recreate functionality already implemented during Milestone 1.

## 1. Project Context

The application allows patrons to:

- Register and authenticate.
- Configure milkshakes.
- Select restaurants and pickup times.
- Place orders and complete payments.
- Receive email confirmations and receipts.
- View their order history.

Managers will maintain configurable product options, prices, VAT and discounts, review audit history, and generate reports.

The requirements originate from the confidential **Order Milkshake for Pickup User Requirement Specification v1.1**.

Use the URS as the authoritative source whenever it is available. Do not invent business requirements.

## 2. Existing Technical Foundation — Already Completed

Milestone 1 has been implemented and verified.

### Backend

- .NET 10 ASP.NET Core Web API using controllers.
- Layered architecture with four projects:
  - `Milkshake.Api`
  - `Milkshake.Application`
  - `Milkshake.Domain`
  - `Milkshake.Infrastructure`
- Dependency injection through `AddApplication()` and `AddInfrastructure()`.
- OpenAPI and Scalar documentation in Development.
- RFC 9457 ProblemDetails error responses.
- Configurable CORS.
- A working health endpoint.

### Database

- Supabase-hosted PostgreSQL.
- EF Core 10 with Npgsql.
- Dedicated `milkshake` schema.
- Existing ASP.NET Core Identity tables.
- Existing `Patron` and `Manager` roles.
- GUID-based user identifiers.
- EF Core migrations already applied.
- Transactional auditing through an EF Core save interceptor.
- Sensitive audit values are redacted.
- Database credentials stored securely in .NET user secrets.

### Frontend

- React + TypeScript + Vite.
- Tailwind CSS v4.
- Feature-based folder structure.
- Existing API client infrastructure.
- Working frontend-to-backend communication.

### Testing

All 15 existing automated tests pass:

- 7 backend unit tests.
- 3 backend integration tests.
- 4 frontend component tests.
- 1 Playwright end-to-end test.

The in-memory API integration tests use HTTP, so an HTTPS-port detection warning may appear. This is understood and does not represent a failing test.

**Important:** Preserve the existing architecture, database schema, migrations, auditing infrastructure and passing tests.

Before suggesting code changes, inspect the relevant existing files that I provide. Do not assume exact class names, file contents or project structure beyond what has been documented.

## 3. Primary Objective

Implement a complete and secure authentication and role-based authorisation foundation for patrons and managers.

At the end of this milestone, users must be able to register, log in, remain authenticated, access authorised resources and log out.

Managers must have appropriately restricted access to manager-only functionality.

## 4. Authentication Architecture — Decide Before Coding

First, evaluate the best authentication mechanism for this architecture.

Compare:

1. ASP.NET Core Identity with secure HTTP-only authentication cookies.
2. ASP.NET Core Identity with bearer-token authentication.

Consider:

- React SPA compatibility.
- CSRF and XSS risks.
- Cookie security and SameSite behaviour.
- Cross-origin development and production deployment.
- Session expiration.
- Logout and session invalidation.
- Future Android/iOS application support.
- Simplicity and maintainability for one developer.

Recommend a secure default and explain the trade-offs.

Do not introduce Supabase Auth or a second identity system.

Do not invent a custom JWT authentication implementation if an established ASP.NET Core Identity mechanism meets the requirements.

## 5. Required Features

### A. Patron Registration

Implement a registration endpoint accepting:

- First name.
- Email address.
- Mobile number.
- Password.
- Password confirmation, if appropriate for the chosen API contract.

Requirements:

- Validate all submitted fields.
- Enforce email uniqueness.
- Apply secure password policies.
- Normalise and validate email addresses.
- Handle duplicate registration attempts safely.
- Store passwords only through ASP.NET Core Identity's password hashing.
- Assign the `Patron` role through trusted server-side logic.
- Never allow a public registration request to assign the `Manager` role.
- Return appropriate HTTP status codes and safe validation messages.
- Avoid logging passwords or other authentication secrets.

Explain any registration requirements not explicitly defined by the URS as implementation recommendations.

### B. Login

Implement secure login using email and password.

Requirements:

- Verify credentials using ASP.NET Core Identity.
- Establish an authenticated session.
- Support appropriate account lockout and failed-login protections.
- Avoid leaking whether an email address exists.
- Return a safe response containing the information needed by the frontend.
- Ensure password hashes and security-sensitive fields never appear in responses.

### C. Logout

Implement logout so that:

- The current session is invalidated appropriately.
- The frontend clears its authenticated state.
- Protected API endpoints are no longer accessible through the logged-out session.

Explain the limitations of logout for the selected authentication mechanism.

### D. Current User Endpoint

Implement an authenticated endpoint such as:

`GET /api/auth/me`

Return only appropriate information:

- User identifier.
- First name.
- Email.
- Mobile number, where appropriate.
- Authorised roles.

Never expose password hashes, security stamps or internal authentication tokens.

### E. Role-Based Authorisation

Implement and test the existing roles:

- `Patron`
- `Manager`

Requirements:

- Unauthenticated users cannot access protected endpoints.
- Patrons cannot access manager-only endpoints.
- Managers can access appropriately authorised management endpoints.
- Authorisation is enforced on the backend, not only through React route guards.
- Privileged account creation must use a secure, explicit provisioning process.

Do not expose a public manager-registration endpoint.

### F. Session Management

Implement appropriate:

- Session expiration.
- Cookie or token security settings.
- Authentication middleware ordering.
- Secure handling of authentication failures.
- Cross-origin configuration.
- HTTPS enforcement outside the in-memory test environment.

If using cookie authentication, address CSRF protection for state-changing requests and explain how the React client must participate.

### G. React Authentication UI

Create:

- Patron registration page.
- Login page.
- Authenticated-user state management.
- Logout functionality.
- Protected routes.
- Manager-only route protection.
- Loading and error states.
- Accessible form validation.

Follow the existing frontend folder structure and styling conventions.

Avoid storing authentication secrets in browser local storage if a safer approach is available.

### H. Auditing and Security

Integrate authentication with the existing audit infrastructure where appropriate.

Consider recording:

- Successful registration.
- Relevant login events.
- Failed login attempts, without sensitive credentials.
- Logout events.
- Privileged account provisioning.
- Security-relevant account changes.

Do not duplicate existing EF Core audit records unnecessarily.

Ensure authentication-related logs do not disclose passwords, tokens, session cookies or other secrets.

## 6. Recommended Implementation Sequence

Guide me through these steps, one at a time:

**Step 1 — Existing Code Review**

Request and inspect the relevant current files, including Identity configuration, user entity, database context, API startup, dependency injection, CORS configuration, current-user abstraction and frontend API client.

Identify what already exists and what needs to change.

**Step 2 — Authentication Design**

Choose and document the authentication strategy, endpoint contracts, session settings and security controls.

**Step 3 — Identity Configuration**

Configure ASP.NET Core Identity, authentication middleware, password policies, lockout settings and role-based authorisation.

**Step 4 — Registration API**

Implement registration, validation, role assignment and tests.

**Step 5 — Login, Logout and Current User API**

Implement session handling and authenticated-user retrieval.

**Step 6 — Manager Provisioning**

Design a secure development-time manager-account provisioning process without exposing public privilege escalation.

**Step 7 — React Authentication Integration**

Implement forms, API calls, authentication state and protected routes.

**Step 8 — Security and Automated Testing**

Verify authentication, authorisation, session handling, validation and relevant abuse cases.

**Step 9 — Documentation and Sign-Off**

Update documentation, confirm acceptance criteria and prepare a clean Git commit.

## 7. Testing Requirements

Maintain all existing tests.

Add automated tests for at least the following:

- Successful patron registration.
- Invalid registration data.
- Duplicate email registration.
- Password policy enforcement.
- Successful login.
- Invalid login credentials.
- Account lockout behaviour.
- Unauthenticated access to protected endpoints.
- Patron access denied to manager endpoints.
- Manager access permitted to manager endpoints.
- Logout invalidating the current session.
- Current-user endpoint returning only safe fields.
- Public registration being unable to assign privileged roles.
- Relevant CSRF and cookie security behaviour, if cookie authentication is chosen.
- Frontend form validation and authentication flows.

Use xUnit for backend tests, Vitest and Testing Library for frontend component tests, and Playwright for end-to-end flows.

Do not report tests as passing unless they have actually been executed and verified.

## 8. Implementation Standards

For every implementation step:

1. Explain the objective and architectural reasoning.
2. Identify exact files to create or modify.
3. Provide complete, compilable code.
4. Provide PowerShell commands compatible with Windows 11.
5. Explain how to execute and verify the step.
6. Include expected successful results.
7. Address likely errors and security considerations.
8. Wait for my confirmation before continuing.

Additional rules:

- Follow the existing layered architecture.
- Use established ASP.NET Core Identity APIs.
- Prefer clear, maintainable code over unnecessary abstractions.
- Keep database access inside Infrastructure.
- Keep business rules outside controllers.
- Avoid unnecessary database schema changes.
- Review migrations before applying them.
- Never request credentials or secrets in chat.
- Do not break the existing Supabase connection.
- Do not weaken the existing audit or error-handling mechanisms.
- Preserve all 15 passing baseline tests.

## 9. Definition of Done

Milestone 4 is complete only when:

- [ ] Patrons can register successfully.
- [ ] Registration validates required fields and passwords.
- [ ] Duplicate accounts are handled safely.
- [ ] Patrons can log in.
- [ ] Authenticated sessions work correctly.
- [ ] Users can retrieve their own profile information.
- [ ] Users can log out.
- [ ] Protected endpoints reject unauthenticated requests.
- [ ] Manager endpoints reject patron accounts.
- [ ] Manager accounts can be provisioned securely.
- [ ] React registration and login pages work.
- [ ] Frontend route protection works.
- [ ] Authentication security controls are tested.
- [ ] All existing tests continue to pass.
- [ ] New authentication tests pass.
- [ ] Documentation is updated.
- [ ] No secrets have been committed to Git.

## 10. Start Here

Begin with **Step 1: Existing Code Review**.

Ask me to provide the minimum set of existing files needed to understand the current Identity configuration and API architecture.

Do not immediately generate a replacement authentication system or assume that existing Identity infrastructure needs to be rebuilt.

After reviewing the code, recommend the authentication strategy and guide me through implementation incrementally.