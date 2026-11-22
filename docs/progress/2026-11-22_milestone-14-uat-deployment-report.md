# Progress Report – Milestone 14: User Acceptance Testing, Deployment and Final Handover

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 22 November 2026 · **Period covered:** 21–22 November 2026
**Status: DRAFT – awaiting UAT.** Deployment, operations and handover work is complete and evidenced. **AC-14.1 and
AC-14.2 cannot be met until the stakeholder testers have run UAT and signed it off**; this report will be finalised
from their results. No stakeholder acceptance is claimed here.

**Requirements traced:** roadmap AC-14.1 to AC-14.5, T14.1 to T14.5; decisions D-1 to D-15, E-1, E-3
(`docs/decisions/2026-11-22_m14-decisions-register.md`).

Related: [Step 4 hardening](../testing/m14-step4-production-hardening.md) ·
[Step 7 backups](../testing/m14-step7-backup-restore.md) · [Step 8 deployment](../testing/m14-step8-deployment.md) ·
[Step 9 verification](../testing/m14-step9-verification.md) · [defect log](../testing/m14-defect-log.md) ·
[UAT pack](../uat/) · [release notes v1.0.2](../releases/v1.0.2.md) · [M13 report](2026-11-21_milestone-13-integration-testing-report.md)

---

## 1. Summary

Release **v1.0.2** is live at **https://milkyshaky.onrender.com**, deployed by hand from the CI pipeline (tag- and
CI-gated) to a Render free web service in Frankfurt, with a fresh Supabase database in Ireland. The live site passed 28
automated outside checks, a browser smoke test (including a test-card payment confirmed through the live Paystack
webhook and the receipt email), and a ZAP baseline scan with no High, Medium or Low findings. Backups run daily and the
first live backup passed its restore test; monitoring alerts by email and was tested once; rollback was rehearsed. The
UAT pack (38 requirement scripts plus 3), tester pack and guides are ready. Two plan changes were forced by outside
facts and recorded: email reaches only the Resend account address (E-1), and Koyeb's free plan closed, so the approved
fallback Render was used (E-3).

## 2. Acceptance criteria

| ID | Criterion (roadmap) | Status | Evidence |
| --- | --- | --- | --- |
| AC-14.1 | UAT scripts cover every Section 11 requirement and are executed by stakeholder testers | **Scripts ready; execution pending** | `docs/uat/m14-uat-scripts.md` (one script per MR-01–MR-10, FR-01–FR-28) |
| AC-14.2 | UAT sign-off with 0 open Critical/High; Medium/Low with an agreed plan | **Pending UAT** | `docs/uat/m14-uat-sign-off.md`, `m14-uat-defect-log.md` |
| AC-14.3 | Deployed from the CI pipeline with live credentials; smoke test incl. a live transaction | **Met with deviation D-1** (test-mode credentials and test card instead of live ones); **stakeholder acceptance of the deviation pending** | Deploy runs 44940146233, 44981488118; smoke test (Step 9 §2) |
| AC-14.4 | Backups, monitoring and a rollback plan in place and tested once | **Met** (developer evidence) | Backup run 44940639255 restore drill passed; alert test received; rollback rehearsal (Step 9 §4–5) |
| AC-14.5 | Patron, manager and technical/operations guides | **Delivered**; stakeholder review at UAT | `docs/guides/` (operations guide v1.0) |

## 3. Results

| Check | Result |
| --- | --- |
| Backend tests | Full suite green in CI on every release commit (673 unit + 520 integration: the 518 of the first CI run plus the DEF-08 race test and the `CF-Connecting-IP` test) |
| Vitest / lint / build | 209/209; clean |
| E2E against the production image | 19/19 (`2026-11-21T18-07-44-117Z`), integrity checks 0 |
| CI on GitHub | Build, tests, vulnerable NuGet, npm audit, gitleaks (whole history), image content check – green |
| Live outside checks | 28/28 (`tools/deploy/verify-production.mjs`) |
| Live smoke test | Passed (owner) |
| ZAP baseline, live (passive) | High 0, Medium 0, Low 0, Informational 6 |
| Backup + restore drill | Local ×2 (512 and 551 rows, counts exact); live passed (27 tables, integrity 18 × 0) |
| Alert test | GitHub failure email received |
| Rollback rehearsal | v1.0.2 → v1.0.1 (~1 min 24 s) → v1.0.2, all Healthy |
| Response time (live, awake, from South Africa) | health p50 241 ms; site p50 196 ms |

