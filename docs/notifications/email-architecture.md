# Email Architecture (Milestone 9)

How the transactional emails are produced, sent, retried and monitored. Decisions D1–D7 are in
[`docs/decisions/2026-11-20_m9-email-decisions.md`](../decisions/2026-11-20_m9-email-decisions.md); setup is in the
[email setup guide](email-setup-guide.md).

**Principle (D7):** an email is a consequence of a business change, never part of it. The change and its queued email
are saved together; sending happens later in the background. An email-provider outage only delays emails: it can never
block, undo or duplicate a payment, cancellation or refund.

## 1. Components

| Part | Where | Role |
| --- | --- | --- |
| `EmailNotification` | Domain | One logical email: kind, unique notification key, recipient, template version, JSON payload, delivery state (claim, sent, suppressed, failed with backoff, resend) |
| `email_notifications` table | Migration `20261120121248_AddEmailNotifications` (applied to Supabase 20 November 2026) | The outbox. Unique `notification_key`; checks on kind, status, last error, counts, lease, sent time, refund and request links and recipient format; `(status, next_attempt_at)` index; restrict-only foreign keys to orders, refunds and cancellation requests |
| `NotificationSaveChangesInterceptor` | Infrastructure | Producer. Before every SaveChanges, finds the email-worthy changes and adds their outbox rows **to the same SaveChanges** (one transaction). Runs before the audit interceptor, so the rows are timestamped and audited |
| `EmailNotificationFactory` | Application | Builds each row's payload from the stored order, payment, refund or request (never from today's prices or settings, AC-9.3) |
| `EmailTemplates` | Application | Pure renderer: subject, HTML and plain text for template version 1 |
| `EmailDispatcher` + `EmailDispatchWorker` | Infrastructure | Consumer. A background job inside the API (like payment reconciliation) that claims due emails, renders, sends and records the result |
| `IEmailSender` | Application / Infrastructure | `MailpitEmailSender` (SMTP capture, development and tests), `ResendEmailSender` (HTTP API, optional real inbox), `DisabledEmailSender` (provider `None`: emails stay queued) |
| Management → Emails | API + React | Delivery status, filters and **Send again** (AC-9.5, D6) |

## 2. Notification matrix (D1, D2)

All emails go to the order's patron, at the address on their account **at the moment of the event** (read on the
server; the browser never supplies a recipient).

| # | Kind | Trigger (detected in the same SaveChanges) | Key (one email per event) | Content |
| --- | --- | --- | --- | --- |
| 1 | `OrderConfirmed` | Order status changes to Confirmed (any of the four payment-verification paths: webhook, return check, patron refresh, reconciliation) | `order-confirmed:{orderId}` | Order and payment confirmation and **final receipt**: order number, pickup restaurant, address and local time, each milkshake with its choices and fees, subtotal, discount, VAT rate and amount, total paid, payment reference and time |
| 2 | `CancellationRequested` | A manager's cancellation request is created | `cancellation-requested:{requestId}` | The reason, the full refund the patron gets if they accept (or "nothing is charged" if unpaid), the pickup-time deadline, and that they must sign in to answer |
| 3 | `OrderCancelled` | Order status changes to Cancelled (patron cancellation, or an accepted request) | `order-cancelled:{orderId}` | Paid amount, fee kept, refund amount and reference, and that the refund is **on its way – not yet completed**; or "nothing was charged" |
| 4 | `RefundProcessed` | A refund becomes Processed (verified with Paystack) | `refund-processed:{refundId}` | Amount refunded, fee kept, refund reference, completion time |
| 5 | `RefundFailed` | A refund becomes Failed | `refund-failed:{refundId}` | That the refund could not be completed, its reference, and to contact the restaurant; states that we never ask for card or bank details by email |

No email for: payments pending or failed, declined or lapsed requests, refunds needing attention (still pending at
Paystack), managers, or identity events (D5, see section 8).

## 3. Template rules

- **One link only:** `{Email:PublicBaseUrl}orders/{reference}` (the reference is URL-escaped). No tokens, no
  accept/decline or other one-click actions: the patron always signs in to act.
- **Every value HTML-encoded**; the recipient's name, the cancellation reason and restaurant and flavour names are
  tested with `<script>`. Subjects have control characters removed (no header injection).
- **No images, scripts, styles blocks or tracking**; inline styles and a 600-pixel table layout for mail clients;
  every email also has a plain-text version.
- **Formatting as the app showed it:** amounts in the stored display locale and currency (another currency is shown as
  `USD 29.90`), times in the restaurant's time zone (UTC if unknown).
