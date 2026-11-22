# Order Milkshake for Pickup

Online milkshake ordering for **Milky Shaky Custom Drinks**: patrons order and pay online and collect at a restaurant;
managers maintain prices, lookups and discounts, and review reports and audit history.

> Requirements: *Order Milkshake for Pickup – User Requirement Specification v1.1* (confidential; not stored in this repository).
> Roadmap: [`docs/`](docs/).

**Live demonstration:** https://milkyshaky.onrender.com (release v1.0.2; Paystack **test mode**, no real money; free
hosting sleeps after 15 minutes idle, so the first page can take about a minute).

## Documentation

| For | Read |
| --- | --- |
| Patrons and managers | [`docs/guides/patron-guide.md`](docs/guides/patron-guide.md), [`docs/guides/manager-guide.md`](docs/guides/manager-guide.md) |
| Running the deployment | [`docs/guides/operations-guide.md`](docs/guides/operations-guide.md), [`docs/deployment/configuration-reference.md`](docs/deployment/configuration-reference.md) |
| UAT | [`docs/uat/`](docs/uat/) – start with the tester pack |
| What is in a release | [`docs/releases/`](docs/releases/) |
| Decisions | [`docs/decisions/`](docs/decisions/) |
| Progress and evidence | [`docs/progress/`](docs/progress/), [`docs/testing/`](docs/testing/) |

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

4. **Paystack test key** (payments, Milestone 8). Use a Paystack account in Test mode; store only the secret key,
   at a hidden prompt (details: [`docs/payments/paystack-setup-guide.md`](docs/payments/paystack-setup-guide.md)):

   ```powershell
   $secure = Read-Host 'Paystack secret key' -AsSecureString
   $key = [System.Net.NetworkCredential]::new('', $secure).Password
   dotnet user-secrets set "Payments:Paystack:SecretKey" $key --project backend/src/Milkshake.Api | Out-Null
   Remove-Variable secure, key
   ```

   Without it everything else works, but **Pay now** answers "Payments are temporarily unavailable".

5. **HTTPS development certificate:** `dotnet dev-certs https --trust`

6. **A manager account** (there is deliberately no sign-up page or endpoint for managers). With the API stopped:

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

**Self-contained end-to-end stack** (Docker; disposable database, mail capture and Paystack simulator; nothing
external is touched):

```powershell
cd frontend
npm run test:e2e:local                               # API + vite preview
npm run test:e2e:local -- --container                # the production image behind a local TLS edge proxy
npm run test:e2e:local -- --container --backup-drill # then back up and restore-test the run's database
npm run test:e2e:local -- --perf --zap               # k6 load test and ZAP scans against the local stack
```

CI (`.github/workflows/ci.yml`) runs the backend and frontend suites, vulnerability audits, a secret scan of the whole
history and an image content check on every push to `main`.

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
Placed orders are stored **unpaid** until the patron pays online (Milestone 8).

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
  PaymentFailed → Confirmed / Cancelled; Confirmed → Cancelled (with a refund, M8b). Only a payment verified with
  Paystack confirms an order; cancellation is allowed before the pickup time only.
- Orders have a public reference such as `MS-7KQ2-X9TD`; patrons see `/orders/<reference>` after placing an order.
- Report: `docs/progress/2026-11-20_milestone-7-order-processing-report.md`.

## Payments (Milestone 8)

Patrons pay on the order page (`/orders/<reference>`) through **Paystack's hosted checkout** (test mode in
development). An order becomes **Confirmed** only after the server verifies the payment with Paystack.

| Endpoint | Access | Purpose |
| --- | --- | --- |
| `POST /api/ordering/orders/{reference}/payments` | Patron + CSRF + `Idempotency-Key` header, body `{}` | Opens (or resumes) a checkout for the stored order total: 200 `{ paymentReference, checkoutUrl }`, 404, 409 already paid / not payable / being prepared, 422 pickup passed or key used, 503 payments unavailable |
| `POST /api/ordering/orders/{reference}/payments/refresh` | Patron + CSRF | Checks the open attempt with Paystack and returns `orderStatus`, `isPaid`, `awaitsPayment`, `checking` and the latest attempt |
| `POST /api/payments/paystack/webhook` | Paystack only (HMAC-SHA512 signature; no session or CSRF) | Verified, deduplicated payment notifications |