## 4. Defects

DEF-04 (Low) and DEF-07 (Low, test) closed in Step 3; DEF-08 (Medium – simultaneous payment confirmations returned
HTTP 500, no data harm), DEF-09 (Low, test), DEF-10 (Low, accessibility) and DEF-11 (Low, configuration) found by the
first CI run, the E2E suite and the image check, fixed with regression tests, and retested locally and in CI. **Open:
none known.** UAT defects will be logged in `docs/uat/m14-uat-defect-log.md`.

## 5. Delivered

- **Production hardening:** same-origin site serving with server-side security headers and HSTS; forwarded headers
  (`CF-Connecting-IP`); encrypted database key ring; worker heartbeats and a `workers` health check; production
  settings without secrets.
- **Container and pipelines:** Dockerfile (non-root, no secrets, no development settings); CI, Deploy (Render API,
  exact commit), Database backup (encrypted, restore-tested) and Monitor workflows.
- **Tools:** `tools/deploy/database.mjs` (guarded migrations, one-time seeding, manager provisioning),
  `new-deployment-secrets.ps1`, `verify-production.mjs`, `tools/db/backup.mjs` and `restore-drill.mjs`; local E2E
  options `--container` and `--backup-drill`.
- **Documents:** UAT plan, scripts, defect log, sign-off form and tester pack; patron, manager and operations guides;
  configuration reference; release notes; decisions register.

## 6. Issues and resolutions

| Issue | Resolution |
| --- | --- |
| Resend cannot email anyone but its account owner without a verified domain | E-1: demonstration email to the facilitator's address only; UAT email scripts adjusted |
| Koyeb free plan closed to new accounts (Mistral AI acquisition) | E-3: approved fallback Render; deploy workflow rewritten for the Render API |
| Render passes the client address only through `CF-Connecting-IP` reliably | New setting `ForwardedHeaders:ForwardedForHeaderName`; verified on the live site |
| Race on simultaneous payment confirmations (CI) | DEF-08 fixed in the data layer; deterministic race test |
| Deploy tool's seed guard misread psql output in rehearsal | Fixed before touching the live database; re-rehearsed |
| GitHub alert email filed under Gmail's Updates tab | Noted in the operations guide |

## 7. Pending – to complete M14

1. **UAT session(s)** with ToppanTester1–3 on the live site (`docs/uat/m14-uat-tester-pack.md`); manager accounts
   created beforehand.
2. Fix any Critical/High UAT defects, redeploy, retest with the testers.
3. **Sign-off** and the stakeholder decisions on the form (D-1, E-1, E-3 limitations, Q-01–Q-04, Q-07/AC-6.6, B1.x,
   Q-08, Q-14, B5, M4 follow-up, D-9; earlier milestone sign-offs).
4. Finalise this report and the release notes from the signed results.

## 8. Commit history (M14)

| Commit | Summary |
| --- | --- |
| c1e8b2d | DEF-04 estimate space; DEF-07 focus test |
| d4ad5c8 | Step 4a: production hosting, key ring, worker health |
| aaa737a | Step 4b: container image, CI, E2E against the image |
| cc09450 | Step 5: UAT pack and guides (drafts) |
| ec753e9 | First CI findings DEF-08/09/10; secret-scan review |
| 02f492e | Step 7: encrypted backups with restore drill |
| 9b25bb1 | Step 8a: deploy workflow, database and secrets tools, runbook |
| 0995f99 | DEF-11: development settings out of the image |
| a7d63b2 | Deploy tool prompts on stderr |
| 1c11289 | Render instead of Koyeb (E-3) — released as v1.0.1 |
| 37fe6b5 | Step 8 deployment record |
| 89848c2 | Step 9: monitoring, verification script, Node 24 actions — released as v1.0.2 |
| c690f19 | Step 9 verification record |
| 2e4879e | Operations guide v1.0 |
| (this commit) | Step 10: handover documents |
