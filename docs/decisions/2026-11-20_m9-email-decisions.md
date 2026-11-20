# Decision Register – Milestone 9 (Email Notifications and Receipts)

**Date:** 20 November 2026
**Decided by:** the developer, on the assistant's recommendations. *Provisional* items are business rules the
stakeholder should ratify.

**Requirements:** URS §3.1 (payment receipt and order confirmation emailed to the patron's registered address), URS §4
(confirmation email of orders and payments; final receipt by email), roadmap AC-9.1 to AC-9.5, T9.1 to T9.4, R-08.

| ID | Decision | Status |
| --- | --- | --- |
| D1 | Q-08: **one combined email** at verified payment, serving as order confirmation, payment confirmation and final receipt (AC-9.2 content). A receipt at collection would need the Collected status (B5, not built) | Provisional (Q-08) |
| D2 | Notification matrix (all to the order's patron, address read from the account on the server): (1) order confirmed with receipt; (2) manager cancellation request; (3) order cancelled; (4) refund processed; (5) refund failed. No email for "needs attention" refunds, pending payments, declined or lapsed requests, failed payments, or managers | Approved; emails 2–5 provisional (from M8b decisions, not the URS) |
| D3 | Provider: one sender interface with two adapters – **Mailpit** (local capture; default in development and automated tests) and **Resend** (optional, for manual real-inbox checks; our notification id sent as the idempotency key). SendGrid excluded (no free plan since 2025) | Approved |
| D4 | Recipient policy: a server-side **allow-list**; any other recipient is never sent to and the email is marked suppressed. Without a verified domain, Resend itself can only deliver to the account owner's address | Approved |
| D5 | Identity emails (email confirmation, password reset, change password; deferred from M4) are **not** part of M9; documented as deferred to a later phase | Approved |
| D6 | AC-9.5: Management → Emails – delivery status list and **Resend** (same recipient and content, never a new address), ManagerOnly, CSRF, rate-limited, audited | Approved |
| D7 | Outbox architecture: rows created by a SaveChanges interceptor in the same transaction as the business change (covers all four payment-confirmation paths); unique key per logical email; background dispatcher like the payment reconciliation job; PostgreSQL row claims with `FOR UPDATE SKIP LOCKED` and a lease; exponential backoff; a provider outage never affects payments or refunds | Approved (schema detailed in Step 3) |
| – | Retention of email records: **open** (Q-14, POPIA); no policy invented | Open |
