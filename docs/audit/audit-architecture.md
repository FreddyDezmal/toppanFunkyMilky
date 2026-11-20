# Audit Architecture (Milestone 11)

How every change and transaction is recorded, protected and searched (URS §3.3, §4; roadmap AC-2.5, AC-11.1 to
AC-11.5). Decisions are in [`docs/decisions/2026-11-20_m11-audit-decisions.md`](../decisions/2026-11-20_m11-audit-decisions.md).

## 1. Mechanism (AC-2.5)

| Part | Where | Role |
| --- | --- | --- |
| `AuditingSaveChangesInterceptor` | Infrastructure | Runs before every SaveChanges and adds one `audit_logs` row per inserted, updated or deleted row **to the same SaveChanges** – business change and audit commit or roll back together |
| `audit_logs` | table | `occurred_at`, `action` (Insert, Update, Delete), `table_name`, `record_id`, `changes` (jsonb: `[{field, old, new}]`), `actor`, `user_id`, `user_email`, `trace_id` |
| `security_events` | table (M4) | Sign-ins, failed sign-ins, lockouts, sign-outs: `event_type`, `user_id`, `email`, `ip_address`, `trace_id` |
| `AuditActors`, `AuditActorScope` | Domain, Application | Who acted (section 3) |
| Append-only triggers | migration `20261120174124_AppendOnlyAuditAndActors` | Refuse UPDATE, DELETE and TRUNCATE on both tables (section 4) |
| `AuditQueryService`, `ManagerAuditController` | Infrastructure, API | Read-only search for managers (section 6) |
| `AuditCoverageTests`, `AuditBypassScanTests` | tests | Prove no write path skips the audit (section 7) |

**Audited tables:** every table in the `milkshake` schema except `audit_logs`, `security_events` and the migrations
history – catalogue items and their prices, restaurants, settings, discount tiers, orders and order items, payments
and payment events, refunds, cancellation requests, email notifications, users, roles and the Identity tables.

**Stored per change:** who (actor, user id, user email), what (table, record id, action), old and new value of each
changed field, when (UTC), and the request trace id. Timestamps and the row version are not listed as changes, except
for a "touch" (section 5).

## 2. Transactional integrity

- Audit rows are added to the same SaveChanges as the change, so a failed or rolled-back save leaves no audit row
  (tested with a row-version conflict).
- Keys come from the HiLo sequence before saving, so every audit row names its record.
- The interceptor runs once per SaveChanges; EF Core's retry strategy retries the database work, not the interceptor.
- Concurrent changes are rejected by the row version (`xmin`), and the loser's audit rows roll back with it.

## 3. Actors (AC-11.1 "user")

| Actor | When | User id and email |
| --- | --- | --- |
| `user` | A request with a signed-in patron or manager | The signed-in user |
| `anonymous` | A request without a session (registration, sign-in) | None |
| `system:paystack-webhook` | Paystack payment and refund notifications | None |
| `system:payment-reconciliation` | The reconciliation job | None |
| `system:email-dispatcher` | Email claims and delivery results | None |
| `system:seeding` | The database seeder | None |
| `system:manager-provisioning` | The manager-provisioning command | None |

A system process sets its actor explicitly (`using AuditActorScope.Begin(AuditActors.…)`); the scope always wins over
any HTTP user, so background work is never attributed to a browser user. The database checks the value
(`ck_audit_logs_actor`). Records written before M11 have no actor (shown as not recorded); none is invented.

## 4. Append-only (AC-11.3)

- **Database:** `milkshake.refuse_audit_change()` raises "… is append-only" for any UPDATE or DELETE (row triggers)
  and TRUNCATE (statement triggers) on `audit_logs` and `security_events`, whoever runs it through a normal connection.
- **Application:** the interceptor refuses to save a modified or deleted audit record or security event.
- **API:** the audit endpoints are GET only; POST, PUT, PATCH and DELETE return 405.
- **Limit:** the database owner role can disable triggers. That is outside the application; any future retention
  clean-up (Q-14) needs a reviewed procedure run by the owner.

## 5. Special cases

| Case | Behaviour |
| --- | --- |
| "Touch" updates | Scheduling or cancelling a price touches its catalogue item, and changing minimum or maximum drinks touches the partner setting, so the row version guards the related change. These updates change only `updated_at`; they are audited with that column (found by the coverage test, fixed in M11) |
| Email claims | Locked with `SELECT … FOR UPDATE SKIP LOCKED`, then claimed through the domain and saved normally (audited as `system:email-dispatcher`); the M9 raw `UPDATE` is gone |
| Masked at write | Password hashes, security and concurrency stamps, token values, and the email outbox's address, name and content are stored as `***` |

## 6. Audit query (AC-11.4)

`GET /api/manager/audit/changes` and `GET /api/manager/audit/security` – ManagerOnly, 60 requests per minute per
manager (`RateLimiting:AuditPermitLimit`), never cached.

| Filter | Changes | Security |
| --- | --- | --- |
| `from`, `to` | ISO 8601 UTC, `to` exclusive, at most 366 days apart | same |
| `user` | email (ignoring case) or user id | same |
| `actor` | `user`, `anonymous`, `system:…` | – |
| `entity` | an audited table | – |
| `action` | Insert, Update, Delete | – |
| `eventType` | – | LoginSucceeded, LoginFailed, LoginLockedOut, Logout |
| `q` | free text in table, record id and changes, at most 100 characters, `%` and `_` literal | – |
| `cursor`, `limit` | opaque keyset cursor; 1–100, default 50 | same |

- **Strict:** unknown, repeated or malformed filters return 400 with field errors.
- **Paging:** newest first on `(occurred_at, id)` using the descending indexes.
- **Output:** parsed `{field, old, new}` – never the raw JSON. Account details (email, user name, phone, names) are
  masked; patrons appear as `t***@example.test`, IP addresses as `196.21.x.x`; managers who acted are shown in full.
  Free text never searches inside account changes, so masked values cannot be found by guessing them.
- **SQL:** assembled from fixed fragments with every value as a parameter (free text has to search jsonb).
- **Performance:** each tested filter combination answers within 3 seconds over 100,000 records (Testcontainers).

## 7. Coverage evidence (AC-11.5)

1. **Schema enumeration:** every table in the schema is an audited EF entity or one of three named exclusions.
2. **Writes versus audit rows:** test-only counting triggers (in a separate schema, removed afterwards) count every
   row written to every audited table and every audit row, while a broad run of real write paths executes –
   management, registration, orders, payments by webhook and refresh, cancellations, requests, reconciliation, email
   dispatch, resend. The counts must match per table and action, and every table with a write path must be reached.
3. **Bypass scan:** the build fails on any `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSql…`, `SqlQuery` or `FromSql`
   in the application code beyond the reviewed read-only uses.

## 8. Not here (by design)

- **Audit screen and patron history screens:** M12 (AC-12.1, AC-12.4, AC-12.5) builds on this API.
- **Retention:** open (Q-14, POPIA); nothing is deleted.
- **Collected status (B5):** decided at the start of M12.
