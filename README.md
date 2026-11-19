# Order Milkshake for Pickup

Online milkshake ordering for **Milky Shaky Custom Drinks**: patrons order and pay online and collect at a restaurant;
managers maintain prices, lookups and discounts, and review reports and audit history.

> Requirements: *Order Milkshake for Pickup – User Requirement Specification v1.1* (confidential; not stored in this repository).
> Roadmap: [`docs/`](docs/).

## Stack

| Layer | Technology |
| --- | --- |
| Frontend | React, TypeScript, Vite, Tailwind CSS (`frontend/`) |
| API | ASP.NET Core Web API, .NET 10 (`backend/src/Milkshake.Api`) |
| Data | Supabase-hosted PostgreSQL via EF Core 10 + Npgsql (schema `milkshake`) |
| Tests | xUnit, Vitest + Testing Library, Playwright |

## Prerequisites

.NET SDK 10.0.4xx (pinned in `global.json`), Node.js 24+ LTS, Git, `dotnet-ef` 10.x (`dotnet tool install --global dotnet-ef`),
and **Docker Desktop** (backend integration tests run against a throwaway PostgreSQL container).

## First-time setup

1. **Database secrets** (stored outside the repository with .NET user secrets; never commit them):

   ```powershell
   dotnet user-secrets set "ConnectionStrings:MilkshakeDb" "Host=<pooler-host>;Port=5432;Database=postgres;Username=postgres.<project-ref>;SSL Mode=Require;Timeout=15;Command Timeout=30;Maximum Pool Size=10;Application Name=milkshake-api-dev" --project backend/src/Milkshake.Api
   ```

   Then store the password separately (prompted, hidden):

   ```powershell
   $securePw = Read-Host "Database password" -AsSecureString
   $pw = [System.Net.NetworkCredential]::new('', $securePw).Password
   @{ Database = @{ Password = $pw } } | ConvertTo-Json | dotnet user-secrets set --project backend/src/Milkshake.Api
   Remove-Variable securePw, pw
   ```

2. **Database schema and reference data:**

   ```powershell
   dotnet ef database update --project backend/src/Milkshake.Infrastructure --startup-project backend/src/Milkshake.Api
   ```

3. **Frontend configuration:** copy `frontend/.env.example` to `frontend/.env.development.local` and leave
   `VITE_API_BASE_URL` empty. The app then calls `/api` on its own origin and Vite forwards it to the API
   (required for the `SameSite=Strict` session cookie).

4. **HTTPS development certificate:** `dotnet dev-certs https --trust`

5. **A manager account** (there is deliberately no sign-up page or endpoint for managers). With the API stopped:

   ```powershell
   dotnet run --project backend/src/Milkshake.Api --launch-profile https -- provision-manager --email <email> --first-name <name>
   ```

   The password is entered twice at a hidden prompt (or, for unattended set-up only, read from the
   `MILKSHAKE_MANAGER_PASSWORD` environment variable). Running it for an existing account adds the Manager role
   without changing its password. In production: `dotnet Milkshake.Api.dll provision-manager ...` on the server.

## Run locally

```powershell
# Terminal 1 - API (https://localhost:7298, docs at /scalar)
dotnet run --project backend/src/Milkshake.Api --launch-profile https

# Terminal 2 - frontend (http://localhost:5173; /api is proxied to the API)
cd frontend
npm install
npm run dev
```

## Tests

```powershell
dotnet test Milkshake.slnx          # backend unit + integration tests (Docker must be running; Supabase is never touched)
cd frontend
npm test                            # component tests (Vitest)
npm run test:e2e                    # end-to-end tests against the real stack (needs database secrets;
                                    # creates e2e-...@example.test patrons per run in the dev database)
```

The end-to-end **manager journey** (and its accessibility scans) runs only when manager credentials are set in the
current terminal; otherwise it is skipped. Never store them in a file:

```powershell
$env:E2E_MANAGER_EMAIL = '<manager email>'
$secure = Read-Host 'Manager password' -AsSecureString
$env:E2E_MANAGER_PASSWORD = [System.Net.NetworkCredential]::new('', $secure).Password
npm run test:e2e
Remove-Item Env:E2E_MANAGER_EMAIL, Env:E2E_MANAGER_PASSWORD; Remove-Variable secure
```

Each manager run adds one deactivated `E2E Flavour <timestamp>` to the development database (nothing is deleted).

