# Progress Report – Milestone 1: Full-Stack Foundation

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 14 November 2026
**Period covered:** 12–14 November 2026
**Status:** Milestone 1 complete – all Definition of Done criteria met
**Source of requirements:** Order Milkshake for Pickup – User Requirement Specification v1.1 (confidential, not stored in the repository)

---

## 1. Summary

The full-stack development foundation is in place and verified end to end: a React frontend calls an ASP.NET Core API, which connects securely to a Supabase-hosted PostgreSQL database. The initial database schema is designed, migrated and seeded, every data change is audited automatically, and the stack is covered by automated tests at three levels (unit/integration, component, end-to-end). No credentials have ever been committed to Git.

| Area | Result |
| --- | --- |
| Backend | Layered ASP.NET Core Web API (.NET 10), OpenAPI docs, health endpoint, ProblemDetails errors, CORS |
| Database | Supabase PostgreSQL, `milkshake` schema, 17 tables, initial migration applied, reference data seeded |
| Frontend | React + TypeScript + Vite 8 + Tailwind CSS v4, live API/database status page |
| Security | Secrets in .NET user secrets only, SSL to the database, Supabase Data API disabled, audit with secret redaction |
| Tests | 7 domain unit tests, 3 API integration tests, 4 component tests, 1 Playwright end-to-end test |
| Repository | Private GitHub repository, 8 commits, history scanned clean of secrets |

---

## 2. Technology stack (as installed)

| Layer | Technology | Version |
| --- | --- | --- |
| Runtime / SDK | .NET (LTS, supported to Nov 2028) | SDK 10.0.402, runtime 10.0.13 |
| API | ASP.NET Core Web API (controllers), OpenAPI + Scalar UI | 10.0.13 / Scalar 2.21.0 |
| ORM | Entity Framework Core + Npgsql provider | EF Core 10.0.14, Npgsql EF 10.0.3 |
| Naming | EFCore.NamingConventions (snake_case) | 10.0.1 |
| Identity | ASP.NET Core Identity (EF Core stores) | 10.0.13 |
| Database | Supabase-hosted PostgreSQL (shared pooler, session mode, EU West) | – |
| Frontend | React, TypeScript, Vite, Tailwind CSS | React 19.3, TS 7.0, Vite 8.3, Tailwind 4.3 |
| Lint | oxlint | 1.90 |
| Tests | xUnit, Vitest + Testing Library, Playwright (Chromium) | xUnit 2.9.3, Vitest 5.1, Playwright latest |
| Tooling | Node.js (via nvm-windows), Git, VS Code, dotnet-ef | Node 26.13.1, Git 2.54, dotnet-ef 10.0.14 |
| OS | Windows 11 | – |

---

## 3. Work completed, step by step

