# Production configuration reference (M14)

Setting names only. **No values go in this file, in Git, in the image or in the frontend bundle**: secrets are set in
the host's secret store (Koyeb secrets) and referenced by environment variables. ASP.NET Core reads `A:B` from the
environment variable `A__B` (double underscore); list items use an index, e.g. `Email__AllowedRecipients__0`.

## Committed defaults (`appsettings.Production.json`, no secrets)

| Setting | Value | Why |
|---|---|---|
| `ForwardedHeaders:Enabled` | `true` | Behind Koyeb's proxy: client address and HTTPS come from one forwarded hop. Only safe while the app is reachable solely through that proxy. |
| `DataProtection:StoreKeysInDatabase` | `true` | D-3: sign-in keys in `milkshake.data_protection_keys`, so sign-ins survive restarts. Start-up fails in Production without the certificate below. |
| `Cors:AllowedOrigins` | empty | D-2: one origin; the API serves the site, so no CORS. |
| `Email:Provider` | `Resend` | D-5 as amended by E-1 (Step 8): no sending domain, so Resend delivers only to the Resend account's own address. Sends only to `Email:AllowedRecipients`. |

## Set on the host

| Setting (environment variable) | Secret? | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | no | `Production`. |
| `ConnectionStrings__MilkshakeDb` | no (password-free) | Supabase pooler connection string **without** the password, `SSL Mode=Require`. |
| `Database__Password` | **yes** | Supabase database password. |
| `DataProtection__CertificateBase64` | **yes** | Base64 PKCS#12 certificate that encrypts the key ring. Keep an offline copy: losing it signs everyone out (no data loss). |
| `DataProtection__CertificatePassword` | **yes** | Password of that certificate. |
| `Payments__Paystack__SecretKey` | **yes** | Paystack **test** key only (`sk_test_…`, D-1). Never a live key. |
| `Payments__Paystack__ReturnBaseUrl` | no | The site's public address, with trailing `/`. |
| `Email__Resend__ApiKey` | **yes** | Resend API key. |
| `Email__FromAddress` | no | `onboarding@resend.dev` (Resend's test sender; E-1). |
| `Email__PublicBaseUrl` | no | The site's public address, with trailing `/`. |
| `Email__AllowedRecipients__0` | no (personal data: keep out of Git) | The Resend account's own email address – the only address Resend's test sender can deliver to (E-1). Every other email is marked Suppressed and visible under Management → Emails. |
| `SeedData__…` | – | **Not set on the host.** Demonstration data is added once by `tools/deploy/database.mjs apply --seed` at the first deployment. |

## GitHub Actions (deploy and backup workflows)

| Name | Kind | Notes |
|---|---|---|
| `KOYEB_TOKEN` | secret | Koyeb API token used by the Deploy workflow |
| `KOYEB_SERVICE` | variable | `<app>/<service>` on Koyeb |
| `APP_URL` | variable | `https://<app>.koyeb.app` (no trailing `/`) |
| `BACKUP_PGHOST`, `BACKUP_PGPORT`, `BACKUP_PGUSER`, `BACKUP_PGDATABASE` | secrets | Supabase session pooler connection (port 5432, user `postgres.<project-ref>`, database `postgres`) |
| `BACKUP_PGPASSWORD` | secret | Supabase database password |
| `BACKUP_PASSPHRASE` | secret | Encrypts backups; created by `tools/deploy/new-deployment-secrets.ps1`; keep a copy outside GitHub |
| `BACKUP_ENABLED` | variable | `true` switches the daily backup on |

## Rules

- Never print, log or paste a secret value; check deployment output for none.
- Rotate a secret by replacing it in the host store and redeploying; record the date (not the value) in the operations log.
- The health endpoint (`/api/health`) reports counts and times only, never personal data or references.
