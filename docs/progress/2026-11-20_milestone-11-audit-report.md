# Progress Report – Milestone 11: Auditing and Transaction History

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 20 November 2026
**Period covered:** 20 November 2026
**Status:** M11 complete. Every insert, update and delete on every audited table is recorded with who, what, old,
new, action and when, in the same transaction as the change; each record names its actor, including the system
processes. Audit history is append-only in the database and the application, and managers can search it with
filters and paging within 3 seconds over 100,000 records. A coverage test proves that no write path skips the audit
(it found one gap, now fixed).
**Requirements traced:** roadmap AC-11.1 to AC-11.5, T11.1 to T11.5, MR-04, FR-09, FR-23, R-06; AC-7.2, AC-10.4,
AC-10.5 and R-04 re-verified end to end; M11 decisions A1 to A11 (`docs/decisions/2026-11-20_m11-audit-decisions.md`).

Related: [audit architecture](../audit/audit-architecture.md) · [M9 report](2026-11-20_milestone-9-email-notifications-report.md)

---

## 1. Summary

- **Scope (A1):** the roadmap's M11 is the audit trail. Patron history screens, the audit screen and the lookup-change
  report are M12 (AC-12.1, AC-12.4, AC-12.5); My orders already exists from M8b.
- **Actors (A2):** `user`, `anonymous`, or a named system process – webhook, reconciliation, email dispatcher, seeder,
  manager provisioning – set explicitly and never confused with a browser user.
- **Append-only (A7):** database triggers and an interceptor guard; the API is read-only.
- **No unaudited writes:** email claims became audited saves (A8); "touch" updates are audited (A9).
- **Audit query (A3, A4, A10):** change history and sign-in events with date, user, actor, table, action and free-text
  filters, keyset paging, masking of personal data, rate limit.
- **Coverage (A11):** schema enumeration, writes-versus-audit-rows counting across real write paths, and a source scan
  for raw-SQL and bulk writes.
- **Migration** `AppendOnlyAuditAndActors` (additive: actor column and check, two indexes, trigger function, four
  triggers) reviewed and applied to Supabase.

## 2. Decision register

Decisions A1 to A11 are in the M11 decision register. A4 (masking of personal data) is provisional pending Q-14.

## 3. Design

| Part | Contents |
| --- | --- |
| Domain | `AuditActors` (allowed actors, the same rule as the database check); `AuditLog.Actor` |
| Application | `AuditActorScope` (ambient system actor); `AuditQuery`, `SecurityEventQuery`, `AuditQueryParser` (strict filters), `AuditCursor` (opaque keyset), `AuditMasking`, `IAuditQueryService` |
| Infrastructure | Interceptor: actors, append-only guard, touch auditing. Scopes in the webhook handlers, reconciliation, dispatcher, seeder. Dispatcher claim through the domain. `AuditQueryService` (parameterised read-only SQL) |
| Database | Migration `20261120174124_AppendOnlyAuditAndActors`: `audit_logs.actor` with `ck_audit_logs_actor`; indexes `(occurred_at DESC, id DESC)` on both tables; `refuse_audit_change()` and triggers |
| API | `GET /api/manager/audit/changes`, `GET /api/manager/audit/security`; rate-limit policy `audit`. Manager-provisioning CLI scoped |

## 4. Requirements traceability

| Requirement | Status | Evidence |
| --- | --- | --- |
| **AC-11.1** Every insert, update and delete on audited tables creates a record with user, entity, field, old, new, action and timestamp | **Met** | `AuditCoverageTests` (writes = audit rows per table and action across real write paths); `AuditIntegrityTests` (actor and user per source: manager, anonymous, webhook, reconciliation, dispatcher; a system scope never takes the signed-in user); earlier per-feature audit tests |
| **AC-11.2** Payment, order-status and email events recorded as transaction audit entries | **Met** | Payment and order changes audited with the webhook, reconciliation or patron as actor (webhook and reconciliation tests); email claim and result audited as the dispatcher; sign-in events searchable (`/audit/security`) |
| **AC-11.3** Audit records cannot be edited or deleted through the application | **Met** | Triggers refuse UPDATE, DELETE, TRUNCATE on both tables (7 tamper cases); interceptor refuses EF changes (2 tests); audit endpoints refuse POST, PUT, PATCH, DELETE (405) |
| **AC-11.4** Filters for date range, user, entity/table, action and free text; within 3 s for 100,000 records | **Met** | `ManagerAuditTests`: filters alone and combined, stable pages, 12 invalid-filter cases, masking and unsearchable personal data, roles, rate limit; **8 filter combinations each under 3 s over 100,000 seeded records** |
| **AC-11.5** An automated test enumerates audited tables and fails if any write path skips the audit | **Met** | `AuditCoverageTests` (schema enumeration; writes-versus-audits) and `AuditBypassScanTests`; the counting test found unaudited "touch" updates, fixed (A9) |
| T11.1 Append-only audit table and indexes | **Met** | Migration triggers and indexes |
| T11.2 Field-level capture on all write paths | **Met** | Interceptor; coverage test; email claim moved off raw SQL |
| T11.3 Transaction events incl. logins | **Met** | As AC-11.2 |
| T11.4 Audit query endpoint with filters and paging | **Met** | As AC-11.4 |
| T11.5 Audit-coverage and tamper tests | **Met** | As AC-11.3 and AC-11.5 |
| MR-04, FR-09, FR-23 | **Met for M11** | Through AC-11.1 to AC-11.5; the manager-facing report screens are M12 (AC-12.4, AC-12.5) |
| R-06 Audit incomplete | **Mitigated** | Central interceptor, append-only table, automated coverage and bypass tests |
| AC-7.2, AC-10.4, AC-10.5, R-04 (re-verified) | **Met** | `HistoricalAccuracyTests`: after renaming the flavour, deactivating the topping, renaming and moving the restaurant, changing a price, the VAT rate and the fee rate, the order page, My orders entry, cancellation page, stored refund and receipt payload are byte-for-byte unchanged |

