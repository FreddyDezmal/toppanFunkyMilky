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

Supabase Free keeps no backups, so the project makes its own (decision D-12).

| Item | Detail |
| --- | --- |
| What | `pg_dump` of schema `milkshake` (all application tables, the audit trail with its append-only triggers, the migration history and the encrypted sign-in key ring), custom format |
| How | `tools/db/backup.mjs` runs `pg_dump` in a `postgres:17` container (read-only) and encrypts the result with AES-256-GCM, using a key derived from `BACKUP_PASSPHRASE` |
| When | `.github/workflows/backup.yml`, daily 03:23 South African time, and on demand (Actions → Database backup → Run workflow) |
| Where | Encrypted file + counts-only restore report kept as a workflow artifact for 30 days |
| Every backup is tested | The same workflow restores it with `tools/db/restore-drill.mjs` into a disposable PostgreSQL on the runner and fails if anything is wrong |
| Switched on | Only when the repository variable `BACKUP_ENABLED` is `true` and these secrets exist: `BACKUP_PGHOST`, `BACKUP_PGPORT`, `BACKUP_PGUSER`, `BACKUP_PGPASSWORD`, `BACKUP_PGDATABASE`, `BACKUP_PASSPHRASE` (*set in Step 8*) |

**The passphrase.** Keep a copy of `BACKUP_PASSPHRASE` outside GitHub (for example in a password manager). Without it
no backup can be decrypted. Changing it affects new backups only; keep the old one until its backups expire.

**What the restore drill checks** (`node tools/db/restore-drill.mjs <file.msbk> [--expect-counts counts.json] [--report report.json]`,
with `BACKUP_PASSPHRASE` set; needs Docker):

1. the file decrypts (a wrong passphrase or a changed byte is refused) and is a `pg_dump` archive;
2. `pg_restore --exit-on-error` into a new, disposable PostgreSQL 17 container that is removed afterwards – it never
   connects to any other database, so it cannot overwrite live data;
3. the 18 integrity checks (`tools/db/integrity-checks.sql`) report 0;
4. the audit trail is still append-only (its triggers are present and changing an audit row is refused);
5. the migration history is present (latest migration reported);
6. with `--expect-counts`, every table's row count equals the source.

**Evidence (AC-14.4 "tested once", before deployment):** `npm run test:e2e:local -- --container --backup-drill`,
run `2026-11-21T10-42-51-261Z`: after 19 of 19 E2E tests against the production image, the app was stopped, the test
database was backed up (107 KB, 3.3 s), restored in 1 s, and the drill **passed**: 27 tables, 551 rows, counts equal
to the source, 18 integrity checks at 0, 4 audit triggers, an audit change refused, latest migration
`20261121091600_AddDataProtectionKeys`. The encrypted file contained no readable text and no log contained a secret.
An earlier run (`2026-11-21T10-25-58-944Z`, 512 rows) also passed. *Step 8 adds the first backup of the live
database and its drill on GitHub.*

**Restoring for real** (only after a decision to do so; never test by overwriting the live database):

1. Download the artifact from the chosen backup run and decrypt-test it with `restore-drill.mjs` first.
2. Restore into a **new** Supabase project (or a new database), never over the existing one, with `pg_restore --no-owner
   --no-privileges --exit-on-error`, then run the integrity checks there.
3. Point the app at it by changing the connection secrets and redeploying; keep the old database untouched until the
   new one is accepted.

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