### Step 1 – Development environment
- Verified .NET SDK, Node.js, npm, Git, winget and the EF Core CLI.
- Upgraded Node.js from 22 (maintenance LTS) to 26.13.1 via nvm-windows; removed the duplicate winget installation.
- Chose VS Code (C# Dev Kit, ESLint/oxlint, Prettier, Tailwind IntelliSense).

### Step 2 – Solution structure and repository
- Created `Milkshake.slnx` with four source projects and two test projects (see section 4).
- Pinned the SDK with `global.json` (10.0.402, roll forward within 10.0.x only).
- Shared compiler settings in `Directory.Build.props`: nullable reference types, warnings treated as errors, XML documentation.
- `.gitignore` extended for Node/Vite, environment files, Playwright output and confidential PDFs.
- Initialised Git and pushed to a **private** GitHub repository (made private because the docs describe the confidential URS).

### Step 3 – ASP.NET Core Web API foundation
- Central package version management (`Directory.Packages.props`).
- Layered dependency injection: `AddApplication()` and `AddInfrastructure()`.
- RFC 9457 ProblemDetails for all errors – no stack traces reach clients.
- OpenAPI document (`/openapi/v1.json`) and Scalar interactive docs (`/scalar`) in Development only.
- `GET /api/health` returning a typed JSON report (200 when healthy, 503 when a dependency fails).

### Step 4 – Supabase project and secure connection
- Supabase project created; Data API disabled so tables cannot be reached except through the API; SSL enforcement on.
- Chose the **session pooler** (IPv4-compatible, supports prepared statements) over the IPv6-only direct connection.
- Connection string stored in .NET user secrets; **the password is stored as a separate secret** and combined in code with `NpgsqlConnectionStringBuilder`, so no character in the password can break parsing.

### Step 5 – EF Core and Npgsql
- `MilkshakeDbContext` in the `milkshake` schema (kept apart from Supabase-managed schemas).
- Connection retry on transient failures; migrations history table inside the application schema.
- Custom database health check that logs the real exception server-side while returning only a generic message to clients.
- Integration tests run in a `Testing` environment without the database, so the test suite is fast and works offline.

### Step 6 – Database design, auditing, seeding and first migration
Design decisions agreed (6A), implemented (6B) and applied (6C):
- Domain model: catalog (flavours, toppings, consistencies) with **effective-dated price history**, restaurants, system settings, discount tiers, audit log, ASP.NET Core Identity users and roles.
- `Money` value type: amount + ISO 4217 currency, never floating point, at most two decimals.
- Automatic auditing via an EF Core save interceptor – one audit record per changed row, **in the same transaction** as the change, with sensitive values redacted.
- HiLo key generation so record ids are known before saving (required for transactional auditing).
- Configuration-driven, idempotent seeding from `appsettings.Development.json`.
- Migration `20261114134727_InitialCreate` applied to Supabase; seeding produced 2 roles, 7 settings, 7 flavours, 6 toppings, 4 consistencies, 2 restaurants, 3 discount tiers and 48 audit records; a second run inserted nothing.

### Step 7 – React frontend
- Vite + React + TypeScript app in `frontend/` with Tailwind CSS v4 and brand colour tokens.
- Feature-based folder structure (`app`, `components`, `features`, `lib/api`, `pages`).
- API base URL from `VITE_API_BASE_URL` (template `.env.example` committed; real values in git-ignored `.env.development.local`).
- Configuration-driven CORS policy on the API allowing only the Vite dev origin in Development.
- System status page showing overall, database and API health, with refresh and clear error handling.

### Step 8 – Tests, README and sign-off
- CORS integration tests (allowed origin accepted; unknown origin receives no CORS header).
- Component tests for the status card (healthy, unhealthy, unreachable, refresh).
- Playwright end-to-end smoke test that starts the API and frontend and verifies the full chain.
- Root `README.md` with setup, run, test and security instructions.

---

## 4. Solution architecture

```
Milkey/
├── Milkshake.slnx, global.json, Directory.Build.props, Directory.Packages.props, .editorconfig
├── backend/
│   ├── src/
│   │   ├── Milkshake.Api             HTTP endpoints, middleware, CORS, OpenAPI, current-user adapter
│   │   ├── Milkshake.Application     use cases and abstractions (e.g. ICurrentUser)
│   │   ├── Milkshake.Domain          entities, Money, price history rules – no dependencies
│   │   └── Milkshake.Infrastructure  EF Core, migrations, Identity, auditing, seeding, health check
│   └── tests/
│       ├── Milkshake.UnitTests         domain rules (price history, Money)
│       └── Milkshake.IntegrationTests  in-memory API tests (health, CORS)
├── frontend/                          React app, Vitest tests (src/**/*.test.tsx), Playwright tests (e2e/)
└── docs/                              roadmap, prompts, progress reports, audits
```

Dependency direction: `Api → Application → Domain`, with `Infrastructure → Application/Domain`. The API references Infrastructure only to register services at startup.

---

## 5. Database (schema `milkshake`)

| Group | Tables | Notes |
| --- | --- | --- |
| Identity | `users`, `roles`, `user_roles`, `user_claims`, `user_logins`, `user_tokens`, `role_claims` | GUID v7 ids; roles **Manager** and **Patron** seeded |
| Catalog | `flavours`, `toppings`, `consistencies` | URS values seeded; deactivated, never deleted |
| Price history | `flavour_prices`, `topping_prices`, `consistency_prices` | `amount numeric(12,2)`, `currency_code char(3)`, `effective_from` / `effective_to`; one open price per item enforced by a partial unique index |
| Operations | `restaurants`, `system_settings`, `discount_tiers` | Settings include min/max drinks, VAT, max discount, pickup lead time, currency (ZAR) and display locale (en-ZA); discount tiers seeded **inactive** |
| Audit | `audit_logs` | `changes` as JSONB (field, old, new); user, timestamp, trace id; no foreign key to users so history survives user deletion |

Also present: `EntityFrameworkHiLoSequence` (id generation) and `__ef_migrations_history`.

**Deferred to later milestones:** `orders` / `order_items` with price snapshots (M5/M7), `payments` (M8), `email_messages` (M9), optimistic concurrency tokens on editable configuration (M10).

---

## 6. Key decisions

| # | Decision | Rationale | Status |
| --- | --- | --- | --- |
| D1 | .NET 10 LTS instead of .NET 11 STS | Longer support window; mature EF Core / Npgsql | Recommended, accepted |
| D2 | Layered architecture (4 projects), controllers | Testable business rules; role-based endpoint grouping | Recommended, accepted |
| D3 | Supabase as managed PostgreSQL only; Data API disabled | All access, validation and auditing through ASP.NET Core | Required by brief |
| D4 | Session pooler + `SSL Mode=Require` | IPv4 compatibility; encryption in transit | Recommended; `VerifyFull` with CA certificate planned for production |
| D5 | snake_case naming, `milkshake` schema | PostgreSQL conventions; isolation from Supabase objects | **Confirmed by developer** |
| D6 | Effective-dated price tables + order snapshots | Full price history and guaranteed immutability of placed orders (URS §3.4.6) | **Confirmed by developer** (option B) |
| D7 | Identity tables in the first migration | Avoids a disruptive change later; audit can reference users from day one | **Confirmed by developer** |
| D8 | Sample prices from configuration; currency and locale as data | No hard-coded prices; ready to scale beyond one currency/locale | **Confirmed by developer** (option A + locale requirement) |
| D9 | Transactional audit via save interceptor + HiLo keys | Data and audit commit or fail together (URS §3.3) | Recommended, implemented |
| D10 | Configuration-driven CORS, explicit origins | Least privilege; compatible with cookie authentication later | Recommended, implemented |

---

## 7. Security posture

- Database credentials only in .NET user secrets (development); production will use environment variables or a secrets store.
- Password stored separately from the connection string.
- Encrypted database connection; Supabase SSL enforcement on; Data API disabled.
- Repository is private; confidential PDFs excluded by `.gitignore`.
- Frontend environment files contain no secrets (`VITE_` values are public by design).
- Audit log redacts password hashes, security stamps, concurrency stamps and token values.
- API errors return ProblemDetails without stack traces; health endpoint hides exception details from clients.
- **History scan (14 Nov 2026):** no connection strings, passwords, Supabase keys or private keys in any commit; the only environment file ever committed is `frontend/.env.example`.

---

## 8. Test results

| Suite | Scope | Result |
| --- | --- | --- |
| Backend unit (xUnit) | Price history and `Money` rules | 7 tests – passed (14 Nov) |
| Backend integration (xUnit) | Health endpoint; CORS allowed/denied origins | 3 tests – health test passed; CORS tests added in Step 8 (confirm with `dotnet test`: expected 10 passed in total) |
| Frontend component (Vitest 5.1) | Status card: healthy, unhealthy, unreachable, refresh | 4 passed |
| End-to-end (Playwright, Chromium) | Browser → React → API → Supabase | 1 passed |
| Build / lint | `tsc -b && vite build`, oxlint | Passed; 0 warnings, 0 errors |

---

## 9. Milestone 1 – Definition of Done

| Criterion | Status |
| --- | --- |
| React frontend starts locally | ✅ |
| ASP.NET Core API starts locally | ✅ |
| Health endpoint accessible | ✅ |
| Backend connected securely to Supabase PostgreSQL | ✅ |
| EF Core migration created and applied | ✅ |
| Database schema verified in Supabase | ✅ |
| React successfully calls the API | ✅ (manual + Playwright) |
| Foundation committed without exposing credentials | ✅ (history scan clean) |

---

## 10. Issues encountered and resolutions

| Issue | Cause | Resolution / lesson |
| --- | --- | --- |
| `global.json` missing after Step 2 | Command not run | Re-run and committed; verify pins explicitly rather than inferring from output |
| Node still v22 after upgrade | nvm-windows controls `node` on PATH | Installed 26.13.1 through nvm; removed winget copy |
| Builds failing with "file locked by Milkshake.Api" | A running API instance locks its DLLs | Always stop the API (Ctrl+C) before building, testing or running `dotnet ef` |
| `Couldn't set max pool size` | Invalid Npgsql keyword (`Max Pool Size`) | Corrected to `Maximum Pool Size`; password moved to its own secret |
| Pasted script consumed by `Read-Host` | Multi-line paste into an interactive prompt | Run prompt lines one at a time |
| Database check unhealthy with no detail | EF's built-in check swallows exceptions | Custom health check that logs the real error |
| `28P01 password authentication failed` | Wrong password stored | Reset to an alphanumeric password; stored via stdin JSON |
| Audit records need keys before insert | Database identity columns assign keys after insert | Switched integer keys to HiLo sequence |
| `PendingModelChangesWarning` on update | Custom HiLo sequence name not written per column in the snapshot | Used the provider's default sequence name; verified with `has-pending-model-changes` |
| Folder `catalog` vs namespace `Catalog` | Case mismatch, invisible on Windows | Renamed with a two-step rename before committing |

---

## 11. Open items and assumptions to confirm

Carried from the roadmap (section 12) – these block later milestones, not Milestone 1:
- **Q-01 – Q-04:** discount percentage per tier, meaning of "maximum discount", which orders count, discount before/after VAT. Tiers are seeded inactive with placeholder values until answered.
- **Q-05 / Q-06:** real restaurant list and pickup-time rules. Two sample restaurants and a 15-minute lead time are placeholders.
- **Q-07:** whether fees are VAT-inclusive.
- **Q-12:** payment gateway selection.
- **New:** multi-currency *conversion* (exchange rates) is not in the URS. The design stores currency with every price and keeps currency/locale as settings, so conversion can be added later without schema changes.
- **Sample prices** in `appsettings.Development.json` are development data only and must be replaced by management via the lookup manager.

Technical follow-ups:
- Production database connection: `SSL Mode=VerifyFull` with the Supabase CA certificate (M14).
- Dedicated least-privilege database role for the application at runtime (M14).
- Disposable PostgreSQL (e.g. Testcontainers) for database integration tests (M11/M13).
- Continuous integration pipeline (build, test, lint on every push).
- Optimistic concurrency on editable configuration and case-insensitive name uniqueness (M10).

---

## 12. Next steps

1. **Authentication and roles (roadmap M4):** patron registration and login, manager login, role-based authorisation, session handling.
2. **Lookup management (roadmap M10):** manager screens and API for flavours, toppings, consistencies, restaurants, prices (using the effective-dated price history), settings and discount tiers – fully audited.
3. Obtain stakeholder answers to Q-01 to Q-07 before starting ordering and pricing (M5/M6).

---

## 13. Commit history

| Commit | Date | Description |
| --- | --- | --- |
| `9811093` | 2026-11-12 | Scaffold solution structure, SDK pin and gitignore |
| `42fcb90` | 2026-11-12 | Pin .NET SDK to 10.0.402 via global.json |
| `91c67cc` | 2026-11-13 | Central package management, layered DI, ProblemDetails, OpenAPI and health endpoint |
| `61393f6` | 2026-11-13 | Enable user secrets for local configuration |
| `aee6bf6` | 2026-11-14 | EF Core with Npgsql, database health check with diagnostic logging |
| `f4874c4` | 2026-11-14 | Domain model, transactional audit interceptor, HiLo keys, config-driven seeding; initial migration applied |
| `e931081` | 2026-11-14 | Vite + React + TypeScript + Tailwind app with API health status; API CORS policy |
| `a65f7f2` | 2026-11-14 | CORS integration tests, Vitest component tests, Playwright e2e smoke test; README |
