# Progress Report – Milestone 9: Email Notifications and Receipts

**Project:** Order Milkshake for Pickup (Milky Shaky Custom Drinks)
**Report date:** 20 November 2026
**Period covered:** 20 November 2026
**Status:** M9 complete. Patrons receive an order confirmation with the final receipt when their payment is verified,
and emails for cancellation requests, cancellations and refund outcomes. Emails are queued in the same transaction as
the business change and sent by a background dispatcher with retries, so an email-provider outage never affects a
payment, cancellation or refund. Managers see delivery status and can send an email again. Local delivery is captured
by Mailpit; one approved real-inbox check through Resend succeeded (section 8).
**Requirements traced:** URS §3.1 and §4 (confirmation and receipt emails), roadmap AC-9.1–AC-9.5, T9.1–T9.4, R-08;
M9 decisions D1–D7 (`docs/decisions/2026-11-20_m9-email-decisions.md`); M8b integration notes.

Related: [email architecture](../notifications/email-architecture.md) ·
[email setup guide](../notifications/email-setup-guide.md) · [M8b report](2026-11-20_milestone-8b-cancellation-refunds-report.md)

---

## 1. Summary

- **Outbox (D7):** an `email_notifications` row is added by a SaveChanges interceptor in the same transaction as the
  order, cancellation, request or refund change, with one unique key per logical email. All four payment-verification
  paths are covered without changing them.
- **Five emails (D1, D2):** order confirmed with receipt; manager cancellation request; order cancelled; refund
  processed; refund failed. Content comes from the stored records (AC-9.3).
- **Templates:** HTML and plain text, every value encoded, one link to the order page, no tokens or one-click actions,
  never asks for card or bank details, pending refunds never described as completed.
- **Dispatcher:** every 30 seconds; row claims with `FOR UPDATE SKIP LOCKED` and a 5-minute lease; backoff 1–60 minutes,
  8 attempts; provider idempotency keys; allow-list suppression for real-inbox senders (D4).
- **Senders (D3):** Mailpit (SMTP capture) by default in development; Resend verified with one approved real-inbox check.
- **Management → Emails (AC-9.5, D6):** status list with counts and filters; **Send again** for sent or failed emails
  (ManagerOnly, CSRF, rate-limited, audited).
- **Migration** `AddEmailNotifications` reviewed (no DROP/ALTER/DELETE/UPDATE) and applied to Supabase.

## 2. Decision register (implementation)

Decisions D1–D7 are in the M9 decision register. Made during implementation:

| ID | Decision | Status | Basis |
| --- | --- | --- | --- |
| E1 | Outbox rows are created by an interceptor that detects status changes (order → Confirmed or Cancelled, request added, refund → Processed or Failed), so no payment or cancellation code path can forget an email | Approved | D7 |
| E2 | A missing account, invalid address, duplicate key or unbuildable payload skips the email (logged without the address) instead of failing the business save | Approved | The payment or refund matters more than its email |
| E3 | The recipient is the patron's account email read at event time; changing the account email later does not change queued emails (a resend uses the stored address) | Approved | Never trust a browser-supplied recipient |
| E4 | Payloads are read strictly (required fields, no unexpected nulls); an unrenderable email ends as Failed with error `Rejected` and is logged, never sent with blanks | Approved | No retry can fix it |
| E5 | The claim is a raw SQL statement, so the claim itself is not in the audit trail; every outcome (sent, failed, suppressed, resent) is audited | Approved | Atomic claim with `SKIP LOCKED` |
| E6 | Managers see the full recipient address on Management → Emails, as they already see the patron's email on Management → Orders (I8) | Provisional | Needed to check delivery problems (POPIA, Q-14) |
| E7 | Suppressed emails cannot be resent (they would be suppressed again) | Approved | D4 |
| E8 | Delivery status means "accepted by the provider"; bounce and complaint webhooks are not processed | Approved | Demonstration store; Resend optional |

## 3. Design

