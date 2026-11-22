# Personal-data retention policy (Q-14 / B6)

**Status:** written by the project owner on 22 November 2026 for the demonstration; **to be confirmed by the
stakeholder**. Not automated yet: in the demonstration it is applied by hand, and the system holds test data only.

## 1. What personal data the system keeps

| Data | Where | Why |
| --- | --- | --- |
| First name, email address, mobile number, password hash | Patron and manager accounts | Signing in; order and payment emails; contacting a patron about an order |
| Orders (items, prices, pickup, restaurant, status) | Orders | Making the order; refunds; reports |
| Payment and refund references | Payments, refunds | Matching Paystack transactions; refunds |
| Email delivery records | Email notifications | Showing managers whether an email was sent |
| Audit records (who changed what, with personal values masked in views) | Audit log | Accountability (M11) |

No card details are stored: payments are handled by Paystack.

## 2. How long it is kept

- **Patron accounts and their orders:** kept for **5 years after the patron's last order** (or after the account was
  created, if the patron never ordered).
- After that period, the patron's **personal details are removed** (anonymised): name, email address and mobile
  number are replaced with placeholders, and the account can no longer sign in.
- **Order totals, items, dates and restaurants are kept** without the personal details, so reports and accounting
  totals stay correct. Order history snapshots are never rewritten in any other way.
- **Audit records** are kept; personal values in them are masked in every view and removed together with the
  account's personal details.
- **Manager accounts:** personal details removed within 3 months after the manager leaves, keeping the audit trail
  of what they did under a placeholder name.

## 3. Requests from a patron

- **Access:** a patron can ask what is held about them; a manager answers within 30 days.
- **Correction:** wrong details are corrected on request.
- **Deletion:** personal details are removed on request (the same anonymisation as in section 2) unless an open order,
  payment or refund still needs them; in that case they are removed once it is settled. Order totals are kept.

## 4. How it is carried out

- Demonstration: by hand, on request, by a manager with database access, recorded in the audit log.
- Later (future work): a scheduled job that anonymises accounts past the retention period, with a report for managers.
- Never: dropping or resetting the database, deleting audit history or rewriting historical order snapshots.

## 5. Review

Reviewed when the stakeholder confirms or changes it, and at least once a year after go-live.
