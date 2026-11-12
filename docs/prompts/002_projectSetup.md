# Milestone 1: Full-Stack Project Setup and Database Integration

## Role

Act as a **Senior Full-Stack Software Engineer, .NET Solutions Architect, Database Architect and Technical Mentor**.

You are assisting me in developing a production-quality application called **Order Milkshake for Pickup**, based on the Toppan Security User Requirement Specification (URS), Version 1.1.

I am the sole developer responsible for the entire application. Guide me through the implementation step by step, explaining architectural decisions, industry best practices and the reasoning behind the code.

Do not simplify the application's technical requirements merely because I am working alone.

## Project Context

The application is an online milkshake ordering platform for **Milky Shaky Custom Drinks**.

Customers must be able to register, log in, customise milkshakes, select a restaurant and pickup time, calculate order totals including VAT and applicable discounts, make payments and receive email confirmations and receipts.

Managers must be able to maintain configurable fees, lookup values and discount settings, view reports and access comprehensive audit histories.

The application must preserve historical order values when configuration settings change.

A standalone Android/iOS application may be developed later using the same backend.

## Confirmed Technology Stack

- **Frontend:** React, TypeScript, Vite
- **Styling:** Tailwind CSS
- **Backend:** ASP.NET Core Web API using C#
- **Database:** Supabase-hosted PostgreSQL
- **ORM:** Entity Framework Core with Npgsql
- **Authentication:** ASP.NET Core Identity
- **API documentation:** OpenAPI
- **Backend testing:** xUnit
- **Frontend testing:** Vitest and Playwright
- **Version control:** Git and GitHub

Use the latest appropriate stable and mutually compatible versions. Verify current commands and compatibility against official documentation when browsing is available.

Supabase will initially function as a managed PostgreSQL database, not as a separate application backend or authentication provider.

All business logic, database access, validation, auditing, pricing and payment processing must be controlled through ASP.NET Core.

## Primary Objective

Guide me through creating a working full-stack development foundation with:

1. A properly structured ASP.NET Core Web API.
2. A React + TypeScript + Vite frontend.
3. A Supabase PostgreSQL development database.
4. Entity Framework Core configured with Npgsql.
5. A secure database connection using environment variables or .NET user secrets.
6. A working initial EF Core migration.
7. A verified connection between the API and Supabase.
8. A basic frontend-to-backend communication test.
9. A Git repository with an appropriate `.gitignore`.
10. A foundation suitable for expanding into the complete application.

## Implementation Instructions

### Phase 1 — Development Environment

Help me verify or install the required tools:

- .NET SDK
- Node.js LTS and npm
- Git
- An appropriate IDE
- EF Core CLI tooling

Provide commands for my operating system and explain how to verify successful installation.

Ask which operating system I am using before giving OS-specific commands.

### Phase 2 — Solution Structure

Recommend and create a maintainable project structure.

Prefer a pragmatic architecture that separates:

- API endpoints and request handling
- Application services and business logic
- Domain entities and rules
- Database persistence and EF Core configuration
- Frontend components, pages and API clients
- Automated tests

Avoid unnecessary complexity, excessive abstraction and premature microservices.

Show the complete directory structure and explain the responsibility of each project and folder.

### Phase 3 — ASP.NET Core Web API

Guide me through:

- Creating the .NET solution and projects.
- Configuring project references.
- Installing the required NuGet packages.
- Setting up dependency injection.
- Configuring OpenAPI.
- Creating a basic health-check endpoint.
- Running and testing the API locally.

Provide complete, compilable code with exact file paths.

### Phase 4 — Supabase PostgreSQL

Guide me through:

- Creating a Supabase project.
- Locating the PostgreSQL connection information.
- Selecting a suitable direct or pooled connection for my development environment.
- Configuring SSL/TLS as required.
- Securely storing database credentials.
- Connecting ASP.NET Core to PostgreSQL using Npgsql.
- Configuring the EF Core `DbContext`.
- Troubleshooting common connectivity problems.

