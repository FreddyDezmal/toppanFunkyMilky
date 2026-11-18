# Progress Report – Milestone 4: Authentication and Role-Based Access Control

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 18 November 2026
**Period covered:** 16–18 November 2026
**Status:** Milestone 4 complete – all Definition of Done criteria met
**Requirements traced:** URS §3.1 (create account / log in), §3.4.2 (log in / sign up screens), §3.3 and §4 (auditing),
§4 "Two+ user profiles with two logins (Manager, Client)"

---

## 1. Summary

Patrons can register, sign in, stay signed in, view their own profile and sign out. Managers are created only through a
trusted command-line provisioning process and have access to manager-only endpoints and pages that patrons cannot reach.
Authorisation is enforced by the API on every request; the React route guards only improve the user experience.
Every security-relevant event is recorded, and no passwords or tokens appear in responses, logs or the audit trail.

| Area | Result |
| --- | --- |
| Mechanism | ASP.NET Core Identity with an HTTP-only session cookie (no custom JWT, no Supabase Auth) |
| Endpoints | `csrf`, `register`, `login`, `logout`, `me` under `/api/auth`; `GET /api/manager/ping` (Manager only) |
| Roles | `Patron` (assigned at registration), `Manager` (command-line provisioning only) |
| Frontend | Login and registration pages, session state from `/api/auth/me`, protected and manager-only routes, logout |
| Database | One new table: `security_events` (migration `AddSecurityEvents`) |
| Tests | 50 backend (xUnit), 13 frontend (Vitest), 2 end-to-end (Playwright) – all passing |

---

## 2. Design decisions

| # | Decision | Rationale | Status |
| --- | --- | --- | --- |
| A1 | Identity + HTTP-only cookie instead of bearer tokens | JavaScript cannot read the session (XSS), server-controlled expiry and logout, simplest for one developer; bearer tokens can be added alongside for the mobile app (M15) | Recommended, accepted |
| A2 | Own `AuthController` instead of `MapIdentityApi` | Needed first name and mobile number, server-side Patron role, CSRF rules | Recommended, accepted |
| A3 | Same origin in development via the Vite proxy | `SameSite=Strict` cookies require the browser to see one site; production will serve app and API from one domain | **Confirmed by developer** |
| A4 | Throwaway PostgreSQL (Testcontainers) for auth tests | Real Identity, sequences and jsonb behaviour without touching Supabase | **Confirmed by developer** (Docker available) |
| A5 | `security_events` table | Logins, failures and logouts change no data, so the audit interceptor cannot see them; the table is reportable later (M11/M12) | **Confirmed by developer** |
| A6 | Password policy: ≥ 8 characters with upper case, lower case and a digit | URS example screen says "at least 8 characters"; composition rules are a project recommendation | **Confirmed by developer** |
| A7 | Duplicate email: clear field error + rate limiting | Full enumeration protection needs email verification (arrives with M9) | **Confirmed by developer**; revisit in M9 |
| A8 | Mobile numbers stored in E.164 (`+27…`), not unique; registration signs the patron in | URS requires the mobile number; uniqueness not stated | Recommended default, accepted |
| A9 | Manager provisioning as an API command (`provision-manager`), not an endpoint | No public path to privilege; reuses the API's configuration and secrets in every environment | Recommended, implemented |

---

## 3. What was built

### Backend
- **Identity configuration:** user store on the existing tables (no schema change), unique email (the email is also the
  user name), password policy, lockout (5 attempts / 15 minutes), security-stamp re-validation every 5 minutes.
- **Session cookie:** `milkshake.session` – HttpOnly, Secure, SameSite=Strict, 2-hour sliding expiry; the API answers
  401/403 JSON instead of redirecting to a login page.
- **Secure by default:** a fallback authorization policy requires a signed-in user on every endpoint unless it is
  explicitly marked `[AllowAnonymous]`; `ManagerOnly` policy for management endpoints.
- **CSRF protection:** a global filter validates the `X-XSRF-TOKEN` header on every POST/PUT/PATCH/DELETE.
- **Registration:** validation (API contract + Identity), email normalisation, mobile-number normalisation, user and
  Patron role saved in one transaction; any role sent by the client is ignored.
- **Login:** identical response for unknown email, wrong password and locked account; a dummy hash is computed for
  unknown emails so response time does not reveal them.
