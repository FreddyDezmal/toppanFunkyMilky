# Milestone 14 — User Acceptance Testing, Deployment and Final Handover

## Your Role

Act as my Senior Solution Architect, ASP.NET Core Deployment Engineer, DevSecOps Engineer, PostgreSQL Database Administrator, QA Lead, UAT Coordinator, Site Reliability Engineer and Technical Mentor.

We are completing **Order Milkshake for Pickup**, the Milky Shaky Custom Drinks demonstration application.

I am the sole developer, using Windows 11, Visual Studio Code, PowerShell and GitHub.

Your responsibility is to guide me through **Milestone 14 — User Acceptance Testing and Deployment**, following the actual User Requirement Specification and Development Roadmap.

This is the final planned milestone before optional M15 mobile development.

**Do not begin coding, changing configuration, deploying, provisioning infrastructure or running database migrations immediately.**

First inspect the authoritative documents and repository. Produce a gap analysis, deployment plan, UAT plan and risk assessment. Wait for my explicit approval before making changes.

Work in small, independently verifiable stages.

---

## 1. Project Technology

### Backend

- ASP.NET Core 10 Web API
- C#
- Entity Framework Core 10
- Npgsql
- PostgreSQL
- Supabase PostgreSQL, schema `milkshake`
- ASP.NET Core Identity
- Domain, Application, Infrastructure and API layers

### Frontend

- React 19
- TypeScript
- Vite 8
- Tailwind CSS v4

### Testing

- xUnit
- Testcontainers PostgreSQL 17
- Vitest
- Playwright
- axe
- k6
- OWASP ZAP

### External Services

- Paystack hosted checkout, **test mode only**
- Paystack payment verification and webhooks
- Paystack refunds
- Mailpit for local email testing
- Resend for approved external email tests

### Authentication and Security

- Secure HTTP-only cookies
- SameSite=Strict
- CSRF protection
- PatronOnly and ManagerOnly policies
- Default-deny authorisation
- Rate limiting
- Input validation
- Audit logging
- Server-side secrets

Preserve the established architecture.

---

## 2. Authoritative Sources

Read the actual:

1. User Requirement Specification v1.1.
2. Development Roadmap, especially the exact M14 section.
3. M13 integration test report.
4. M13 security report.
5. M13 performance report.
6. M13 defect log.
7. M13 test plan.
8. M12 reporting progress report and decision register.
9. M11 audit architecture.
10. M9 email architecture.
11. M8 payment and refund documentation.
12. Current deployment and environment documentation.
13. Existing repository configuration.

Extract the exact M14 acceptance criteria and tasks.

**Do not invent AC-14.x or T14.x requirements.**

If any authoritative document is unavailable, identify it and request the missing material before finalising the plan.

Create a requirements traceability matrix showing the requirement, existing evidence, missing work, proposed verification and final sign-off.

---

## 3. Current Project Baseline

The M13 progress report dated 21 November 2026 records:

- 1,180 backend tests passed.
- 206 frontend unit tests passed.
- 18 local end-to-end tests passed.
- 9 test-environment end-to-end tests passed.
- 18 database integrity checks passed locally.
- 18 database integrity checks passed on Supabase.
- 5,702 performance-test requests with zero failures.
- Slowest measured step's 95th percentile: 161 ms.
- OWASP ZAP: zero unresolved High or Medium findings.
- Zero open Critical or High application defects.

Total reported automated test executions: **1,413**.

These are reported results, not tests you have independently executed.

Reproduce the appropriate baseline before making changes.

### Outstanding M13 Items

- DEF-04: the live price estimate causes a layout shift near the Continue to pickup button.
- Website security headers and production HSTS configuration.
- Q-15: stakeholder confirmation of performance targets.
- M12 stakeholder sign-off decisions.
- Monitoring and alerting.

Also review the intermittent test failures previously reported in M12 and record whether M13 evidence resolves them.

---

## 4. Primary Objective

Prepare, deploy, verify and hand over a secure demonstration application that satisfies the actual M14 acceptance criteria.

The milestone should address, where required by the roadmap:

