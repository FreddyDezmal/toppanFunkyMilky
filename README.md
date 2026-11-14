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

.NET SDK 10.0.4xx (pinned in `global.json`), Node.js 24+ LTS, Git, `dotnet-ef` 10.x (`dotnet tool install --global dotnet-ef`).

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

3. **Frontend configuration:** copy `frontend/.env.example` to `frontend/.env.development.local`.

4. **HTTPS development certificate:** `dotnet dev-certs https --trust`

## Run locally

```powershell
# Terminal 1 - API (https://localhost:7298, docs at /scalar)
dotnet run --project backend/src/Milkshake.Api --launch-profile https

# Terminal 2 - frontend (http://localhost:5173)
cd frontend
npm install
npm run dev
```

## Tests

```powershell
dotnet test Milkshake.slnx          # backend unit + integration tests (no database needed)
cd frontend
npm test                            # component tests
npm run test:e2e                    # end-to-end smoke test (starts API + frontend; needs database secrets)
```

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

## Security notes

- Secrets live in user secrets (development) or environment variables / a secrets store (production), never in Git.
- Frontend `VITE_` variables are public by design: never put secrets in them.
- Every data change is recorded in `milkshake.audit_logs` in the same transaction as the change.