## Repository layout

```
backend/src/Milkshake.Api             HTTP endpoints, middleware, configuration
backend/src/Milkshake.Application     use cases and service interfaces
backend/src/Milkshake.Domain          entities and business rules (no dependencies)
backend/src/Milkshake.Infrastructure  EF Core, migrations, auditing, seeding, external services
backend/tests/                        xUnit unit and integration tests
frontend/                             React application
docs/                                 roadmap and working notes
```

## Authentication

| Endpoint | Access | Purpose |
| --- | --- | --- |
| `GET /api/auth/csrf` | anyone | anti-forgery token for state-changing requests |
| `POST /api/auth/register` | anyone | creates a **Patron** and signs them in (role assigned by the server) |
| `POST /api/auth/login` | anyone | email + password; sets the session cookie |
| `POST /api/auth/logout` | signed in | ends the session and invalidates the user's other sessions |
| `GET /api/auth/me` | signed in | id, first name, email, mobile number, roles |
| `GET /api/manager/ping` | Manager | role-enforcement probe |

- Session: ASP.NET Core Identity cookie `milkshake.session` (HttpOnly, Secure, SameSite=Strict, 2-hour sliding expiry).
  Nothing is stored in browser storage; React asks `/api/auth/me` who is signed in.
- Every POST/PUT/PATCH/DELETE needs the `X-XSRF-TOKEN` header; the frontend HTTP client handles this.
- Endpoints require a signed-in user unless explicitly marked `[AllowAnonymous]`.
- 5 wrong passwords lock an account for 15 minutes; every sign-in failure returns the same message.
- Login and registration are limited to 10 requests per minute per IP (`RateLimiting:AuthPermitLimit`).
- Sign-ins, failures, lockouts, logouts and manager provisioning are recorded in `milkshake.security_events`.

## Management (Milestone 10)

Managers use **Management** in the app (`/manager`): flavours, toppings, consistencies, their prices, restaurants,
settings and discount tiers. All endpoints are under `/api/manager` and require the **Manager** role.

| Area | Endpoints |
| --- | --- |
| Catalogue (`{kind}` = `flavours`, `toppings`, `consistencies`) | `GET/POST /catalog/{kind}`, `GET/PUT /catalog/{kind}/{id}`, `POST /catalog/{kind}/{id}/deactivate` · `/activate` |
| Prices | `GET/POST /catalog/{kind}/{id}/prices`, `POST /catalog/{kind}/{id}/prices/{priceId}/cancel` |
| Restaurants | `GET/POST /restaurants`, `GET/PUT /restaurants/{id}`, `POST /restaurants/{id}/deactivate` · `/activate` |
| Settings | `GET /settings`, `PUT /settings/{key}` |
| Discount tiers | `GET /discount-tiers`, `PUT /discount-tiers/{tierNumber}`, `POST /discount-tiers/{tierNumber}/activate` |

- **Nothing is deleted:** records are deactivated (hidden from new orders, kept for history).
- **Prices are effective-dated:** a new price starts now or later; prices that have taken effect are never edited;
  the latest future price can be cancelled before it starts.
- **Concurrency:** every response includes `version`; send it back with a change. If someone else saved first,
  the API answers **409** and the UI offers a reload.
- **Setting limits:** minimum drinks ≥ 1 and ≤ maximum; maximum drinks ≤ 50; VAT 0–1 (fraction); lead time
  0–240 minutes; currency is read-only.
- **Discount tiers** can be edited but **not activated** until the discount rules are confirmed (open questions Q-01 to Q-04).
- Errors are RFC 9457 ProblemDetails (`application/problem+json`); validation errors list messages per field.

## Security notes

- Secrets live in user secrets (development) or environment variables / a secrets store (production), never in Git.
- Frontend `VITE_` variables are public by design: never put secrets in them.
- Every data change is recorded in `milkshake.audit_logs` in the same transaction as the change; password hashes,
  security stamps and tokens are stored only as `***`.
- Passwords are never logged (verified by an automated test that searches every log line).
- Management changes are version-checked (optimistic concurrency) and audited field by field with the manager's identity.
- Check that the built frontend contains no server details:
  `Select-String -CaseSensitive -Path frontend/dist/assets/*.js -Pattern 'Host=|Database=|Username=|supabase\.co|pooler'`
  (no output expected).