# M14 Step 4: production hardening evidence (2026-11-21)

Scope approved by the stakeholder ("Approve", Step 4). Nothing was deployed, no accounts were created and Supabase
was not touched: every database below is a throwaway container.

| Item | Decision | Evidence |
|---|---|---|
| Same-origin site served by the API (wwwroot, SPA fallback, per-path headers) | D-2 | `SiteServingTests` 7/7; E2E 19/19 against the image |
| Forwarded headers, off unless configured | D-2 | `ProductionHostingTests`: forwarded address recorded; ignored when off |
| Data Protection key ring in `milkshake.data_protection_keys`, certificate-encrypted, not audited | D-3 | `ProductionHostingTests`: sign-in survives a second host; keys stored as `encryptedSecret`; separate ring rejects the cookie. Migration `AddDataProtectionKeys` adds one table only |
| Workers run at start-up; "workers" health check (Degraded, counts only) | D-11, D-13 | Existing worker/health tests pass; health check removed only where no database exists |
| Production settings without secrets; configuration reference (names only) | D-5, D-6 | `appsettings.Production.json`, `docs/deployment/configuration-reference.md` |
| Container image (Node build, .NET publish, ASP.NET runtime, non-root, HTTP 8080) | D-2 | `docker build` succeeded on the developer machine |
| Image under production settings behind a TLS edge proxy | D-2, D-3 | `npm run test:e2e:local -- --container`: 19/19 passed, integrity checks 0 violations (run 2026-11-21T09-38-09-230Z). Container log: no warnings; one expected error (deliberate wrong-amount payment test); no secrets |
| CI (build -warnaserror, tests, NuGet vulnerabilities, lint/build/Vitest, npm audit, gitleaks, image build and content check) | D-14 | `.github/workflows/ci.yml`; first run happens on the next push to `main` |

Test totals on the developer machine: backend 1,191/1,191 (1,190 in the full run, plus the corrected
`SiteServingTests` 7/7), lint 0 warnings, E2E 19/19 against the production image.

Not yet evidenced (later steps): a CI run on GitHub, the deployed environment, backups and restore, monitoring
alerts, ZAP against the deployed site, rollback rehearsal.