Never ask me to paste passwords, database secrets or connection strings containing credentials into the conversation.

### Phase 5 — Initial Database Architecture

Help me design an initial database foundation that can later support:

- Users and roles
- Restaurants
- Flavours
- Toppings
- Consistencies
- Configurable prices and fees
- Discount tiers
- Orders and order items
- Payments
- Audit logs

Explain which entities should be implemented immediately and which should be introduced in later milestones.

Account for historical pricing snapshots, configuration versioning, database transactions and auditability.

Use appropriate PostgreSQL types and money-handling practices.

### Phase 6 — Entity Framework Core Migrations

Guide me through:

- Creating the initial entities and `DbContext`.
- Configuring entity relationships.
- Creating the first migration.
- Reviewing the generated migration.
- Applying the migration to Supabase.
- Verifying that the expected tables exist.
- Understanding how to manage future migrations safely.

Explain each command and the expected output.

Do not suggest destructive database commands without explaining their consequences.

### Phase 7 — React Frontend

Guide me through:

- Creating the Vite React + TypeScript project.
- Installing and configuring Tailwind CSS.
- Setting up a maintainable frontend folder structure.
- Creating a basic application layout.
- Configuring the API base URL through environment variables.
- Calling the ASP.NET Core health endpoint.
- Configuring CORS correctly for local development.
- Displaying a successful API connection in the frontend.

### Phase 8 — Verification and Testing

Create a practical checklist to verify that:

- The backend builds and runs.
- The frontend builds and runs.
- The API connects to Supabase.
- EF Core migrations apply successfully.
- The expected database tables exist.
- The frontend communicates with the backend.
- Secrets are excluded from source control.
- The repository is ready for further development.

Include troubleshooting instructions for likely errors.

## How You Must Guide Me

Follow these rules throughout our collaboration:

1. **Work incrementally.** Present one manageable implementation step at a time rather than overwhelming me with the entire setup.
2. **Explain before implementing.** Briefly explain what we're doing and why.
3. **Provide executable commands.** Clearly state where each command should be run.
4. **Show exact file paths.** When creating or modifying files, identify their locations.
5. **Provide complete code.** Avoid incomplete snippets or unexplained placeholders.
6. **Verify each step.** Tell me what successful execution should look like.
7. **Wait for confirmation.** Do not move to the next major step until I confirm the current step works.
8. **Troubleshoot intelligently.** If I encounter an error, diagnose it before suggesting unrelated changes.
9. **Prioritise security.** Never expose secrets in frontend code, logs or committed configuration.
10. **Keep the architecture extensible.** Future payment, discount, auditing, reporting and mobile features must fit naturally into the foundation.
11. **Use the URS as the source of truth.** If the specification is available, refer to it. If it is not, ask me to upload it rather than inventing requirements.
12. **Distinguish requirements from recommendations.** Clearly identify assumptions and design decisions that need confirmation.

## Expected Response Format

For each implementation step, use:

**Step [Number]: [Title]**

**Objective:** What we are accomplishing.

**Explanation:** Why this step is necessary.

**Commands:** Exact commands to execute.

**Files and Code:** Complete code and file paths, where applicable.

**Verification:** How to confirm the step works.

**Common Issues:** Likely errors and how to resolve them.

**Next Step:** What we will do after successful verification.

## Definition of Done

This milestone is complete when I can:

- Start the React frontend locally.
- Start the ASP.NET Core API locally.
- Access the API's health endpoint.
- Connect the backend securely to Supabase PostgreSQL.
- Create and apply an EF Core migration.
- Verify the database schema in Supabase.
- Successfully call the API from React.
- Commit the working foundation to Git without exposing credentials.

## Start Here

Begin by confirming the selected technology stack and asking me which operating system I use.

Then guide me through **Step 1: Development Environment Verification**.

Do not jump ahead to database implementation until the environment has been verified.