- **Amount:** always the stored order total, sent to Paystack in cents (ZAR). The browser sends no amount.
- **Verification:** the webhook, the patron's return and a background job (every 5 minutes) all ask Paystack's
  Verify Transaction API; the return address and the webhook's own JSON are never trusted.
- **Tables:** `milkshake.payments` (one row per attempt) and `milkshake.payment_events` (verified webhook receipts),
  migration `AddPayments`. One successful and at most one open payment per order (database-enforced).
- **Locally** Paystack cannot reach the webhook; payments are confirmed by the return check and the background job.
- **Cancellation and refunds (M8b):** patrons cancel before pickup – free if unpaid; if paid they get back what they
  paid less `Refund.FeeRate` (10%) of the VAT-exclusive amount. Managers ask patrons to cancel (Management → Orders,
  reason ≤ 50 characters); a patron who accepts is refunded in full. Late or duplicate payments are refunded in full
  automatically. Refunds go through Paystack (`MSR-…` references) and are complete only when Paystack confirms them;
  failed ones are listed for managers. Report: `docs/progress/2026-11-20_milestone-8b-cancellation-refunds-report.md`.
- **Tests** never call Paystack: a simulated provider and test-signed webhooks are used. A manual sandbox payment
  with a Paystack test card is described in the setup guide.
- Docs: [`docs/payments/payment-architecture.md`](docs/payments/payment-architecture.md),
  [`docs/payments/paystack-setup-guide.md`](docs/payments/paystack-setup-guide.md),
  report `docs/progress/2026-11-20_milestone-8-payments-report.md`.

## Emails (Milestone 9)

Patrons get an **order confirmation with the final receipt** when their payment is verified, and emails when a
manager asks to cancel, when an order is cancelled, and when a refund completes or fails. All go to the address on the
patron's account.

- **Reliable by design:** each email is queued in the same database transaction as the change that causes it
  (`milkshake.email_notifications`, migration `AddEmailNotifications`) and sent by a background job every 30 seconds,
  with retries (up to 8 attempts over about 2 hours). An email-provider outage only delays emails; it never affects a
  payment, cancellation or refund.
- **Locally** emails are captured by **Mailpit**: `docker run -d --name mailpit -p 8025:8025 -p 1025:1025 axllent/mailpit`,
  then open <http://localhost:8025>. With `Email:Provider` = `None` (the default outside development) emails stay queued.
- **Safe content:** every value is HTML-encoded, the only link is the order page, there are no tokens or one-click
  actions, and we never ask for card or bank details.
- **Management → Emails** (`/manager/emails`): delivery status with counts, filters, and **Send again** for sent or
  failed emails (`GET /api/manager/emails`, `POST /api/manager/emails/{id}/resend` – ManagerOnly, CSRF, 20 per minute).
- **Real inboxes** (Resend) only with approval and only to allow-listed addresses; the key lives in user secrets.
- Sign-up and password emails are **not** included (deferred, decision D5).
- Docs: [`docs/notifications/email-architecture.md`](docs/notifications/email-architecture.md),
  [`docs/notifications/email-setup-guide.md`](docs/notifications/email-setup-guide.md),
  report `docs/progress/2026-11-20_milestone-9-email-notifications-report.md`.

## Audit trail (Milestone 11)

Every insert, update and delete on the application's tables is recorded in `milkshake.audit_logs` – who, what, old
value, new value, action and when – in the same transaction as the change. Sign-ins and sign-outs are in
`milkshake.security_events`.

- **Who acted:** `user` (with the user id), `anonymous`, or a named system process such as
  `system:payment-reconciliation`, `system:paystack-webhook` or `system:email-dispatcher`.
