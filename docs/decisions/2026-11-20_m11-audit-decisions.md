# Decision Register – Milestone 11 (Auditing and Transaction History)

**Date:** 20 November 2026
**Decided by:** the developer, on the assistant's recommendations. *Provisional* items await stakeholder ratification.

**Requirements:** URS §3.3 and §4 (full audit of field changes and transactions, searchable by management); roadmap
AC-11.1 to AC-11.5, T11.1 to T11.5, MR-04, FR-09, FR-23, R-06.

| ID | Decision | Status |
| --- | --- | --- |
| A1 | **Scope follows the roadmap:** M11 is the audit trail. Patron history screens, paging and filters (AC-12.1), the audit screen (AC-12.5) and the lookup-change report (AC-12.4) belong to M12. One historical-accuracy regression test is added now | Approved |
| A2 | **Actor on every audit record:** `user`, `anonymous` or a named system process (`system:paystack-webhook`, `system:payment-reconciliation`, `system:email-dispatcher`, `system:seeding`, `system:manager-provisioning`), set explicitly at each entry point; a system scope always wins over a signed-in user. Pre-M11 rows stay without an actor | Approved |
| A3 | **Sign-in events in the audit query** (T11.3): a second, read-only endpoint over `security_events` | Approved |
| A4 | **Personal data masked on output**, kept as stored: account email, user name, phone and names; security-event emails and IP addresses; managers who acted shown in full. Free text does not search inside account changes | Provisional (Q-14) |
| A5 | **API now, screen in M12** (AC-12.5) | Approved |
| A6 | **Collected status (B5)** is not part of M11; decide at the start of M12 | Approved |
| A7 | **Append-only** in the database (triggers refusing UPDATE, DELETE, TRUNCATE on `audit_logs` and `security_events`) and in the application (interceptor guard); the owner role's ability to disable triggers is documented | Approved |
| A8 | **Email claims are audited saves** (row locks, then the domain claim) instead of the M9 raw `UPDATE` | Approved |
| A9 | **"Touch" updates are audited** with the columns that changed (normally `updated_at`), so every UPDATE has an audit row | Approved (found by the coverage test) |
| A10 | **Two endpoints** (`/audit/changes`, `/audit/security`) instead of one with a `source` switch: the two sources return different shapes | Approved |
| A11 | **Coverage test** with test-only counting triggers, plus a source scan for raw-SQL and bulk writes with a reviewed allow-list | Approved |
| – | Retention of audit records: **open** (Q-14, POPIA); nothing is deleted or anonymised | Open |
