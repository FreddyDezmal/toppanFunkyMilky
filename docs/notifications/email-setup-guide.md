# Email Setup Guide (Milestone 9)

How to run the emails locally, and how to configure the optional real-inbox provider. Architecture:
[email-architecture.md](email-architecture.md).

## 1. Settings (section `Email`)

| Key | Default | Development (`appsettings.Development.json`) | Notes |
| --- | --- | --- | --- |
| `Email:Provider` | `None` (emails stay queued) | `Mailpit` | `None`, `Mailpit` or `Resend` |
| `Email:FromAddress` | – | `orders@milkyshaky.test` | Required by both senders |
| `Email:FromName` | `Milky Shaky Custom Drinks` | same | |
| `Email:PublicBaseUrl` | – | `http://localhost:5173/` | The site address for the order link; without it nothing is sent. Must be `https://` outside Development |
| `Email:AllowedRecipients:0…` | empty | – | Only used for senders that reach real inboxes (Resend). Empty means every email is suppressed |
| `Email:Dispatch:Enabled` | `true` | | Off in automated tests (they call the dispatcher directly) |
| `Email:Dispatch:IntervalSeconds` / `BatchSize` / `LeaseMinutes` | 30 / 10 / 5 | | Keep the lease longer than batch × sender timeout |
| `Email:Mailpit:Host` / `Port` / `TimeoutSeconds` | `localhost` / 1025 / 15 | | |
| `Email:Resend:ApiKey` | – | – | **User secrets only**, never in a file in Git |
| `Email:Resend:BaseUrl` / `TimeoutSeconds` | `https://api.resend.com/` / 15 | | |
| `RateLimiting:EmailResendPermitLimit` | 20 per minute | | Management → Emails **Send again**, per manager |

The database table comes from migration `20261120121248_AddEmailNotifications` (additive; applied to Supabase on
20 November 2026 after reviewing the script, as for the payment migrations).

## 2. Local capture with Mailpit (default)

Mailpit is an SMTP server that keeps every email in a local web inbox; nothing leaves the machine.

```powershell
docker run -d --name mailpit -p 8025:8025 -p 1025:1025 axllent/mailpit
# later: docker start mailpit
```

Open <http://localhost:8025>. Run the API and the frontend, place and pay for an order with a Paystack test card: the
receipt appears within about 30 seconds and is listed as Sent under **Management → Emails**. Without Mailpit running,
emails are retried and finally marked Failed; orders and payments are unaffected, and **Send again** works once
Mailpit is up.

The automated tests start their own Mailpit container (Testcontainers) for the SMTP adapter tests and use a fake sender
elsewhere; they never send real email.

## 3. Optional real-inbox check with Resend

Only with the developer's approval of the sender, recipient and configuration (M9 rule). The demonstration store has
no verified domain, so Resend delivers only to the Resend account owner's own address; the allow-list makes sure
nothing else is attempted.

1. Create a Resend account and an API key with **sending access only**. Do not paste it anywhere.
2. Store it in user secrets without it appearing on screen or in history:

   ```powershell
   cd C:\Users\mohau\Milkey
   $secure = Read-Host 'Resend API key' -AsSecureString
   dotnet user-secrets set "Email:Resend:ApiKey" ([System.Net.NetworkCredential]::new('', $secure).Password) --project backend/src/Milkshake.Api
   Remove-Variable secure
   dotnet user-secrets list --project backend/src/Milkshake.Api | Select-String -SimpleMatch "Email:Resend:ApiKey" | Measure-Object | Select-Object -ExpandProperty Count   # expect 1
   ```

3. For the check only, set in user secrets (not in `appsettings`): `Email:Provider` = `Resend`, `Email:FromAddress` =
   `onboarding@resend.dev` (Resend's test sender while no domain is verified), `Email:AllowedRecipients:0` = the
   Resend account's own address. If your usual address is already taken by a patron in the development database, a
   Gmail plus address (`name+milkyshaky@gmail.com`) works for both the Resend account and a new patron.
4. Place and pay for an order as a patron **whose account email is that address**. Every other patron's email is
   marked Suppressed.
5. Afterwards remove the overrides (`dotnet user-secrets remove "Email:Provider" …`) to return to Mailpit.

## 4. Checks

- Built frontend contains no email settings:
  `Select-String -Path frontend/dist/assets/*.js -Pattern 'api\.resend\.com|re_[A-Za-z0-9]{16}'` (no output expected).
- Logs contain email ids, kinds and keys only – never addresses, subjects or content.