## 5. Security controls with evidence

| Control | Evidence |
| --- | --- |
| Manager-only audit access | Patron 403, anonymous 401 on both endpoints |
| Read-only | GET only (405 for writes); append-only triggers and guard |
| Strict validation | Unknown, repeated or malformed filters → 400 with field errors; no exception or database text in errors (tested) |
| Bounded queries | Page size 1–100, period at most 366 days, free text at most 100 characters, keyset paging with indexes |
| Injection-safe | Fixed SQL fragments, every value a parameter; `%` and `_` taken literally (tested) |
| Personal data | Account details and IPs masked on output; patrons masked, managers in full; masked values not searchable (tested); write-time masking of secrets and email content unchanged |
| Rate limit | 60 per manager per minute (429 tested) |
| Actor attribution | System actors explicit; checked by the database |
| No new secrets or dependencies | Secret scan clean; `dotnet list package --vulnerable`: none; `npm audit`: 0 |

## 6. Test results (executed 20 November 2026)

| Suite | Count | Result |
| --- | --- | --- |
| Backend unit + integration (Testcontainers PostgreSQL 17, Mailpit) | 1,050 (944 at M9 + 106) | Passed (1,050/1,050) |
| Vitest | 164 | Passed (no frontend change in M11) |
| Playwright (with axe), including the manager journey | 9 | Passed (9/9) |
| oxlint, TypeScript build, .NET build `-warnaserror` | – | Passed |
| Dependency audits | – | No findings |

New in M11:

| Area | Tests |
| --- | --- |
| Actors and scope (`AuditActorTests`) | 24 |
| Audit query parsing, cursor, masking (`AuditQueryTests`) | 40 |
| Bypass scan (`AuditBypassScanTests`) | 2 |
| Append-only and actor attribution (`AuditIntegrityTests`) | 15 |
| Coverage (`AuditCoverageTests`) | 2 |
| Audit query API (`ManagerAuditTests`) | 22 |
| Historical accuracy (`HistoricalAccuracyTests`) | 1 |

## 7. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| **The coverage test found unaudited writes:** "touch" updates (a catalogue item when its prices change; the partner of the minimum/maximum drinks setting) changed only `updated_at`, so the interceptor wrote no audit row | Every UPDATE is now audited; for a touch the record lists the changed columns (A9) |
| The M9 email dispatcher claimed emails with a raw `UPDATE`, invisible to the audit | Row locks, then the domain claim and an audited save (A8) |
| In .NET a `$` anchor also matches before a trailing line break, so `"user\n"` passed the actor rule (found by a unit test) | The C# rule ends with `\z`; the database keeps `$` |
| The audit queries returned 500: the raw-SQL result types are mapped with the project's snake_case convention | Result columns named in snake_case |
| Seeding 100,000 rows exceeded the 30-second command timeout on the developer's Docker | Seed statement given 3 minutes; the timed queries keep the 3-second target |
| A test assumed no audit rows dated tomorrow; other tests legitimately run on a clock days ahead | The test uses a date no test clock reaches |
| Test-request details (a price schedule needs the item version; analyzer rules xUnit2029 and EF1002) | Tests corrected |

## 8. Database

`20261120174124_AppendOnlyAuditAndActors` was reviewed with `dotnet ef migrations script` (only the expected trigger
definitions matched DROP/DELETE/TRUNCATE/UPDATE) and applied to Supabase on 20 November 2026 with the developer's
approval. From then on, audit history on Supabase cannot be changed through normal connections.

## 9. Open items

| Item | Needed from | Effect |
| --- | --- | --- |
| Q-14 retention (POPIA) | Stakeholder | Audit records and security events are kept indefinitely; any clean-up needs the owner role and a reviewed procedure |
| A4 masking of personal data in the audit view | Stakeholder | Provisional; easily changed |
| Audit records before M11 have no actor | – | Shown as not recorded; not reconstructed |
| Collected status (B5) | Decide at the start of M12 | Lifecycle unchanged in M11 |

## 10. Integration notes for Milestone 12

- **Audit report (AC-12.5):** build the Management → Audit screen on `/api/manager/audit/changes` and `/security`
  (filters as AC-11.4; pass `nextCursor` for "more"). Show `actor` for system processes and "not recorded" for null.
- **Lookup-change report (AC-12.4):** the same endpoint filtered by `entity` (flavours, toppings, consistencies, their
  price tables, restaurants, system_settings, discount_tiers) gives who changed what from what to what, and when.
- **Patron history (AC-12.1):** extend My orders (currently the 20 newest) with paging and filters; the order page and
  list are already snapshot-based (verified by `HistoricalAccuracyTests`).
- **Export (AC-12.6):** export from the masked API output, never from the raw tables.

## 11. Commit history (M11)

| Commit | Description |
| --- | --- |
| `14d1fa5` | Append-only audit (triggers, guard), audit actors, paging indexes; migration |
| `9ac9263` | Email claims as audited saves |
| `b91d6b6` | Coverage tests (schema, writes versus audits, bypass scan) |
| `8879ecd` | Audit query API; touch updates audited |
| `a19bedb` | Historical-accuracy regression test |
| (this commit) | M11 documentation |
