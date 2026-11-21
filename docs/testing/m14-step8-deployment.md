# M14 Step 8: deployment record (2026-11-21)

Plan approved by the stakeholder (Step 8 "Yes I approve"). Accounts were created and every secret was entered by the
owner; no secret value was shown to the assistant, printed, logged or committed.

## Decisions taken during the step

| ID | Decision |
| --- | --- |
| E-1 (amends D-5) | Email via Resend's test sender `onboarding@resend.dev` with no own domain: Resend delivers only to the Resend account's own address, which is the only entry in `Email:AllowedRecipients`. Other patrons' emails are recorded as Suppressed. UAT P-18 to P-20 use the facilitator's account. |
| E-3 (uses the D-2 fallback) | Koyeb closed its free plan to new accounts after joining Mistral AI, so the approved fallback is used: **Render free web service, Frankfurt** (512 MB, 0.1 CPU, sleeps after 15 min idle, about 1 min to wake). Client address read from `CF-Connecting-IP` (Render is behind Cloudflare and only appends to `X-Forwarded-For`). |
| – | New Supabase Free project in West EU (Ireland), separate from the development project (keeps UAT data clean). |

## Record

| Step | Result | Evidence |
| --- | --- | --- |
| Migration script review | 10 guarded migrations, nothing destructive (only 4 index replacements from M10) | `.m14-deploy/migrations-2026-11-21.sql` (local, not committed) |
| Tool rehearsal (throwaway PostgreSQL) | 0 → 10 migrations, seeded once, second `--seed` refused, wrong project refused, manager created | Step 8a; status-parsing bug found and fixed before use |
| Live database (21 Nov 2026 ~19:40 SAST) | 0 → 10 migrations (latest `20261121091600_AddDataProtectionKeys`); seeded 2 roles, 10 settings, 7 flavours, 6 toppings, 4 consistencies, 2 restaurants, 3 tiers; manager `manager@milkyshaky.test` provisioned | `tools/deploy/database.mjs` output |
| CI on the released code | All 4 jobs green (backend, frontend, secret scan, image check) on `0995f99`, then on `1c11289` | GitHub Actions |
| Release | Tag `v1.0.1` → `1c112897ba0eaef948e9b1d96bd070dd24aee7fd` (`v1.0.0` on `0995f99` was never deployed) | git |
| Service created | Render web service `milkyshaky`, Docker, Frankfurt, Free, health check `/api/health`, Auto-Deploy Off; first start reported `{"status":"Healthy"}` | owner's check of `https://milkyshaky.onrender.com/api/health` |
| **Deployment from the pipeline** | Deploy workflow run 44940146233: tag on main ✔, CI passed for the commit ✔, Render deploy `dep-dc0usmutlv9c73ag1kjg` **live**, health **Healthy**, finished 2026-11-21T19:18:33Z, started by FreddyDezmal | GitHub Actions run summary |
| Paystack | Test webhook URL set to `https://milkyshaky.onrender.com/api/payments/paystack/webhook` (test mode only, D-1) | owner |
| **First backup of the live database** | Database backup run 44940639255: **PASSED** – 27 tables restored into a disposable PostgreSQL 17, integrity 18 checks / 0 violations, audit change refused, latest migration present (10 migrations, 7 flavours, 1 user, 54 audit rows, 1 encrypted key-ring key) | GitHub Actions run summary (counts only) |

Annotation to follow up: GitHub warned that `actions/checkout@v4` targets the deprecated Node.js 20 runtime (it was run
on Node.js 24). Housekeeping in Step 9.

## AC-14.3 status

Deployed from the CI pipeline: **yes** (manual, tag-gated, CI-gated). With "live gateway and email credentials":
**replaced by test-mode credentials** under D-1 (stakeholder acceptance of the deviation still required). The smoke
test (register, order, pay with the test card, receive emails, view reports) is Step 9.