- **Wording:** a pending refund is never described as completed; we never ask for card or bank details.
- **Versioned:** each row stores its template version (1). An unknown version, a missing field or an unreadable
  payload cannot be rendered: the email ends as Failed and is logged, never sent with blanks.

## 4. Delivery states

```
Pending ──claim──▶ Sending ──sent──────────────▶ Sent ──(manager: Send again)──▶ Pending
   ▲                 │ ├─temporary failure──────▶ Pending (backoff)
   │                 │ ├─permanent / 8th attempt / unrenderable ─▶ Failed ──(Send again)──▶ Pending
   └─lease expired───┘ └─real-inbox sender, recipient not allow-listed ─▶ Suppressed (final)
```

| Rule | Value |
| --- | --- |
| Run interval | every 30 seconds (`Email:Dispatch:IntervalSeconds`) |
| Batch | 10 emails per run (`Email:Dispatch:BatchSize`) |
| Lease | 5 minutes (`Email:Dispatch:LeaseMinutes`); must exceed batch × sender timeout (10 × 15 s) |
| Backoff | 1, 2, 4, 8 … minutes, at most 60; longer if the provider asks (HTTP 429 `Retry-After`) |
| Attempts | at most 8, then Failed |
| Permanent failures | provider rejection (4xx other than 409/429), invalid recipient, unrenderable email |
| Idempotency | Resend receives the notification key (`…#resend-N` after a manager resend), so a retried request is not delivered twice |

The claim is one SQL statement (`UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING id`), so several
runs or API instances never take the same email. Each result is saved on its own, straight after sending.

## 5. Guarantees and where they are enforced

| Guarantee | Enforced by | Evidence |
| --- | --- | --- |
| Email queued if and only if the business change is saved | Interceptor adds rows to the same SaveChanges | `NotificationCreationTests` (a row-version conflict leaves no email) |
| One email per event | Unique `notification_key`; the interceptor skips keys already queued instead of failing the business save | Duplicate test; retried SaveChanges guard |
| A problem with the email never blocks the business change | Missing account, invalid address or unbuildable payload → skipped and logged | Invalid-address test (cancellation still saved) |
| Provider outage harmless | Dispatcher is separate from every business transaction | `EmailDispatcherTests` (order and payment stay confirmed; email waits) |
| Never sent twice by two runs | `FOR UPDATE SKIP LOCKED` + lease | Three concurrent runs send each email once |
| Real inboxes only for allow-listed recipients | Dispatcher checks `Email:AllowedRecipients` whenever the sender reaches real inboxes (Resend); Mailpit only captures | Suppression tests |
| Content never in the audit trail or logs | Audit masks `recipient_email`, `recipient_name`, `payload`; logs carry ids, kinds and keys only | Persistence and security checks |

## 6. Recovery (runbook)

| Situation | What happens | What to do |
| --- | --- | --- |
| Mailpit or Resend down | Emails wait and retry (up to 8 attempts over about 2 hours) | Nothing; check Management → Emails → Waiting. If they ended as Failed, use **Send again** once the provider is back |
| API stopped while sending | The lease expires after 5 minutes; the next run takes the email back with the same idempotency key | Nothing |
| `Email:PublicBaseUrl` missing, or provider `None` | Nothing is sent; emails stay Pending (a warning is logged for the missing URL) | Configure, restart; queued emails go out |
| Email Failed (rejected or unrenderable) | Logged with its id and kind | Check the log line; fix the cause; **Send again** (for a rejected address, the patron must correct their account email first – a resend uses the stored address) |
| Email Suppressed | The recipient was not on the allow-list for a real-inbox sender | Expected in the demonstration store; it cannot be resent |
| Patron says no email arrived | Find the order under Management → Emails | Check status; **Send again** if Sent or Failed |

## 7. Management → Emails (AC-9.5, D6)

`GET /api/manager/emails?status=&order=` (ManagerOnly): the newest 100 emails with kind, status, order, recipient,
attempts, last error, times and resend count, plus counts per status. Never the subject or content.
`POST /api/manager/emails/{id}/resend` (ManagerOnly, CSRF, 20 per manager per minute, audited): only Sent or Failed
emails; queues the same email (same recipient and content) with fresh attempts and a new idempotency key.

## 8. What is not here (by design)

- **Identity emails** (email confirmation, password reset, change password) are **deferred** (D5): they were
  deferred from M4 and are not part of the M9 requirements. Registration does not promise a confirmation email.
- **Receipt at collection:** the receipt is sent at verified payment (D1); a collection receipt needs the Collected
  status, which is not built.
- **Retention:** how long email records are kept is open (Q-14, POPIA). They are kept with their orders.
- **Bounce and complaint webhooks** from the provider are not processed; delivery state means "accepted by the provider".
