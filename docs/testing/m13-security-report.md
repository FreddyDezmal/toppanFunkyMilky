# M13 Security Report – OWASP Top 10 Checklist and Automated Scans

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Task:** T13.3 · **Acceptance criterion:** AC-13.3 – security testing (OWASP Top 10 checklist and an automated scan)
shows no unresolved High or Critical findings
**Date:** 21 November 2026 · **Result:** **Met.** 0 High, 0 Critical. One Low (site HSTS) deferred to production hosting
(M14).

Related: [test plan](m13-test-plan.md) · [defect log](m13-defect-log.md) ·
[performance report](m13-performance-report.md)

## 1. Scope and method

| Part | How |
| --- | --- |
| OWASP Top 10 (2021) checklist, SEC-01 to SEC-10 | Each category mapped to the controls in the code, with evidence from automated tests (backend integration and unit tests, Vitest, Playwright E2E) |
| Automated scan 1: OWASP ZAP baseline (passive) | `zap-baseline.py` against the production build of the site on the local E2E stack (`npm run test:e2e:local -- --zap`) |
| Automated scan 2: OWASP ZAP API scan (active) | `zap-api-scan.py` with the API's own OpenAPI document (every endpoint), through the site's origin, as a browser calls it. Active rules: injection (SQL, OS command, LDAP, XPath, template), cross-site scripting, path traversal, server-side include, CRLF and parameter tampering, among others |
| Environment | Local E2E stack (test plan §2, L): API in the `E2E` environment, PostgreSQL 17 from migrations, Paystack simulator, Mailpit. No real keys or personal data |

Limits: the ZAP API scan runs unauthenticated, so endpoints behind sign-in answer 401 or 403 to its probes. Signed-in
behaviour is covered by the role and ownership tests in A01 (including E2E-12 over real HTTP) and by input-handling
tests on every endpoint.

## 2. Scan results

| Scan | Run | High | Medium | Low | Info |
| --- | --- | --- | --- | --- | --- |
| Baseline (site), before fixes | `2026-11-21T05-58-36-918Z` | 0 | 2 | 6 | 4 |
| API (active), before fixes | `2026-11-21T05-58-36-918Z` | 0 | 0 | 3 | 6 |
| **Baseline (site), after fixes** | `2026-11-21T06-15-17-905Z` | **0** | **0** | **1** | 4 |
| **API (active), after fixes** | `2026-11-21T06-15-17-905Z` | **0** | **0** | **0** | 6 |

Reports: `.m13-logs/e2e/<run>/zap/` (`baseline.html`, `api.html`, JSON and `summary.txt`). The active scan found **no
injection, cross-site scripting, path traversal or other vulnerability** in any endpoint.

### Findings and resolutions

| ID | Finding (ZAP plugin) | Risk | Where | Resolution | Status |
| --- | --- | --- | --- | --- | --- |
| SEC-F1 | Content Security Policy not set (10038) | Medium | Site | `frontend/security-headers.ts`: `default-src 'self'`, `script-src 'self'`, `style-src 'self'`, `connect-src 'self'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'`, `frame-ancestors 'none'`. All 18 E2E journeys pass under it | Fixed |
| SEC-F2 | Missing anti-clickjacking header (10020) | Medium | Site | `frame-ancestors 'none'` and `X-Frame-Options: DENY` | Fixed |
| SEC-F3 | X-Content-Type-Options missing (10021) | Low | Site, API | `nosniff` on both | Fixed |
| SEC-F4 | Cross-Origin-Resource/Opener/Embedder-Policy missing (90004) | Low | Site, API | CORP and COOP `same-origin` on both; COEP `require-corp` on the site | Fixed |
| SEC-F5 | Permissions-Policy not set (10063) | Low | Site | Camera, microphone, geolocation, payment and USB disabled | Fixed |
| SEC-F6 | Strict-Transport-Security not set (10035) | Low | API | `UseHsts()` outside Development, max-age one year (it skips localhost) | Fixed |
| SEC-F7 | Strict-Transport-Security not set (10035) | Low | Site | Sent by the production host once the site has its HTTPS domain (`max-age=31536000`). Recorded in `security-headers.ts` and the M14 deployment checklist. Not set locally: browsers ignore HSTS on localhost | **Deferred to M14** |
| – | Suspicious comments (10027) | Info | Site bundle | False positive: the word "user" in minified React code | No action |
| – | Cache-control and storable content (10015, 10049) | Info | Site, API | The API already sends `no-store` on personal data (M12); static files may be cached | No action |
| – | Client error responses (100000) | Info | API | Expected: 401, 403, 404 and 400 to the scan's probes | No action |