- **Logout:** deletes the cookie and rotates the security stamp, invalidating copies of the cookie and other sessions.
- **Current user:** `GET /api/auth/me` returns only id, first name, email, mobile number and roles.
- **Rate limiting:** login and registration limited to 10 requests per minute per IP (configurable).
- **Security events:** `LoginSucceeded`, `LoginFailed`, `LoginLockedOut`, `Logout`, `ManagerProvisioned` with user,
  email, IP address and trace id; excluded from the audit interceptor to avoid duplicate records.
- **Manager provisioning:** `provision-manager --email … --first-name …`; creates a manager (hidden password prompt) or
  promotes an existing account; logged at Warning level and recorded as a security event.
- **Database access isolation:** the CSRF endpoint no longer depends on the database (services injected per action).

### Frontend
- Vite dev proxy (`/api` → `https://localhost:7298`); `VITE_API_BASE_URL` defaults to same origin.
- HTTP client with automatic anti-forgery token handling (cached, cleared on session change, one retry on rejection)
  and structured `ApiError` with field-level validation messages.
- `AuthProvider` (session state from `/api/auth/me`, kept in memory only), `RequireAuth` route guard with optional role.
- Pages: Log in, Create account, My account, Management (manager only), Access denied, Not found.
- Accessible forms: labelled inputs, `aria-invalid` and `aria-describedby`, focus moves to the first invalid field,
  server errors shown next to the matching field, safe in-app return path after login.

---

## 4. Security controls and evidence

| Control | Evidence |
| --- | --- |
| Passwords stored only as Identity hashes | `Register_ValidPatron…` (hash ≠ password) |
| No privileged self-registration | `Register_WithManagerRoleInBody…`; `OnlyLoginAndRegisterAcceptAnonymousStateChangingRequests` |
| Enumeration resistance at login | `Login_WrongPasswordAndUnknownEmail_GetIdenticalResponses` |
| Brute-force protection | `Login_AfterFiveWrongPasswords…`, `Login_BeyondRateLimit_Returns429` |
| CSRF | `CsrfEndpoint_ReturnsTokenAndHardenedCookie`, `Register_WithoutCsrfToken…`, `Logout_WithoutCsrfToken…`, manual check (POST without token → 400) |
| Cookie hardening and expiry | `Login_ValidCredentials…` (Set-Cookie flags), `SessionCookie_IsHttpOnlySecureStrict…`, manual check (`document.cookie` empty) |
| Session invalidation | `Logout_EndsTheSession`, `Logout_RotatesSecurityStamp_SoACopiedCookieStopsWorking` |
| Forged cookies rejected | `TamperedSessionCookie_IsRejected` |
| Role enforcement on the server | `ManagerEndpoint_AsPatron_Returns403`, `…AsManager_Returns200`, manual check (`fetch('/api/manager/ping')` as patron → 403) |
| No secrets in responses | `Me_ReturnsOnlySafeFields`, `Register_ResponseContainsOnlySafeFields` |
| No secrets in logs | `Passwords_NeverAppearInLogs` (captures every log line at every level) |
| No secrets in the audit trail | `AuditLog_RecordsPasswordHashOnlyAsRedacted` |
| Settings cannot drift silently | `IdentityPolicy_MatchesTheAgreedPasswordLockoutAndSessionRules` |
| Security events recorded | assertions in login, lockout, logout and provisioning tests; manual SQL checks |

---

## 5. Test results (executed 18 November 2026)

| Suite | Result |
| --- | --- |
| Backend unit + integration (xUnit, Testcontainers PostgreSQL 17) | 50 passed, 0 failed |
| Frontend component (Vitest 5, threads pool) | 13 passed, 0 failed |
| End-to-end (Playwright, Chromium, real API + Supabase) | 2 passed, 0 failed |
| Frontend build and lint | `tsc -b && vite build` passed; oxlint 0 warnings, 0 errors |
| `npm audit --omit=dev` | 0 vulnerabilities in shipped dependencies |
| Manual abuse checks | HTTP-only cookie hidden from scripts; patron → 403 on manager API; POST without CSRF token → 400; identical failure messages and lockout recorded |

Growth from the Milestone 1 baseline: backend 10 → 50 tests, frontend 4 → 13, end-to-end 1 → 2.

---

## 6. Database changes

| Migration | Change |
| --- | --- |
| `AddSecurityEvents` | `milkshake.security_events` (id, occurred_at, event_type, user_id, email, ip_address, trace_id) with indexes on `occurred_at` and `user_id`; no foreign key to users so the trail survives account deletion |

No changes to existing tables. Reviewed with `dotnet ef migrations script` before applying; applied to Supabase on
17 November 2026.

---

## 7. Milestone 4 – Definition of Done