- **Append-only:** database triggers refuse UPDATE, DELETE and TRUNCATE on both tables, and the application refuses to
  change them (migration `AppendOnlyAuditAndActors`).
- **Search (managers):** `GET /api/manager/audit/changes` (filters `from`, `to`, `user`, `actor`, `entity`, `action`,
  `q`, `cursor`, `limit`) and `GET /api/manager/audit/security`. Read-only, 60 requests per minute, newest first with
  keyset paging; account details and IP addresses are masked. The screen is Management → Reports → Audit Lookup (M12).
- **Coverage:** automated tests count every row written against every audit row across the real write paths, and a
  source scan rejects raw-SQL or bulk writes that would bypass the audit.
- Docs: [`docs/audit/audit-architecture.md`](docs/audit/audit-architecture.md), decisions
  `docs/decisions/2026-11-20_m11-audit-decisions.md`, report `docs/progress/2026-11-20_milestone-11-audit-report.md`.

## Reports (Milestone 12)

- **Patrons:** My orders lists the full order history, 20 at a time with "Load more". Each order shows when it was
  placed, its milkshakes, total, order status and payment status (`GET /api/ordering/orders?cursor&limit`).
- **Managers:** Management → Reports (`/manager/reports`) has three tabs, matching URS §3.4.4. A single date or a
  date range applies to all three, in the business time zone.
  - **Orders:** one row per milkshake: date, time, order, flavour, topping, consistency, pickup and payment status.
    It has a status filter, search, numbered pages, **Copy** (this page) and **Export** (CSV).
  - **Trends:** weekly, monthly and growth charts (all orders against paid orders), each with a table of the numbers.
    With no date chosen, it covers all orders.
  - **Audit Lookup:** who changed which lookup value from what to what, and when; all changes with the M11 filters;
    sign-in events. Each view has an Export.
- **API:** `GET /api/manager/reports/orders` and `/trends` (60 per minute), plus `…/export` endpoints for orders,
  trends and the audit history (10 per minute, at most 10,000 records). Exports are protected against spreadsheet
  formula injection and recorded as `ReportExported` security events.
- **Data:** figures come from the orders as they were placed (never today's prices), and payment status from stored
  payment and refund records. Manager reports show no patron details.
- **Database:** migration `AddOrderReportIndex` (one index on `orders (created_at, id)`).
- Docs: [`docs/reports/reporting-architecture.md`](docs/reports/reporting-architecture.md), decisions
  `docs/decisions/2026-11-21_m12-reporting-decisions.md`, report
  `docs/progress/2026-11-21_milestone-12-reporting-report.md`.

## Deployment (Milestone 14)

Production is one Docker container (`Dockerfile`) serving the site and the API on one origin, on a Render free web
service in Frankfurt, with Supabase PostgreSQL in Ireland. Deployments happen only by hand: tag a commit on `main`
whose CI run passed, then **Actions → Deploy** with the tag. Migrations are reviewed and applied separately with
`tools/deploy/database.mjs`; backups (**Database backup**) and health checks (**Monitor**) run on schedules. Full
procedures, including first deployment, rollback and incidents: [operations guide](docs/guides/operations-guide.md).

## Security notes

- The Paystack secret key is used only by the API; the frontend never sees it, and no card data reaches the application.
- The Resend API key (optional) lives only in user secrets; email addresses and content never appear in logs or the audit trail.
- Secrets live in user secrets (development) or environment variables / a secrets store (production), never in Git.
- Frontend `VITE_` variables are public by design: never put secrets in them.
- Every data change is recorded in `milkshake.audit_logs` in the same transaction as the change; password hashes,
  security stamps and tokens are stored only as `***`.
- Passwords are never logged (verified by an automated test that searches every log line).
- Management changes are version-checked (optimistic concurrency) and audited field by field with the manager's identity.
- Check that the built frontend contains no server details:
  `Select-String -CaseSensitive -Path frontend/dist/assets/*.js -Pattern 'Host=|Database=|Username=|supabase\.co|pooler'`
  (no output expected).