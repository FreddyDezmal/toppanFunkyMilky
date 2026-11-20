# Paystack Setup and Webhook Configuration Guide

For developers and for whoever activates payments in production. Never paste keys into chat, documents, issues,
screenshots or committed files.

## Part A – Provider setup

### A1. Accounts

| Environment | Account | Keys |
| --- | --- | --- |
| Development / testing | A developer's Paystack account in **Test mode** (no compliance documents needed for test keys) | `sk_test_…` |
| Production | **The business's own Paystack account** (the stakeholder completes Paystack's compliance/KYC) | `sk_live_…` |

Keys are under **Settings → API Keys & Webhooks** in the Paystack dashboard. Only the **secret key** is used, and only
on the server. The public key is not needed (the app uses Paystack's hosted checkout page, not Paystack JavaScript).

### A2. Configuration keys

| Key | Default | Where |
| --- | --- | --- |
| `Payments:Paystack:SecretKey` | – (required) | user secrets (development); environment variable or secrets store (production) |
| `Payments:Paystack:ReturnBaseUrl` | – (required) | `appsettings.Development.json` = `http://localhost:5173/`; production = the site's public https address, ending in `/` |
| `Payments:Paystack:BaseUrl` | `https://api.paystack.co/` | leave as is |
| `Payments:Paystack:CheckoutHost` | `checkout.paystack.com` | leave as is (the only host the browser is sent to) |
| `Payments:Paystack:TimeoutSeconds` | `15` | |
| `Payments:Reconciliation:Enabled` / `IntervalMinutes` / `BatchSize` | `true` / `5` / `50` | |
| `RateLimiting:WebhookPermitLimit` | `300` per IP per minute | |

Without a secret key, starting a payment returns 503 "Payments are temporarily unavailable", the webhook answers 503
(so Paystack retries later) and the background job skips its runs.

### A3. Store the key (development, Windows PowerShell)

```powershell
$secure = Read-Host 'Paystack secret key' -AsSecureString
$key = [System.Net.NetworkCredential]::new('', $secure).Password
dotnet user-secrets set "Payments:Paystack:SecretKey" $key --project backend/src/Milkshake.Api | Out-Null
Remove-Variable secure, key
# Check it is stored without printing it (expect 1):
(dotnet user-secrets list --project backend/src/Milkshake.Api | Select-String 'Payments:Paystack:SecretKey').Count
```

Production: set the environment variable `Payments__Paystack__SecretKey` (double underscores) in the host's secret
settings, and `Payments__Paystack__ReturnBaseUrl` to the public site address.

### A4. Database

Apply migration `20261120062531_AddPayments` (two new tables, nothing existing changes). Review first:

```powershell
dotnet ef migrations script 20261119191550_AddOrders 20261120062531_AddPayments --project backend/src/Milkshake.Infrastructure --startup-project backend/src/Milkshake.Api -o "$env:TEMP\AddPayments.sql"
dotnet ef database update 20261120062531_AddPayments --project backend/src/Milkshake.Infrastructure --startup-project backend/src/Milkshake.Api
```

Applied to the development Supabase database on 20 November 2026.

### A5. Manual sandbox check

1. Run the API and the frontend; place an order as a patron; click **Pay now**.
2. On `checkout.paystack.com`, pay with a Paystack test card (see Paystack's *Test Payments* page; the standard
   successful card is 4084 0840 8408 4081, any future expiry).
3. Back on `/orders/MS-…?payment=return`: "Checking your payment…" then "Payment received. Your order is confirmed.",
   order status **Confirmed**, payment **Paid**.
4. In the dashboard (Test mode → Transactions) the reference is the app's `MSP-…` reference and the amount matches.

Locally the webhook cannot reach the computer; the confirmation comes from the refresh endpoint and the background
job, which ask Paystack directly.

### A6. Manual sandbox refund (M8b)

1. Pay an order with the test card (A5), then press **Cancel order** on the order page and confirm the shown refund
   (paid − 10% of the price before VAT).
2. The order shows **Cancelled** and the refund "on its way". In the dashboard (Test mode → Refunds or the
   transaction's page) the refund carries our `MSR-…` reference and the amount.
3. Locally no webhook arrives: within 5 minutes the reconciliation job asks Paystack and the refund shows "completed"
   once Paystack has processed it (test refunds may stay pending for a while).
4. Optional: as a manager, use **Management → Orders** to ask the patron of another paid order to cancel; accept as the
   patron and check the full amount is refunded.

## Part B – Webhook configuration

### B1. Endpoint

`POST https://<public API host>/api/payments/paystack/webhook`

Set it in **Settings → API Keys & Webhooks → Webhook URL** (separately for Test and Live mode). It must be a public
HTTPS address; `localhost` cannot receive webhooks. For a local webhook test, a tunnelling tool can expose the API
temporarily; this is optional because recovery works without webhooks.

### B2. How a notification is handled

1. **Size and rate limits:** at most 64 KB; 300 requests per minute per IP. No cookies, session or CSRF token needed.
2. **Signature:** HMAC-SHA512 of the exact raw request body with the secret key, compared in constant time with the
   `x-paystack-signature` header. Missing or wrong → **401**, nothing stored.
3. **Parse:** event type, transaction id and reference only. Malformed → **400**.
4. **Act on `charge.success` and `refund.*`** (pending, processing, needs-attention, processed, failed). Other events and
   unknown references → **200**, no change (payment failures are found by verification instead). A refund event is
   matched by our refund reference (`MSR-…`, any letter case) or, if absent, by the payment reference, and the refund
   is re-read with `GET /refund?reference=MSR-…`; the event's own status is never used. Repeats change nothing.
5. **Deduplicate:** a receipt with the same (provider, event type, transaction id) already stored → **200**, no change.
6. **Re-verify** with Paystack's Verify Transaction API; the event's own amount and status are never used.
7. **Apply** the verified outcome; payment, order, receipt and audit rows are saved together.
8. **Provider unreachable** or the key missing → **503**, so Paystack retries; the background job also catches up.

### B3. Responses Paystack sees

| Situation | HTTP |
| --- | --- |
| Processed, duplicate, other event, unknown reference | 200 |
| Malformed body | 400 |
| Missing or invalid signature | 401 |
| Body over 64 KB | 413 |
| Over the rate limit | 429 |
| Key not configured, or verification temporarily impossible | 503 (Paystack retries) |

### B4. Logging

Logged: payment references, event types, reasons and HTTP status codes. Never logged: request bodies, the
signature, the secret key, patron e-mail addresses or card details.

### B5. Production activation checklist (pending stakeholder)

- [ ] Business Paystack account verified (compliance completed by the stakeholder)
- [ ] Live secret key stored in the production secrets store (not in Git)
- [ ] `ReturnBaseUrl` set to the public https site address
- [ ] Live webhook URL set in the dashboard and one live notification seen in the logs
- [ ] `AddPayments` applied to the production database
- [ ] One low-value live payment and its refund agreed with the stakeholder