| Criterion | Status |
| --- | --- |
| Patrons can register successfully | ✅ |
| Registration validates required fields and passwords | ✅ |
| Duplicate accounts are handled safely | ✅ |
| Patrons can log in | ✅ |
| Authenticated sessions work correctly | ✅ |
| Users can retrieve their own profile information | ✅ |
| Users can log out | ✅ |
| Protected endpoints reject unauthenticated requests | ✅ |
| Manager endpoints reject patron accounts | ✅ |
| Manager accounts can be provisioned securely | ✅ |
| React registration and login pages work | ✅ |
| Frontend route protection works | ✅ |
| Authentication security controls are tested | ✅ |
| All existing tests continue to pass | ✅ |
| New authentication tests pass | ✅ |
| Documentation is updated | ✅ (README, this report) |
| No secrets have been committed to Git | ✅ (re-check before the final commit, see section 10) |

---

## 8. Issues encountered and resolutions

| Issue | Cause | Resolution / lesson |
| --- | --- | --- |
| `CS0618` obsolete `PostgreSqlBuilder()` | Newer Testcontainers requires the image in the constructor | `new PostgreSqlBuilder("postgres:17-alpine")` |
| `CS0122` `AccountService` inaccessible in tests | Shared message lived on an internal class | Moved to `RegistrationMessages` in the Application layer |
| CSRF endpoint returned 500 without a database | Controller constructor pulled in the DbContext for every action | Database services injected per action with `[FromServices]` |
| Vitest "Timeout waiting for worker to respond" | Process-based workers start slowly on a loaded Windows machine | `pool: 'threads'` in `vite.config.ts` |
| Intermittent `findBy…` failure under heavy load | Testing Library's 1-second default wait | `asyncUtilTimeout: 5000` in the test setup |
| All auth component tests failed with "Element type is invalid" | `authContext.ts` saved as an empty file | Saved the file; `npm run build` would also catch this |
| Playwright registration timed out after 5 s | ≈190 ms per query from South Africa to Supabase EU West, ~15 queries per registration | Expect timeout raised to 20 s; hosting note added (section 9) |
| `npm run test:e2e` "missing script" | Run from the repository root | Run frontend scripts from `frontend/` |

---

## 9. Open items and recommendations

**Security and operations (deployment milestone, M14):**
- Persist ASP.NET Core Data Protection keys (otherwise every deployment signs everyone out).
- Configure forwarded headers behind the reverse proxy so rate limiting and security events see real client IPs.
- Serve frontend and API from one domain over HTTPS (keeps `SameSite=Strict` cookies working without CORS).
- Host the API in the same region as the database, or move the database closer to users: each query currently
  crosses continents (≈190 ms); registration takes 5–8 s in development.

**Functional follow-ups:**
- Email confirmation, password reset and "change password" – depend on the email milestone (M9).
- Revisit duplicate-email disclosure once email verification exists (decision A7).
- Bearer-token authentication for the mobile app (M15), alongside the cookie scheme.
- Security-event and audit reports with filters (M11/M12).

**Dependencies:**
- `npm audit` reports a high-severity ReDoS advisory in `picomatch`, used only by build/test tools (Vite, Vitest);
  no patched version exists yet. Do **not** run `npm audit fix --force` (it would downgrade Vitest 5 → 3).
  Re-check periodically and update normally once a fix is published.

**Test data:**
- Each Playwright run creates one `e2e-…@example.test` patron in the development database. Plan a cleanup approach
  (or a dedicated test database) before reporting milestones.

---

## 10. Sign-off checks before the final commit

```powershell
git log --all -p | Select-String -Pattern 'Password=|pooler\.supabase\.com|service_role|sb_secret|PRIVATE KEY|MILKSHAKE_MANAGER_PASSWORD='
git log --all --name-only --pretty=format: | Sort-Object -Unique | Select-String -Pattern 'secret|\.env|\.pdf'
```

Expected: no output from the first command; only `frontend/.env.example` from the second.

---

## 11. Commit history (Milestone 4)

| Commit | Description |
| --- | --- |
| `89bd18a` | Identity services, session cookie, CSRF filter, secure-by-default authorization |
| `fa1f04e` | Patron registration with server-side role assignment; Testcontainers PostgreSQL tests |
| `3d555bc` | Login, logout, current user, security events, lockout and rate limiting |
| `44bda06` | Command-line manager provisioning; anonymous write-endpoint regression test |
| `e221598` | Login, registration, session state, protected and manager routes via same-origin proxy |
| *(pending)* | Security regression tests (logs, audit, tampered cookie, settings); Vitest stability settings |
| *(pending)* | Documentation: README and this report |
