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
| `Email:Provider` | `Resend` | D-5. Sends only to `Email:AllowedRecipients`. |

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
| `Email__FromAddress` | no | Sender address Resend accepts for the account. |
| `Email__PublicBaseUrl` | no | The site's public address, with trailing `/`. |
| `Email__AllowedRecipients__0`, `__1`, … | no (personal data: keep out of Git) | The tester addresses; every other email is suppressed. |
| `SeedData__…` | no | Fresh demonstration data (decision for Step 8; set only for the first start). |

## Rules

- Never print, log or paste a secret value; check deployment output for none.
- Rotate a secret by replacing it in the host store and redeploying; record the date (not the value) in the operations log.
- The health endpoint (`/api/health`) reports counts and times only, never personal data or references.