API headers: `backend/src/Milkshake.Api/Security/SecurityHeaders.cs`, applied to every response (errors included).
On `/api` it also sends a CSP that allows no content and no framing. Evidence:
`SecurityPipelineTests.EveryApiResponse_CarriesTheSecurityHeaders` (200, 401 and 404 responses).

## 3. OWASP Top 10 (2021) checklist

| # | Category | Controls in this application | Evidence | Result |
| --- | --- | --- | --- | --- |
| SEC-01 | **A01 Broken Access Control** | Two roles, Patron and Manager; `PatronOnly` and `ManagerOnly` policies on every controller; ownership checks (another patron's order is 404, never 403, so references cannot be probed); manager endpoints never delete; no anonymous writes except login, register and the signed webhook | `OrderingSecurityTests` and `ManagementSecurityTests` (every endpoint has its policy); `SecurityPipelineTests.OnlyLoginRegisterAndThePaymentWebhookAcceptAnonymousStateChangingRequests`; `OrderingAccessTests`; **E2E-12** (another patron gets 404 on 8 actions over real HTTP; role and anonymous boundaries); E2E-10 | Pass |
| SEC-02 | **A02 Cryptographic Failures** | HTTPS only (redirect, HSTS); session and anti-forgery cookies `Secure`, `HttpOnly`, `SameSite=Strict`; ASP.NET Core Identity password hashing; Paystack webhook HMAC-SHA512 compared in constant time; no card data handled (Paystack hosted checkout) | `LoginAndSessionTests.Login_ValidCredentials_ReturnsProfileAndHardenedSessionCookie`; `SecurityPipelineTests.CsrfEndpoint_ReturnsTokenAndHardenedCookie`; `PaystackWebhookAuthenticatorTests`; `SecurityAbuseTests.AuditLog_RecordsPasswordHashOnlyAsRedacted`; ZAP SEC-F6 | Pass (site HSTS: SEC-F7, M14) |
| SEC-03 | **A03 Injection** | EF Core parameterised queries; the few raw SQL queries are parameterised; strict JSON (unknown fields refused); input validation on every request; React escapes output; CSV exports formula-injection safe | `AuditBypassScanTests` (no unparameterised SQL); `ManagerReportsTests` (literal search with `%` and `_`); `CsvWriterTests`; `Register_*` validation tests; **ZAP active API scan: no injection or XSS findings** | Pass |
| SEC-04 | **A04 Insecure Design** | The server decides every amount (the browser's figures are only compared); an order is confirmed only after the server verifies the payment with Paystack; idempotency keys on order and payment; one refund per payment (database constraint); refund status only from Paystack | **E2E-03 to E2E-07**; `PaymentHardeningTests`; `CancellationHardeningTests`; `OrderingHardeningTests`; integrity checks after every run (E2E-13, 18 checks, all 0) | Pass |
| SEC-05 | **A05 Security Misconfiguration** | Security headers on site and API (§2); ProblemDetails errors with no stack traces; OpenAPI and the API reference page only in Development (the document only in the disposable E2E environment); CORS limited to the site's origin; secrets outside the repository (user secrets, environment) | `SecurityPipelineTests.EveryApiResponse_CarriesTheSecurityHeaders`; `CorsPolicyTests`; safe-error tests in M8b (no secret, provider detail or stack trace in error responses); ZAP after fixes: no Medium or High | Pass |
| SEC-06 | **A06 Vulnerable and Outdated Components** | Central package versions (`Directory.Packages.props`); `npm audit` and `dotnet list package --vulnerable` at each milestone | M13 results: see §4 | Pass |
| SEC-07 | **A07 Identification and Authentication Failures** | Password rules (8+, upper, lower, digit); lockout after 5 wrong passwords; identical responses for wrong password and unknown email; 10 attempts a minute per address on login and registration; logout rotates the security stamp; tampered cookies rejected | `LoginAndSessionTests` (lockout, identical responses, rate limit 429, logout rotation); `SecurityAbuseTests.TamperedSessionCookie_IsRejected`; `RegistrationTests` | Pass |
| SEC-08 | **A08 Software and Data Integrity Failures** | Paystack webhooks verified by signature and then re-verified with Paystack's API; payment claims from the browser never trusted; anti-forgery tokens on every state-changing request; audit history append-only (database triggers) | `PaystackWebhookTests`; `PaystackWebhookAuthenticatorTests`; `Logout_WithoutCsrfToken_IsRejectedAndSessionSurvives`; `Register_WithoutCsrfToken_Returns400AndCreatesNoUser`; `AuditIntegrityTests.TheDatabase_RefusesToChangeOrDeleteAuditHistory`; E2E-05 (duplicate webhooks, one receipt) | Pass |
| SEC-09 | **A09 Security Logging and Monitoring Failures** | Security events (sign-in success and failure, lockout, report exports) stored and shown to managers; full change audit with actor (user or named system process); no passwords or personal data in logs | `SecurityAbuseTests.Passwords_NeverAppearInLogs`; `AuditCoverageTests`; `ManagerAuditTests`; E2E-09 and E2E-10 (Audit Lookup, system actors, masking) | Pass (alerting: M14 monitoring, AC-14.4) |
| SEC-10 | **A10 Server-Side Request Forgery** | The API makes outbound calls only to the configured Paystack base URL and the configured mail server; no user-supplied URLs are fetched; the browser is only ever sent to `https://checkout.paystack.com` | `PaystackGatewayTests`; Vitest `payment.test.tsx` ("never sends the browser to an address that is not Paystack's checkout") | Pass |

## 4. Dependencies and secrets (21 November 2026)

| Check | Command | Result |
| --- | --- | --- |
| npm advisories | `npm audit` (frontend) | **0 vulnerabilities** |
| .NET advisories | `dotnet list package --vulnerable --include-transitive` | **No vulnerable packages** in any of the 6 projects (direct and transitive) |
| Secrets in tracked files | `git grep -nIE "sk_(live\|test)_[0-9a-zA-Z]{20,}\|BEGIN (RSA )?PRIVATE KEY"` | **None found** |

## 5. Other observations

| Observation | Action |
| --- | --- |
| EF Core warning 10102 in the API log: the reconciliation job's "lapse unanswered cancellation requests" step took a batch without ordering it | Ordered oldest first (`PaymentReconciliation.cs`); deterministic batches. Not a vulnerability |
| E2E environment raises the login and registration limit to 300 a minute (DEF-05) because every simulated user shares 127.0.0.1 | Test environment only; production and development keep 10. The limit is proven by `Login_BeyondRateLimit_Returns429` |

## 6. Conclusion

AC-13.3 is met: the OWASP Top 10 checklist passes in every category, and both ZAP scans show **no High or Critical
findings**. The two Medium findings (CSP, clickjacking) and five of the six Low findings were fixed and re-scanned.
The remaining Low finding, HSTS on the website, is deferred to the production host in M14, where it belongs.