- UAT planning and execution.
- Stakeholder acceptance.
- Final defect resolution.
- Production-like deployment.
- Environment configuration.
- HTTPS and security headers.
- Authentication and session reliability.
- Database migrations and backups.
- Payment test-mode configuration.
- Email delivery configuration.
- Monitoring and alerting.
- Production smoke testing.
- Rollback and recovery.
- Operational documentation.
- Final handover.

Do not expand scope beyond the roadmap without approval.

---

## 5. Deployment Environment Discovery

Before recommending or configuring hosting, inspect the repository and existing deployment decisions.

Determine:

- Whether frontend hosting is already selected.
- Whether API hosting is already selected.
- Whether a public domain exists.
- Whether the frontend and API will share an origin.
- Whether a reverse proxy is required.
- Whether HTTPS termination is handled by the host.
- Whether the application will use the existing Supabase database.
- Whether a separate demonstration database is appropriate.
- Whether GitHub Actions or another CI/CD workflow already exists.
- Whether persistent storage is available for ASP.NET Core Data Protection keys.
- Whether background workers can run reliably on the chosen hosting platform.

Do not assume Render, Vercel, Azure or any other hosting provider is already approved.

Present deployment options only if hosting has not been selected.

Recommend the smallest practical solution consistent with the architecture and budget.

**Do not create accounts, purchase services, configure DNS or deploy until I approve the specific plan.**

---

## 6. Deployment Architecture

Prepare an architecture diagram and deployment checklist.

Include:

- Browser.
- Frontend hosting.
- API hosting.
- HTTPS termination.
- Authentication cookies.
- Reverse proxy or API routing.
- Supabase PostgreSQL.
- Paystack test-mode API and webhooks.
- Email provider.
- Background reconciliation workers.
- Email outbox dispatcher.
- Monitoring.
- Secret storage.
- Backup and recovery.

### Same-Origin Requirements

The existing application relies on cookie authentication and CSRF protection.

Verify that the deployed frontend and API routing preserve the intended security model.

Do not silently change SameSite, CORS or cookie policies to make deployment work.

Any cross-origin design requires a separate security review and approval.

---

## 7. Production Configuration and Secrets

Review configuration for:

- ASP.NET Core environment.
- Database connection string.
- Cookie Secure flag.
- Cookie domain and path.
- CSRF token handling.
- CORS.
- Forwarded headers.
- HTTPS redirection.
- HSTS.
- Rate limiting.
- Paystack test keys.
- Paystack webhook secret.
- Email provider keys.
- Email allow-list.
- Public base URL.
- Logging levels.
- Data Protection key persistence.

### Security Requirements

- No secrets committed to Git.
- No secrets embedded in the frontend bundle.
- No production credentials in test logs.
- No secrets in exception messages.
- No sensitive database values in deployment output.
- No live Paystack keys.
- No real-money transactions.

Use hosting-provider secret management.

Do not print or copy secret values into documentation.

---

## 8. ASP.NET Core Data Protection

Earlier milestones deferred production persistence of Data Protection keys.

Inspect the current configuration.

Determine whether:

- Keys survive application restarts.
- Keys survive redeployment.
- Multiple API instances can share the key ring if required.
- Keys are stored securely.
- Existing authentication cookies remain valid after a routine restart.

Propose an appropriate key-persistence strategy for the selected host.

Do not introduce a new service or database table without approval.

Test the chosen approach.

---

## 9. Background Worker Reliability

The application has:

- Payment reconciliation.
- Refund reconciliation.
- Email outbox dispatching.

Verify that the hosting platform supports these background processes.

Check:

- Worker startup.
- Worker shutdown.
- Restart recovery.
- Idempotency.
- Database leases or locking.
- Multiple instances.
- Resource limits.
- Logging.
- Monitoring.

A host that suspends the API when idle may not reliably execute scheduled work.

Identify this risk before deployment.

Do not assume that successful HTTP hosting means background workers are operating.

---

## 10. Database Deployment and Migration Safety

The database uses Supabase PostgreSQL with the `milkshake` schema.

Before migration:

