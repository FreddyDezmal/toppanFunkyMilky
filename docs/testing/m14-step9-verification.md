# M14 Step 9: production verification (21–22 November 2026)

Approved by the stakeholder (Step 9 "Yes I approve"), including a **passive** ZAP scan of the live site. No load test
and no active scan were run against the public site. Site: `https://milkyshaky.onrender.com` (Render free, Frankfurt).

## 1. Automated checks – `tools/deploy/verify-production.mjs`

Run 2026-11-21T19:29Z from the developer machine (report `.m14-deploy/verify-2026-11-21T19-29-15-501Z.md`):
**28 passed, 0 failed.**

| Area | Result |
| --- | --- |
| Health | Healthy; `self`, `database`, `workers` all Healthy; response holds no personal data |
| HTTPS | HTTP → HTTPS 301; HSTS `max-age=31536000` without `includeSubDomains`/`preload` (D-6) |
| Website headers | CSP equal to `SecurityHeaders.SiteContentSecurityPolicy`; nosniff; X-Frame-Options DENY; Referrer-Policy strict-origin-when-cross-origin; Permissions-Policy; COOP same-origin; `index.html` no-cache; hashed assets `public, max-age=31536000, immutable`; client routes fall back to the site |
| API headers | CSP equal to `SecurityHeaders.ApiContentSecurityPolicy`; Referrer-Policy no-referrer; unknown `/api` path 404 problem+json |
| API description | No OpenAPI document and no Scalar page published |
| Cookies / CSRF | `milkshake.antiforgery` Secure, HttpOnly, SameSite=Strict, host-only; a write without the token refused (400) |
| CORS | Cross-origin preflight and reads get no `Access-Control-Allow-Origin` |
| Response time (20 sequential, awake) | `/api/health` p50 241 ms / p95 339 ms; site p50 196 ms / p95 204 ms; `/api/ordering/options` (401) p50 196 ms / p95 205 ms |

Observation (not a defect): unknown file-like paths such as `/openapi/v1.json` answer 401 (the site fallback skips
paths with a file extension, so the deny-by-default policy answers); nothing is disclosed.

## 2. Browser smoke test (owner, 21 November 2026)

Reported by the owner as passed on every row: patron registration (with the Resend account address, E-1); session
cookie Secure, HttpOnly, SameSite=Strict; order of 2 milkshakes; **payment with the Paystack test card confirmed
through the real public webhook** (Paystack delivery to `/api/payments/paystack/webhook` with 200 – the first live
webhook of the project); receipt email "Order MS-… confirmed – your receipt" received; manager sign-in; Emails (Sent),
Reports (order listed); Audit Lookup sign-in events show the owner's address, matching the public IP – confirming the
`CF-Connecting-IP` setting (E-3).

## 3. ZAP baseline (passive) on the live site

`zap-baseline.py -t https://milkyshaky.onrender.com/` (ghcr.io/zaproxy/zaproxy:stable), 2026-11-22 03:30 UTC, report
`.m14-deploy/zap-live.html` / `.json`: **High 0, Medium 0, Low 0**, Informational 6 (Suspicious Comments in the
minified bundle; Modern Web Application; Re-examine Cache-control; Storable/Non-Storable content ×3). The M13 Low
finding (no HSTS from `vite preview`) is **closed**: the live site sends HSTS.

## 4. Monitoring (D-13) – `.github/workflows/monitor.yml`

Every 6 hours and on demand; the run fails unless `/api/health` is Healthy, and GitHub notifies the owner.

| Test | Result |
| --- | --- |
| Normal run (on demand) | Passed, summary "Health: Healthy" (reported by the owner) |
| **Alert test** (`simulate_failure`) | Run failed on purpose; GitHub's failure email **received** (filed by Gmail under the *Updates* tab – noted in the operations guide) |

## 5. Rollback rehearsal (deploy workflow)

| Run | Tag | Commit | Render deploy | Health | Finished (UTC) |
| --- | --- | --- | --- | --- | --- |
| 44981271381 – new release | v1.0.2 | `89848c25f06c…` | `dep-dc165nijnfac73fr27b0` live | Healthy | 2026-11-22T03:35:40Z |
| 44981360763 – **rollback** | v1.0.1 | `1c112897ba0e…` | `dep-dc1668utlv9c73b6ddeg` live | Healthy | 2026-11-22T03:37:04Z |
| 44981488118 – roll forward | v1.0.2 | `89848c25f06c…` | `dep-dc1670hksfps73fvjql0` live | Healthy | 2026-11-22T03:38:24Z |

The rollback was live and healthy about 1 min 24 s after the previous deploy finished. The database was not touched
(no migration differs between the tags); v1.0.1 and v1.0.2 differ only in workflows and tools, so this rehearses the
procedure, not a change in application behaviour.

## 6. Housekeeping

Workflows moved to `actions/checkout`, `setup-node`, `setup-dotnet`, `upload-artifact` v5 (Node.js 24), clearing the
Node 20 deprecation warning.

## AC-14.4 status

| Part | Evidence | Status |
| --- | --- | --- |
| Backups in place and tested once | Daily backup workflow; live backup run 44940639255 restored and checked (Step 8) | **Met** |
| Monitoring in place and tested once | Monitor workflow every 6 h; alert test email received | **Met** (periodic, not continuous – D-13 limitation) |
| Rollback plan in place and tested once | Operations guide §9; rehearsal above | **Met** |