| Part | Contents |
| --- | --- |
| Domain | `EmailNotification` (kinds, statuses, delivery errors, keys, recipient rule, claim, sent, suppressed, failed with backoff, resend, idempotency key) |
| Database | Migration `20261120121248_AddEmailNotifications`, additive: `email_notifications` with unique key, checks (kind, status, error, counts, lease, sent time, refund and request links, recipient format), status/next-attempt index, restrict-only foreign keys. Audit masks `recipient_email`, `recipient_name`, `payload` |
| Application | `EmailPayloads` (versioned payload records), `EmailTemplates` (renderer), `EmailNotificationFactory`, `IEmailSender`, `IEmailDispatcher`, `IManagerEmailService` |
| Infrastructure | `NotificationSaveChangesInterceptor` (registered before the audit interceptor), `EmailDispatcher` and `EmailDispatchWorker`, `MailpitEmailSender`, `ResendEmailSender`, `ManagerEmailService`, `EmailOptions` |
| API | `GET /api/manager/emails`, `POST /api/manager/emails/{id}/resend`; rate-limit policy `email-resend` |
| React | Management → **Emails** (`/manager/emails`): status filters with counts, order filter, delivery details, Send again |

## 4. Requirements traceability

| Requirement | Status | Evidence |
| --- | --- | --- |
| AC-9.1 Order and payment confirmation on payment success (combined per Q-08) | **Met** (combined, D1) | `NotificationCreationTests` (one email queued when the payment is verified, to the patron's account address); manual check: email captured in Mailpit |
| AC-9.2 Final receipt: order number, drinks with flavour/consistency/topping, unit prices, discount, VAT, total, restaurant, pickup time, payment reference | **Met** | `EmailTemplateTests` (each item asserted, including the fee of each choice and the drink price) |
| AC-9.3 Values from the order snapshot, not current configuration | **Met** | `EmailNotificationFactoryTests`; the payload is copied from the stored order, payment, refund or request and saved with the email |
| AC-9.4 A failed send is logged and retried automatically; never rolls back a payment | **Met** | `EmailDispatcherTests` (backoff, rate-limit wait, 8 attempts, lease recovery, sender exceptions; outage leaves order and payment confirmed) |
| AC-9.5 Managers see delivery status and resend | **Met** | `ManagerEmailsTests` (list, filters, resend, 409/404, CSRF, roles, rate limit, audit); Vitest `emails.test.tsx` |
| MR-05, MR-08, FR-07 (URS §3.1, §4) | **Met** | Through AC-9.1 to AC-9.4 |
| R-08 Delivery failures or spam filtering | **Partly mitigated** | Retry queue, delivery log and resend, never blocks payment. Sender-domain authentication (SPF/DKIM/DMARC) not done: the demonstration store has no domain |
| M8b notes: cancellation, request and refund emails; reason only to the patron | **Met** | Matrix emails 2–5; all go to the order's patron only |
| D5 Identity emails | **Deferred** | Documented; no screen promises a confirmation or reset email |

## 5. Security controls with evidence

| Control | Evidence |
| --- | --- |
| No real email without approval | Default provider `None`; development uses Mailpit; Resend only with a stored key, and then only to allow-listed recipients (suppression tests) |
| Recipient never from the browser | Read from the patron's account on the server; single plain address enforced by domain and database check |
| HTML injection | Every value encoded; `<script>` in name, reason, restaurant and flavour tested |
| Header injection | Subject control characters removed; recipient and sender name validated; reference with CR/LF tested |
| No tokens or one-click actions | One link to the order page; tests assert a single `href`; requests answered only after sign-in |
| No bank or card requests | Failed-refund email states we never ask for them (tested) |
| Management endpoints | ManagerOnly (patron 403, anonymous 401), CSRF (400 without token), 20 resends per minute (429), audited with the manager's email |
| Secrets | Resend key only in user secrets; no keys in `appsettings`, Git or the built bundle (scans clean) |
| Logging and audit | Logs carry email ids, kinds and keys only; audit masks address, name and content |

## 6. Test results (executed 20 November 2026)

| Suite | Count | Result |
| --- | --- | --- |
| Backend unit + integration (Testcontainers PostgreSQL 17, Mailpit) | 929 (777 at M8b + 152) | Passed (929/929) |
| Vitest | 164 (156 at M8b + 8) | Passed |
| TypeScript, oxlint, build `-warnaserror` | – | Passed |
| Playwright (with axe) | 9 | 7 passed; the 2 manager tests skipped (they run only with `E2E_MANAGER_*` set in the terminal) |

New in M9:

| Area | Tests |
| --- | --- |
| Outbox domain (`EmailNotificationTests`) | 29 |
| Templates (`EmailTemplateTests`) | 38 |
| Payload factory (`EmailNotificationFactoryTests`) | 15 |
| Outbox persistence | 15 |
| Senders (Resend stub 17, Mailpit container 2) | 19 |
| Email creation in the business transaction | 9 |
| Dispatcher | 13 |
| Management → Emails API | 14 |
| Management → Emails page (Vitest) | 8 |

## 7. Issues encountered and resolutions

| Issue | Resolution |
| --- | --- |
| A test cancellation request named a random manager id, which the database's foreign key rejected | The test uses the test manager's account |
| A stored payload with missing fields could have rendered an email with blanks or stopped the dispatcher | Payloads are read strictly; such emails end as Failed (E4, tests added) |
| Filtering on a record built inside an EF Core query cannot be translated (as in M8b) | The email list filters and sorts on the joined rows, then builds the records |
| For the Resend check, the developer's usual address was already registered as a patron (created 18 November during M4 testing; password unknown, no reset yet, D5) | A Gmail plus address was used for both the Resend account and a new patron; the old account was left unchanged |
| The development database lacked the new table, so saves would have failed locally once the interceptor existed | Migration reviewed and applied to Supabase before running the app again |

## 8. Manual check (20 November 2026)

With Mailpit running, the developer paid an order with a Paystack test card: the order confirmation and receipt arrived
in Mailpit and showed as Sent under Management → Emails.

**Real inbox (Resend), approved by the developer the same day:**
- **Configuration:** sender `onboarding@resend.dev`, Resend's test sender for accounts without a verified domain.
  The only allow-listed recipient was the developer's own Resend account address (a Gmail plus address).
- **Where the settings lived:** the API key (sending access only), provider, sender and allow-list were set as
  temporary user-secret overrides and never entered in the conversation.
- **Result:** a patron registered with that address paid an order, and the receipt arrived in the developer's Gmail
  inbox. Resend only accepts recipients equal to the account owner's address, and every other recipient is suppressed
  by the allow-list before Resend is called.

## 9. Open items

| Item | Needed from | Effect |
| --- | --- | --- |
| D1 (single receipt at payment), emails 2–5, E6 confirmation | Stakeholder | Provisional; easily changed |
| Identity emails (D5) | Later phase | Email confirmation, password reset, change password not available |
| Q-14 retention (POPIA) | Stakeholder | Email records kept with their orders |
| Playwright manager journey with M9 | Developer (manager credentials in the terminal) | Optional; the patron journeys passed with M9 |
| Sender domain with SPF/DKIM/DMARC (R-08) | A real store's domain | Needed before emailing real patrons from a real domain |

## 10. Integration notes for Milestone 11

- **My orders / order page:** may show "receipt emailed" from the order's `OrderConfirmed` email status (Sent); never
  show the address to anyone but the patron.
- **New business events** (e.g. a Collected status, B5) get emails by adding a `NotificationKind`, a payload record, a
  template and a detection rule in the interceptor; bump the template version for changed content.
- **Retention (Q-14):** when decided, a clean-up job must keep emails linked to orders that are still kept.

## 11. Commit history (M9)

| Commit | Description |
| --- | --- |
| `1aff4e3` | Outbox entity, table and migration; audit masking; M9 decisions |
| `ffa1b07` | Mailpit and Resend senders, allow-list option |
| `1969406` | Payloads and templates |
| `bca1dc3` | Emails queued in the same save as orders, cancellations and refunds |
| `726d55b` | Dispatcher with leases, backoff and suppression |
| `6978069` | Management → Emails API |
| `88f0a55` | Management → Emails page |
| (this commit) | M9 documentation |