1. Inspect migration history.
2. Verify the target database.
3. Confirm connection permissions.
4. Identify pending migrations.
5. Review generated SQL.
6. Check compatibility.
7. Confirm backup availability.
8. Prepare rollback or recovery instructions.
9. Obtain explicit approval.

Never:

- Drop the demonstration database.
- Reset existing data.
- Rewrite historical order snapshots.
- Delete audit history.
- Run destructive migrations without approval.

Use disposable PostgreSQL for destructive testing.

Run production database checks in read-only mode unless explicitly approved.

---

## 11. Backup and Recovery

Inspect available Supabase backup capabilities and the project's recovery requirements.

Prepare a practical recovery plan.

Cover:

- Backup frequency.
- Backup retention.
- Who can access backups.
- Database restoration.
- Migration rollback limitations.
- Recovery of configuration.
- Recovery of secrets.
- Recovery of email and payment workers.
- Recovery of audit data.

Where required, perform a restoration drill against an isolated database.

**Never test restoration by overwriting the live demonstration database.**

Record the actual restoration results.

Do not claim backup or recovery readiness without evidence.

---

## 12. Website Security Headers

M13 introduced:

- `backend/.../Security/SecurityHeaders.cs`
- `frontend/security-headers.ts`

The M13 report identifies a remaining website HSTS finding.

Inspect the hosting platform's response headers.

Verify appropriate deployment of:

- Content-Security-Policy.
- X-Content-Type-Options.
- Referrer-Policy.
- Frame protection.
- Permissions-Policy where applicable.
- Strict-Transport-Security.

Use the existing tested header policies as the starting point.

Confirm that HSTS is served over HTTPS.

Do not enable HSTS preload or `includeSubDomains` without reviewing domain ownership and subdomain implications.

Run ZAP again after deployment.

---

## 13. DEF-04 — Layout Shift

M13 left one Low-severity defect:

The live price estimate appears and pushes the **Continue to pickup** button downward.

Inspect the actual component and CSS.

Present at least two options:

1. Reserve space for the estimate.
2. Reposition the Continue button above the changing estimate.

Evaluate mobile layout, accessibility and loading states.

Obtain approval for the preferred solution.

Implement the smallest safe change.

Add a regression test.

---

## 14. User Acceptance Testing

Create a UAT plan based on the exact roadmap and URS requirements.

Do not substitute developer test results for stakeholder acceptance.

Identify:

- UAT participants and roles.
- Test environment.
- Test accounts.
- Test data.
- Acceptance scenarios.
- Expected results.
- Evidence requirements.
- Defect reporting process.
- Sign-off criteria.

### Suggested Patron Scenarios

Confirm against the roadmap:

- Registration.
- Login.
- Milkshake configuration.
- Quantity selection.
- Restaurant selection.
- Pickup scheduling.
- Order review.
- Order placement.
- Paystack test payment.
- Receipt email.
- My Orders.
- Cancellation.
- Refund status.

### Suggested Manager Scenarios

Confirm against the roadmap:

- Catalogue configuration.
- Effective-dated pricing.
- Restaurant configuration.
- System settings.
- Discount configuration safeguards.
- Manager order review.
- Cancellation requests.
- Refund attention.
- Email status.
- Orders reporting.
- Trends.
- Audit Lookup.
- CSV exports.

### UAT Evidence

For each test case, capture:

- Test ID.
- Requirement ID.
- Preconditions.
- Steps.
- Expected result.
- Actual result.
- Pass/fail.
- Evidence reference.
- Tester.
- Date.
- Defect reference.

Do not invent tester names, signatures or approval dates.

---

## 15. Stakeholder Decisions

Resolve or explicitly carry forward the decisions documented in earlier milestones.

### M12 Decisions

- B5: whether orders require a Collected status.
- Q-14: POPIA retention.
- M11 A4: personal-data masking.
- R2: two-tone trend bars.
- R1: extra reporting columns.
- R1: reporting by order-placement date versus pickup date.
- R3: payment-status labels.
- R12: one lookup table at a time versus combined lookup view.
- R5/R13: CSV format and 10,000-record cap.
- R11: whether further charts are required.

### Earlier Decisions

Review unresolved:

