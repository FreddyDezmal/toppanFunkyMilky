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
- Login and registration are limited to 10 requests per minute per IP (`RateLimiting:AuthPermitLimit`); a full
  end-to-end run uses most of that, so wait a minute before rerunning if you see "Too many attempts".
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

## Ordering (Milestone 5)

Patrons use **Order** in the app (`/order`): choose how many milkshakes, give each one a flavour, consistency and
topping, choose a restaurant and pickup time, review the server-checked draft, and place the order (Milestone 7).
Placed orders are stored **unpaid**; payment arrives in Milestone 8.

| Endpoint | Access | Purpose |
| --- | --- | --- |
| `GET /api/ordering/options` | Patron | Active, priced flavours, consistencies and toppings with current fees; active restaurants; drink limits; pickup window |
| `POST /api/ordering/draft/validate` | Patron + CSRF | Checks a draft against what is in force now; returns the summary with estimated prices, or errors per field; stores nothing |
| `POST /api/ordering/quote` | Patron + CSRF | Live, non-binding price estimate for the milkshakes being chosen (identifiers only); stores nothing |

- **The server decides:** the browser sends identifiers and a pickup time only. A price or any unknown field in the
  request is rejected. Every price, VAT amount and total is calculated by the server (see Pricing below).
- **Draft storage:** the browser tab's `sessionStorage`, identifiers only, per user, cleared on sign-out.
- **Provisional pickup rules** (Q-05, Q-06, Q-17, Q-18): times in the `Pickup.TimeZone` setting (`Africa/Johannesburg`);
  earliest = now + `Pickup.MinLeadTimeMinutes`; latest = 23:59 local, `Pickup.MaxAdvanceDays` (7) days ahead; whole minutes.
- **Limits:** 60 ordering requests per minute per patron (`RateLimiting:OrderingPermitLimit`); draft bodies up to 16 KB.
- Managers cannot order (patrons only, Q-19). If the ordering settings are invalid, patrons see "Ordering is
  temporarily unavailable" and the reason is logged on the server.

## Pricing, VAT and discounts (Milestone 6)

- **One server-side engine** (`Milkshake.Domain/Pricing/PriceCalculator`, used through `IOrderPricingService`) calculates
  every amount; React only formats what the API returns. Requests containing a price, VAT rate, discount or total are rejected.
- **Drink price** = flavour + consistency + topping fee, read at one pricing instant per request (effective-dated fees).
- **VAT** is added to the VAT-exclusive subtotal at the `Tax.VatRate` setting (provisional decision P1, Q-07). VAT and
  discount are each rounded once per order to 2 decimals, half away from zero.
- **Frequent-customer discount** is applied before VAT and capped at the maximum discount, but is **switched off**
  (`DiscountPolicy.RulesConfirmed = false`) until Q-01 to Q-04 are answered and order history exists (M7); patrons see 0 "(none)".
- Prices on screen are **estimates**; the final amount is calculated when the order is placed (M7).
- Details, decisions and open questions: `docs/progress/2026-11-19_milestone-6-pricing-vat-discounts-report.md`.
  Stakeholder calculation sheet (AC-6.6): `docs/pricing/m6-calculation-sign-off.csv`.

## Orders (Milestone 7)

| Endpoint | Access | Purpose |
| --- | --- | --- |
| `POST /api/ordering/orders` | Patron + CSRF + `Idempotency-Key` header | Revalidates and reprices the draft on the server and stores the order unpaid: 201 created, 200 retry of the same submission, 400 invalid, 409 price changed (`currentTotals`), 422 key reused for different content |
| `GET /api/ordering/orders/{reference}` | Patron | One of your own orders exactly as stored; any other reference is 404 |
| `GET /api/ordering/orders` | Patron | Your 20 most recent orders, newest first |

- **Tables:** `milkshake.orders` and `milkshake.order_items` (migration `AddOrders`). Every name, fee, VAT rate, discount and
  total is a **snapshot** taken at placement; check constraints enforce the arithmetic; foreign keys never cascade.
- **Atomic:** the order, its items and their audit rows are written by one `SaveChanges` call (one transaction).
- **No duplicates:** a unique index on (patron, idempotency key) guarantees one order per submission, including for
  simultaneous requests and retries after a lost response. The browser keeps one key per draft content in `sessionStorage`.
- **Price confirmation:** the browser sends the total the patron confirmed; it is only compared. If the server's price
  differs, nothing is stored and the patron confirms the new total.
- **Status:** new orders are `PendingPayment`. Allowed changes: PendingPayment → Confirmed / PaymentFailed / Cancelled;
  PaymentFailed → Confirmed / Cancelled. In M7 nothing changes status: payment (M8) and cancellation (Q-10) are not built.
- Orders have a public reference such as `MS-7KQ2-X9TD`; patrons see `/orders/<reference>` after placing an order.
- Report: `docs/progress/2026-11-20_milestone-7-order-processing-report.md`.

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