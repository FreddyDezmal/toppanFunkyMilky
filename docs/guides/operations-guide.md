# Milky Shaky Custom Drinks – Technical and operations guide

For whoever runs the demonstration deployment. **Draft (M14 Step 5).** Sections marked *To complete in Step N* are
filled in from evidence when that step is done; nothing here is claimed before it has been tested.

## 1. Architecture

```
Browser ──HTTPS──▶ Koyeb edge (TLS, free *.koyeb.app domain)
                     │ HTTP + X-Forwarded-For/Proto
                     ▼
        One container (Koyeb free instance, Frankfurt, 512 MB, scales to zero after 1 h idle)
          ├─ /          built React site (wwwroot), SPA fallback, website security headers
          ├─ /api/*     ASP.NET Core 10 API (cookies SameSite=Strict, CSRF, rate limits)
          ├─ /api/payments/paystack/webhook ◀── Paystack (test mode)
          ├─ PaymentReconciliationWorker (at start-up, then every 5 min) ──▶ Paystack API
          └─ EmailDispatchWorker (at start-up, then every 30 s) ──▶ Resend (approved recipients only)
                     │
                     ▼
        Supabase PostgreSQL (Ireland), schema "milkshake", incl. data_protection_keys (encrypted)
GitHub Actions: CI on push (checks only); manual deploy; scheduled backup, restore drill and health check
```

Design decisions: `m14/2026-11-21_m14-step2-architecture-decisions.md` (D-1 to D-15).

**Free-tier consequences (accepted):** the app sleeps after an hour without visitors; the first request afterwards
takes several seconds while it starts, and the background workers do not run while it sleeps. On start-up both workers
run at once, so payments and emails waiting from before are processed then. Paystack retries webhooks, and the patron's
own page refresh also checks payment status.

## 2. Configuration and secrets

All settings and secrets: `docs/deployment/configuration-reference.md` (names only). Rules:

- Secrets live only in Koyeb secrets and GitHub Actions secrets; never in Git, the image, the website bundle, logs or
  documents.
- Paystack **test** keys only (`sk_test_…`). A live key must never be configured (decision D-1).
- Rotate a secret by replacing it in Koyeb and redeploying; record the date, not the value.
- The Data Protection certificate encrypts the sign-in key ring. Keep an offline copy. Losing it signs everyone out
  (no data is lost); replace it by setting a new certificate and redeploying.

## 3. Building and testing

| Check | Command (repository root unless stated) |
| --- | --- |
| Backend build | `dotnet build Milkshake.slnx -warnaserror` |
| Backend tests (Docker needed) | `dotnet test Milkshake.slnx` |
| Frontend | in `frontend/`: `npm run lint`, `npm run build`, `npm test` |
| End-to-end (Docker needed) | in `frontend/`: `npm run test:e2e:local` (dev stack) or `npm run test:e2e:local -- --container` (production image) |
| Image | `docker build -t milkshake .` |

CI (`.github/workflows/ci.yml`) runs the same checks plus a vulnerable-package check, `npm audit`, a gitleaks scan of
the whole history and an image content check, on every push to `main`. CI never deploys.

## 4. Deploying

*To complete in Step 8:* Koyeb service settings, the manual deployment workflow, image tag and version recording,
first-time secrets, Paystack test webhook URL, and the post-deploy checklist.

## 5. Database migrations

- Migrations are applied **explicitly**, never at start-up. Each release's migrations are reviewed as an idempotent SQL
  script (`dotnet ef migrations script --idempotent`) before they are applied.
- Only additive, backward-compatible migrations are applied to the live database without a separate approval. Never
  drop the database, reset data, rewrite order snapshots or delete audit history.
- **Rolling back the application does not roll back the database.** Fix forward; down-migrations are not run on live
  data.
- *To complete in Step 8:* the exact command and who approves it.

## 6. Manager accounts

Managers are created from the command line, never through the website. The password is read from the
`MILKSHAKE_MANAGER_PASSWORD` environment variable so it never appears in the command or shell history:

```
provision-manager --email <address> --first-name <name>
```

Locally: `dotnet run --project backend/src/Milkshake.Api -- provision-manager …`. *To complete in Step 8:* how this is
run against the deployed database.

## 7. Backups and restore

*To complete in Step 7:* scheduled `pg_dump` of the `milkshake` schema by GitHub Actions to an encrypted artifact,
retention, and the restore drill into a disposable PostgreSQL with the integrity checks
(`tools/db/integrity-checks.sql`). Restores are never tested by overwriting the live database.

## 8. Monitoring

`GET /api/health` returns JSON with an overall status and one entry per check:

| Check | Healthy | Degraded (HTTP 200) | Unhealthy (HTTP 503) |
| --- | --- | --- | --- |
| `self` | app running | – | – |
| `database` | database reachable | – | cannot reach the database |
| `workers` | each worker ran recently; nothing waiting for a manager | a worker has not run for three intervals; emails failed in the last 24 h; refunds failed or needing attention | – |

The health response contains counts and times only, never personal data.
*To complete in Step 9:* the scheduled GitHub Actions health check, how it alerts, and the record of one alert test.

## 9. Rollback

*To complete in Step 9:* redeploying the previous image, the conditions for rolling back, and the record of one
rehearsal. See §5: the database is never rolled back by an application rollback.

## 10. Incidents

| Symptom | First checks |
| --- | --- |
| Site does not load | Koyeb service status and logs; `/api/health` |
| Health 503, `database` unhealthy | Supabase status; database password secret; Supabase project paused (free plan) |
| Health Degraded, `workers` | Management → Emails (failed), Orders → Refunds; Koyeb logs for worker errors |
| Payments stay unpaid | Paystack test dashboard; webhook URL; Koyeb logs for webhook errors; the reconciliation worker runs at start-up and every 5 min |
| Everyone signed out after a deploy | Data Protection certificate secret changed or missing |
| Emails show Suppressed | Recipient not on `Email:AllowedRecipients` |

## 11. Known limitations

- Free hosting sleeps when idle; first request after sleep is slow; one instance only.
- Paystack test mode only; no real payments (D-1).
- Email via Resend's test sender to approved recipients only; no own sender domain (D-5).
- Frequent-customer discounts switched off until the stakeholder supplies rates (Q-01–Q-04).
- No email confirmation or password reset for patrons (M4 follow-up).
- No "Collected" order status (B5).
- Personal data retention policy not decided (Q-14); the demonstration holds test data only.
- Code not reviewed by a second developer (D-9).