- Discount tier rules and eligibility.
- VAT assumptions.
- Pricing sign-off.
- Cancellation fee and refund policies.
- Manager visibility of patron information.
- Email notification content.
- Identity email confirmation and password reset.
- Pickup scheduling assumptions.
- Restaurant operating hours and capacity.

Do not invent stakeholder decisions.

For each item, recommend:

- Approve current behaviour.
- Change before acceptance.
- Accept as a documented limitation.
- Defer with an owner and target milestone.

Identify which decisions block M14 acceptance.

---

## 16. Performance Verification

M13 reported:

- 50 concurrent patrons.
- 5,702 requests.
- Zero failures.
- 161 ms slowest measured step's 95th percentile.
- Provisional target of two seconds.

Q-15 requires stakeholder confirmation.

For deployment:

- Confirm the required target.
- Run a safe smoke or performance test.
- Avoid overwhelming shared hosting.
- Monitor resource usage.
- Record the environment and results.

Do not conduct high-volume load testing against a public environment without approval.

Distinguish local performance results from deployed-environment results.

---

## 17. Monitoring and Alerting

M13 identifies monitoring and alerting as M14 AC-14.4.

Inspect the exact acceptance criterion.

Propose monitoring for:

- API availability.
- Frontend availability.
- Database connectivity.
- Authentication failures.
- Payment reconciliation failures.
- Refund reconciliation failures.
- Email dispatch failures.
- Background worker health.
- Unhandled exceptions.
- Failed deployments.
- Resource exhaustion.

Define:

- Monitoring service.
- Alert thresholds.
- Alert recipients.
- Notification channel.
- Escalation process.
- Response procedure.

Avoid logging personal data or secrets.

Test at least one safe alert scenario if required.

Do not claim monitoring is operational merely because logs exist.

---

## 18. CI/CD and Release Management

Inspect the existing GitHub workflows.

If required, propose a release pipeline with:

- Dependency restoration.
- .NET build.
- Backend tests.
- Frontend lint.
- TypeScript build.
- Vitest.
- Relevant E2E tests.
- Dependency vulnerability checks.
- Secret scanning.
- Deployment approval.
- Release tagging.
- Smoke tests.

Do not automatically deploy every push without explicit approval.

Preserve a clear separation between test and deployed environments.

Record the deployed commit and version.

---

## 19. Deployment Smoke Testing

After an approved deployment, verify:

- HTTPS access.
- Security headers.
- Frontend loading.
- API health.
- Authentication.
- CSRF.
- Patron order creation.
- Paystack test-mode checkout.
- Verified payment.
- Email receipt.
- Order history.
- Manager access.
- Reporting.
- Audit history.
- Background worker operation.
- Monitoring and alerts.

Use approved test accounts and test data.

Avoid real customer data.

Do not make real payments.

Record the actual result of every smoke test.

---

## 20. Rollback and Incident Response

Prepare a rollback procedure covering:

- Frontend deployment.
- API deployment.
- Configuration changes.
- Database migration complications.
- Worker failures.
- Payment integration failures.
- Email delivery failures.

Specify:

- Trigger for rollback.
- Who authorises rollback.
- Steps.
- Expected recovery time.
- Verification.
- Communication.

Do not assume application rollback can safely reverse a database migration.

For irreversible data changes, use a reviewed forward-fix or recovery plan.

---

## 21. Documentation and Handover

Prepare:

- Final README.
- Architecture overview.
- Environment setup guide.
- Deployment guide.
- Configuration reference without secret values.
- Database migration procedure.
- Backup and recovery runbook.
- Monitoring and alerting runbook.
- Incident response procedure.
- UAT test plan and evidence.
- Defect register.
- Outstanding decisions register.
- Known limitations.
- Final requirements traceability.
- Final milestone report.

Keep documentation aligned with the actual implementation.

---

## 22. Final Regression Testing

Before final acceptance, execute the applicable checks:

```powershell
dotnet build Milkshake.slnx -warnaserror
dotnet test Milkshake.slnx

Set-Location frontend
npm run lint
npm run build
npm test
npm run test:e2e:local
npm run test:e2e
npm audit
```

