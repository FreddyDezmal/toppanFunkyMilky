# Milky Shaky Custom Drinks – UAT tester pack

Welcome, and thank you for testing. This pack is everything you need for the User Acceptance Test (UAT) of the
**Order Milkshake for Pickup** website. Your results decide whether the release is accepted.

## 1. The site

**https://milkyshaky.onrender.com** – release v1.0.2

- **First visit may take about a minute.** The demonstration runs on free hosting that sleeps after 15 minutes
  without visitors. If the page seems slow at first, wait; after it wakes it responds in a fraction of a second.
- Works on any current Chrome, Edge, Firefox or Safari, on a computer or a phone.

## 2. Your accounts

| You are testing as | How to sign in |
| --- | --- |
| **A patron** (customer) | Choose **Register** and create your own account (script P-01). Use any email address you like; see §4 about emails |
| **A manager** | `toppantester1@milkyshaky.test`, `toppantester2@milkyshaky.test` or `toppantester3@milkyshaky.test` – the one matching your alias. The facilitator gives you its password in person |

In the project records you are identified by your alias (**ToppanTester1**, **ToppanTester2** or **ToppanTester3**).
You write your own name, signature and date only on the sign-off form.

## 3. Paying – no real money

Payments use **Paystack's test mode**. Never enter a real card. The facilitator gives you Paystack's published test
card details at the session (they are also on Paystack's "Test Payments" documentation page). The payment page is
Paystack's own and is marked as test mode.

## 4. Emails

The demonstration has no email domain of its own (decision E-1), so receipts are delivered to **one address only –
the facilitator's**. When you pay with your own patron account, your receipt is still created and recorded; a manager
can see it under **Management → Emails** marked *Suppressed*. The email scripts (P-18 to P-20) are run together on the
facilitator's account, and you read the received email on the facilitator's screen.

## 5. What to do

1. Read the **patron guide** and **manager guide** (`docs/guides/`). They are also under review (AC-14.5): note
   anything unclear.
2. Work through your scripts in `m14-uat-scripts.md`: P-01 – P-22 (patron), M-01 – M-13 (manager), C-01 – C-03
   (whole system), X-01 – X-03 (additional).
3. Under each script, fill in the result table: what actually happened, **Pass** or **Fail**, evidence (a screenshot
   name, an order reference such as `MS-ABCD-1234`, or an email subject), your alias and the date.
4. Anything unexpected is a defect, even if the script passes: tell the facilitator, who writes it in
   `m14-uat-defect-log.md` in your words. You decide or agree its severity (Critical, High, Medium, Low – see the plan,
   §6).
5. At the end, the business owner completes `m14-uat-sign-off.md`, including the decisions listed there.

## 6. Good to know

- **Prices** are the demonstration prices (plan §4). Expected amounts in the scripts use them; if a manager tester has
  changed a price, use the current one.
- **Frequent-customer discounts are switched off** until the business supplies the rates, so every patron sees
  R 0,00 (decision Q-01–Q-04, on the sign-off form).
- **Pickup times** must be at least 15 minutes ahead and at most 7 days ahead (South African time).
- **Managers do not place orders**; use a patron account for that.
- Changing settings or prices affects everyone testing at the same time – agree before changing VAT or the drinks
  limit, and set them back afterwards (scripts M-04, M-05).

## 7. Documents

| File | What it is |
| --- | --- |
| `m14-uat-plan.md` | How UAT runs, roles, environment, severity scale, entry and exit criteria |
| `m14-uat-scripts.md` | The scripts with result tables |
| `m14-uat-defect-log.md` | Defects found during UAT |
| `m14-uat-sign-off.md` | The sign-off form |
| `../guides/patron-guide.md`, `../guides/manager-guide.md` | User guides |
