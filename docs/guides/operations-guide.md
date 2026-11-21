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

Deployments are started by hand only (decision D-14), from a release tag on `main` whose CI run passed. The workflow
`.github/workflows/deploy.yml` tells Koyeb to build the repository's Dockerfile at exactly that commit (`--git-sha`) and
deploy it, keeps Koyeb's deploy-on-push off (`--git-no-deploy-on-push`), waits for the deployment, and checks
`/api/health` over HTTPS. No image registry or registry token is involved. Each run's summary records the tag, commit,
health and who started it: that summary is the deployment log.

### 4.1 Every deployment

1. Merge to `main`; wait for CI to pass.
2. If the release adds migrations, apply them first (§5). Migrations must stay compatible with the version still running.
3. Tag the commit and push the tag: `git tag -a v1.2.0 -m "…"` then `git push origin v1.2.0`.
4. GitHub → Actions → **Deploy** → Run workflow → tag `v1.2.0`, deploy ✔.
5. Check the run summary (health Healthy or Degraded) and open the site.

### 4.2 First deployment (once)

Accounts and secrets are created by the owner; values are never shown to the assistant, printed or committed.

1. **Generated secrets:** `powershell -ExecutionPolicy Bypass -File tools/deploy/new-deployment-secrets.ps1`. It copies,
   one at a time, `DataProtection__CertificateBase64`, `DataProtection__CertificatePassword` and `BACKUP_PASSPHRASE` to
   the clipboard; paste each where the script says and into your password manager.
2. **Supabase:** new **Free** project in **West EU (Ireland)**; save the database password in your password manager.
   From Connect → *Session pooler*: host, port 5432, user `postgres.<project-ref>`, database `postgres`.
3. **Database:** in a terminal at the repository root (Docker running):
   ```
   $env:MILKSHAKE_DEPLOY_HOST = "<pooler host>"
   $env:MILKSHAKE_DEPLOY_USER = "postgres.<project-ref>"
   node tools/deploy/database.mjs script                                   # review .m14-deploy/migrations-<date>.sql
   node tools/deploy/database.mjs apply --confirm <project-ref> --seed     # asks for the password; first time only
   node tools/deploy/database.mjs provision-manager --confirm <project-ref> --email <manager email> --first-name <name>
   ```
4. **Release tag:** on the approved commit (CI passed): `git tag -a v1.0.0 -m "First deployment"`, `git push origin v1.0.0`.
5. **Koyeb:** sign up and connect GitHub, giving the Koyeb app access to **this repository only**. Secrets → one secret
   per **secret** in the configuration reference. Create one **Web service** from the GitHub repository: branch `main`,
   builder **Dockerfile**, **Autodeploy off**, instance **Free**, region **Frankfurt**, port **8080** (HTTP, public route
   `/`), health check **HTTP `/api/health`** on 8080, and the environment variables from the configuration reference
   (secrets as secret references). Koyeb builds and starts it once on creation.
6. **GitHub settings for the workflows:** secret `KOYEB_TOKEN` (Koyeb → Settings → API → new token); variables
   `KOYEB_SERVICE` (`<app>/<service>`) and `APP_URL`; the six `BACKUP_*` secrets (§7).
7. **Paystack (test mode):** Settings → API Keys & Webhooks → test webhook URL
   `https://<app>.koyeb.app/api/payments/paystack/webhook`.
8. Actions → **Deploy** → `v1.0.0`: pins the service to that commit and records the first deployment. Then set
   `BACKUP_ENABLED` = `true` and run **Database backup** once by hand.
9. Step 9 checks (smoke test, ZAP, alert, rollback rehearsal).

## 5. Database migrations

- Migrations are applied **explicitly**, never at start-up, with `tools/deploy/database.mjs` from the owner's machine
  (never from CI). `--confirm <project-ref>` must match the target, so a command cannot run against the wrong database.
- Before applying, generate the idempotent SQL with `node tools/deploy/database.mjs script` and review it: only
  additive, backward-compatible changes are applied without a separate approval. Never drop the database, reset data,
  rewrite order snapshots or delete audit history.
- `apply` shows the applied migrations before and after. `--seed` (demonstration data) is refused once the database
  has any flavours: seeding is for the first deployment only, because seeding again would restore items a manager
  renamed.
- Take a backup (Actions → Database backup → Run workflow) before applying migrations to the live database.
- **Rolling back the application does not roll back the database.** Fix forward; down-migrations are not run on live
  data.
- `node tools/deploy/database.mjs status --confirm <project-ref>` is read-only (migrations applied, latest, flavours).

## 6. Manager accounts

Managers are created from the command line, never through the website:

```
node tools/deploy/database.mjs provision-manager --confirm <project-ref> --email <address> --first-name <name>
```

It asks for the database password and the new manager's password (twice; typing is hidden), or reads
`MILKSHAKE_DEPLOY_PASSWORD` and `MILKSHAKE_MANAGER_PASSWORD` if they are set. Passwords are passed to the tool only
through its environment and masked in its output. Locally the same command is
`dotnet run --project backend/src/Milkshake.Api -- provision-manager …` with `MILKSHAKE_MANAGER_PASSWORD` set.
Give each manager tester their own account; the audit trail records who made each change.

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
- Email via Resend's test sender: without an own domain Resend delivers only to the Resend account's own address
  (E-1, amending D-5). Emails to anyone else are marked Suppressed and visible under Management → Emails.
- Frequent-customer discounts switched off until the stakeholder supplies rates (Q-01–Q-04).
- No email confirmation or password reset for patrons (M4 follow-up).
- No "Collected" order status (B5).
- Personal data retention policy not decided (Q-14); the demonstration holds test data only.
- Code not reviewed by a second developer (D-9).