Adapt commands to the actual repository and environment.

Also execute:

- .NET dependency vulnerability checks.
- Secret scanning.
- Database integrity checks.
- Security header verification.
- Appropriate ZAP scans.
- Relevant performance smoke tests.
- UAT scenarios.

Do not run tests requiring external credentials or deployed infrastructure without checking prerequisites and obtaining approval.

Report exact results, skips and failures.

---

## 23. Implementation Sequence

**Stop after each stage and wait for my approval.**

### Step 1 — Requirements and Deployment Readiness Review

Read the M14 roadmap and URS.

Inspect the repository.

Identify:

- Exact AC-14.x requirements.
- Exact T14.x tasks.
- Current hosting decisions.
- Current deployment configuration.
- Outstanding defects.
- Open stakeholder decisions.
- Security risks.
- UAT readiness gaps.

Produce a detailed gap analysis.

**Do not modify anything.**

### Step 2 — Deployment Architecture Approval

Present:

- Hosting options or confirmed hosting.
- Domain and HTTPS strategy.
- API/frontend routing.
- Secret management.
- Database connectivity.
- Data Protection key storage.
- Background worker reliability.
- Monitoring.
- Cost implications.
- Rollback approach.

Obtain approval.

### Step 3 — Final Application Fixes

Address approved release-blocking defects, including DEF-04 if selected.

Add regression tests.

### Step 4 — Production Configuration Hardening

Implement approved configuration changes.

Test locally.

### Step 5 — UAT Preparation

Create test scripts, test accounts and evidence templates.

Do not claim UAT completion before stakeholders execute it.

### Step 6 — UAT Execution and Defect Resolution

Support the actual UAT sessions.

Record evidence and defects.

Fix approved issues.

Obtain stakeholder sign-off.

### Step 7 — Backup and Recovery Readiness

Validate backups and recovery procedures.

Use an isolated environment for restoration drills.

### Step 8 — Approved Deployment

Deploy only after explicit approval.

Record the deployed version.

### Step 9 — Production Smoke Tests and Monitoring

Verify the deployed application.

Test alerting and operational readiness.

### Step 10 — Final Handover and Milestone Closure

Produce the final M14 report.

Include:

- Requirements evidence.
- UAT sign-off status.
- Deployment details.
- Security verification.
- Backup/recovery evidence.
- Monitoring results.
- Open defects.
- Known limitations.
- Outstanding stakeholder decisions.
- Final test counts.
- Git commit and release tag.

---

## 24. Definition of Done

M14 is complete only when:

- [ ] Exact M14 requirements are traced.
- [ ] Deployment architecture is approved.
- [ ] Required application fixes are complete.
- [ ] Security configuration is verified.
- [ ] HTTPS is working.
- [ ] Cookies and CSRF work after deployment.
- [ ] Data Protection keys persist appropriately.
- [ ] Background workers operate reliably.
- [ ] Database migration and recovery procedures are documented.
- [ ] Required UAT scenarios are executed.
- [ ] Stakeholder acceptance is recorded.
- [ ] Required defects are resolved or formally accepted.
- [ ] Monitoring and alerting work.
- [ ] Production smoke tests pass.
- [ ] Required performance targets are met.
- [ ] Regression tests pass.
- [ ] No unresolved Critical or High security findings remain.
- [ ] Final documentation is complete.
- [ ] Handover is complete.

Do not mark any criterion complete without evidence.

---

## 25. Your First Response

Begin with **Step 1 — Requirements and Deployment Readiness Review**.

Read the actual M14 roadmap and URS.

Inspect the existing repository, configuration, test reports and decision registers.

Provide:

1. Exact M14 acceptance criteria and tasks.
2. Requirements traceability matrix.
3. Deployment-readiness assessment.
4. Hosting and architecture options.
5. Security configuration gaps.
6. Database and backup risks.
7. UAT preparation requirements.
8. Outstanding stakeholder decisions.
9. Defect resolution plan.
10. Smallest safe implementation sequence.

**Do not change code, configuration, infrastructure, database or DNS yet. Wait for my explicit approval.**