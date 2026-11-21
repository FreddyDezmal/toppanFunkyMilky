# M14 UAT scripts

**Version:** draft 1, 21 November 2026 · Drafted from Development Roadmap §11 and the features as built; check the
requirement wording against URS v1.1 when it is available (see `m14-uat-plan.md`).

**How to use:** follow the steps in order, then fill in the result table under the script.
**Pass** = every expected result happened. **Fail** = any expected result did not happen (log a defect and write its
number in "Defect ref"). Evidence can be a screenshot file name, an order reference (for example `MS-ABCD-1234`) or an
email subject and time. Leave nothing blank: write "n/a" if a column does not apply.

Prices below use the demonstration data in the plan (§4). Amounts are shown as the site shows them, e.g. `R 55,20`.

## Coverage

| Group | Scripts | Requirements |
| --- | --- | --- |
| Patron | P-01 – P-22 | FR-01–FR-07, FR-10–FR-18, FR-24, MR-05–MR-09 |
| Manager | M-01 – M-13 | FR-09, FR-19–FR-23, FR-25–FR-27, MR-02–MR-04, MR-10 |
| Whole system | C-01 – C-03 | MR-01, FR-08, FR-28 |
| Additional (built, not in §11) | X-01 – X-03 | Cancellation and refund (B1.1–B1.4), restaurant-requested cancellation, keyboard and phone use |

Every one of the 38 in-scope requirements has exactly one script. Index at the end.

---

## Patron scripts

### P-01 · FR-01 · Create an account with mobile number, first name and email

**Role:** visitor · **Preconditions:** you are not signed in; you have an email address on the approved list.

1. Open the site and choose **Register**.
2. Submit the form empty.
3. Enter your first name, an invalid email (e.g. `abc`) and an invalid mobile number (e.g. `123`); submit.
4. Correct the email and mobile number (e.g. `+27 82 123 4567`), enter a password and the same password again; submit.

**Expected:** step 2 and 3 show a message beside each field that needs fixing and create no account; step 4 creates
the account and you are signed in (your first name is shown and **My account** shows your first name, email and
mobile number).

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-02 · FR-02 · Log in with an existing account

**Role:** patron · **Preconditions:** the account from P-01; you are signed out.

1. Choose **Log in** and enter your email with a wrong password.
2. Enter an email that has no account.
3. Enter your email and the correct password.

**Expected:** steps 1 and 2 show the same general message (it does not reveal whether the email exists) and do not
sign you in; step 3 signs you in and shows your first name. After five wrong passwords in a row the account is locked
for a short time (optional check: tell the facilitator first).

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-03 · MR-07 · At least two user profiles with separate logins

**Role:** patron and manager · **Preconditions:** a patron account (P-01) and a manager account from the facilitator.

1. Signed in as the patron, look for **Management** in the menu, then type `/manager` after the site address.
2. Sign out. Sign in as the manager. Open **Management**.
3. As the manager, try to start an order with **Order**.

**Expected:** step 1: the patron has no Management link and is refused the management area; step 2: the manager sees
Overview, Flavours, Consistencies, Toppings, Restaurants, Orders, Emails, Reports, Settings and Discount tiers;
step 3: the manager is refused patron ordering (managers do not place orders).

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-04 · FR-03 · Place an order for milkshakes

**Role:** patron · **Preconditions:** signed in as a patron.

1. Choose **Order**. Enter **2** for **Number of milkshakes** and continue.
2. Milkshake 1: Strawberry, Thick, Oreo crumbs; choose **Done**. Milkshake 2: Bar one, Double thick, Frozen
   Strawberries; choose **Done**.
3. Choose **Continue to pickup**, pick a restaurant and a time at least an hour from now, continue.
4. On the review page check the details, then choose **Place order** and confirm.

**Expected:** the review page lists both milkshakes, the restaurant and the time; after confirming, the order has a
reference (`MS-…`), appears under **My orders** and is waiting for payment (it is not confirmed yet – see P-16).

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-05 · FR-10 · Number of drinks: compulsory, numeric, 1–10, maximum configurable

**Role:** patron · **Preconditions:** signed in as a patron; maximum drinks setting is 10 (M-04 tests changing it).

1. Choose **Order**. Leave **Number of milkshakes** empty and continue.
2. Try `0`, then `11`, then `abc`, then `2.5`.
3. Enter `10` and continue; go back and enter `1` and continue.

**Expected:** step 1 and every value in step 2 are refused with a clear message and you cannot continue; `10` and `1`
are accepted.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-06 · FR-18 · One Drink Detail container per drink ordered

**Role:** patron · **Preconditions:** signed in as a patron.

1. Order **3** milkshakes and continue.
2. Count the milkshake sections, then go back and change the number to **1**.

**Expected:** step 1 shows exactly three sections (Milkshake 1, 2, 3), each with its own flavour, consistency and
topping; after step 2 there is exactly one.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-07 · FR-11 · Flavour drop-down with 7 values

**Role:** patron · **Preconditions:** on the milkshakes page of a new order.

1. Open the **Flavour** list of milkshake 1.

**Expected:** 7 flavours: Strawberry, Vanilla, Chocolate, Coffee, Banana, Oreo, Bar one (each with its price). A
flavour must be chosen before the milkshake can be marked done.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-08 · FR-12 · Thick-or-not (consistency) drop-down with 4 values

**Role:** patron · **Preconditions:** on the milkshakes page of a new order.

1. Open the **Consistency** list of milkshake 1.

**Expected:** 4 values: Double thick, Thick, Milky, Icy (each with its price). A consistency must be chosen.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-09 · FR-13 · Topping drop-down with 6 values

**Role:** patron · **Preconditions:** on the milkshakes page of a new order.

1. Open the **Topping** list of milkshake 1.

**Expected:** 6 values: Frozen Strawberries, Freeze-dried banana, Oreo crumbs, Bar one syrup, Coffee powder with
chocolate, Chocolate vermicelli (each with its price). A topping must be chosen.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-10 · FR-17 · "Done" available only when the container is complete

**Role:** patron · **Preconditions:** on the milkshakes page of a new order with 1 milkshake.

1. Choose only a flavour and try **Done**.
2. Add a consistency and try **Done**.
3. Add a topping and choose **Done**.

**Expected:** in steps 1 and 2 the milkshake is not marked done and the missing choices are pointed out; in step 3
it is marked done and shows a summary of the three choices with an option to edit.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-11 · FR-14 · Read-only price per drink including topping and consistency

**Role:** patron · **Preconditions:** on the milkshakes page with 2 milkshakes.

1. Milkshake 1: Strawberry, Thick, Oreo crumbs. Milkshake 2: Bar one, Double thick, Frozen Strawberries.
2. Try to type into or change the prices.

**Expected:** milkshake 1 shows **R 48,00** (32 + 6 + 10) and milkshake 2 shows **R 60,00** (38 + 10 + 12); prices
cannot be edited.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-12 · FR-15 · Read-only frequent-customer discount, 0 if none

**Role:** patron · **Preconditions:** the two milkshakes from P-11.

1. Look at **Frequent-customer discount** in the estimate.

**Expected:** shows **R 0,00** and cannot be edited.
**Note:** discounts are currently switched off (decision Q-01–Q-04/B2 – the stakeholder has not supplied the tier
rates), so every patron sees R 0,00. If the stakeholder supplies rates and they are switched on before UAT, add a
step with a patron who meets tier 1.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-13 · FR-16 · Read-only order total with VAT included

**Role:** patron · **Preconditions:** the two milkshakes from P-11.

1. Read the estimate: **Subtotal**, **Frequent-customer discount**, **VAT (15 %)**, **Total**.
2. Continue to the review page and compare the total.

**Expected:** Subtotal **R 108,00**, discount **R 0,00**, VAT **R 16,20**, Total **R 124,20**; the review page shows
the same total; none of the amounts can be edited.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-14 · FR-04 · Select the pickup restaurant

**Role:** patron · **Preconditions:** an order at the pickup step.

1. Open **Pickup restaurant**.
2. Try to continue without choosing one.
3. Choose a restaurant and continue.

**Expected:** the list shows the active restaurants (2 sample branches) with their addresses; step 2 is refused;
the review page and later the order show the chosen restaurant.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-15 · FR-05 · Select a suitable pickup time

**Role:** patron · **Preconditions:** an order at the pickup step.

1. Choose a time in the past.
2. Choose a time 5 minutes from now.
3. Choose a time more than 7 days ahead.
4. Choose tomorrow at a normal hour.

**Expected:** steps 1–3 are refused with a message explaining the allowed window (at least 15 minutes from now, at
most 7 days ahead, South African time); step 4 is accepted and shown on the review page and the order.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-16 · FR-06 · Payment is made before the order is confirmed

**Role:** patron · **Preconditions:** the placed, unpaid order from P-04.

1. Open the order under **My orders**. Note its payment status.
2. Choose **Pay now**; on the Paystack test page, leave without paying (close or cancel).
3. Back on the site, open the order again.
4. Choose **Pay now** again and pay with the Paystack test card.

**Expected:** steps 1–3: the order is **Unpaid**, not confirmed, and **Pay now** is still offered; step 4: after
returning, the order changes to **Paid** (confirmed) – it may take a few seconds while the site checks with Paystack.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-17 · MR-06 · Use of a payment gateway

**Role:** patron · **Preconditions:** a new unpaid order.

1. Choose **Pay now**.
2. Check the page address and the amount on the payment page.
3. Pay with the Paystack test card.

**Expected:** the payment page is Paystack's (shown as test mode), the amount equals the order total, the card details
are entered on Paystack's page and never on the Milky Shaky site, and the order becomes **Paid**.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-18 · FR-07 · On successful payment, email a payment receipt and order confirmation

**Role:** patron, using the facilitator's patron account (decision E-1: only that address receives email) ·
**Preconditions:** an order paid with that account; the facilitator's inbox open on screen. A manager tester can also
confirm under **Management → Emails** that the other testers' receipts were created (shown as *Suppressed*).

1. Check the inbox (and spam folder) within a few minutes of paying.

**Expected:** one email from Milky Shaky Custom Drinks, subject "Order MS-… confirmed – your receipt", confirming the
order **and** the payment (decision Q-08/D1: one combined email), with the order reference, the drinks, the restaurant and pickup time, the amount paid and a link to
the order. No email is sent for the attempt that was not paid.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-19 · MR-05 · Confirmation email of orders and payments

**Role:** patron · **Preconditions:** the email from P-18.

1. Compare the email with the order on the site.

**Expected:** reference, each milkshake (flavour, consistency, topping, price), discount, VAT, total, restaurant,
pickup time and payment confirmation all match the site.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-20 · MR-08 · Final receipt by email

**Role:** patron · **Preconditions:** the email from P-18.

1. Read the email as a receipt you would keep.

**Expected:** it reads as a final receipt: business name, order reference, date paid, items and amounts, VAT shown
separately, total paid in rand, the date paid and the payment reference; you would not need anything else as proof of purchase.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-21 · FR-24 · Patron can check order history

**Role:** patron · **Preconditions:** at least two orders (one paid, one unpaid).

1. Open **My orders**.
2. Open each order.
3. Sign in as a second patron (or ask another tester) and compare.

**Expected:** your orders are listed newest first with reference, date, pickup, total and status; each opens with its
full details; the second patron does not see your orders.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### P-22 · MR-09 · User / patron report(s)

**Role:** patron · **Preconditions:** the orders from P-21.

1. Review **My orders** as your personal report of your purchases.

**Expected:** it gives a usable summary of your orders (what, when, where, how much, paid or not) without needing to
contact the restaurant. Note any information you expected but did not find.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

---

## Manager scripts

### M-01 · MR-02 · Configurable fees

**Role:** manager · **Preconditions:** signed in as a manager; a patron tester ready to quote an order.

1. Open **Management → Flavours** and open the prices of **Vanilla**.
2. Set a new price (e.g. R 31,00) from now on and save.
3. Ask the patron tester to start a new order with a Vanilla milkshake.

**Expected:** the new price is saved with its start date, the old price is kept in the price history, and the
patron's new quote uses the new price.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-02 · FR-19 · Fees per flavour, per topping, per consistency; VAT 15 %

**Role:** manager · **Preconditions:** signed in as a manager.

1. Open **Flavours**, **Consistencies** and **Toppings** in turn and open the prices of one item in each.
2. Open **Settings** and find the VAT rate.

**Expected:** each flavour, consistency and topping has its own price that the manager can change; VAT is 15 % and is
a setting the manager can change (do not change it in this script).

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-03 · MR-03 · Lookup table manager

**Role:** manager · **Preconditions:** signed in as a manager.

1. **Toppings:** add a topping named `UAT test topping` with a price; check it appears for patrons.
2. Rename it to `UAT topping (renamed)`.
3. Deactivate it (remove it from sale); check patrons no longer see it.
4. Do the same with a restaurant under **Restaurants** (add, edit the address, deactivate).

**Expected:** each change saves with a confirmation, patrons see added items and stop seeing deactivated ones, and
existing orders that used an item still show it.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-04 · FR-21 · Manage lookups and fixed values (minimum/maximum drinks, VAT, etc.)

**Role:** manager · **Preconditions:** signed in as a manager; a patron tester ready.

1. Open **Settings** and note the current values (minimum and maximum drinks per order, VAT, maximum discount, pickup
   lead time and advance days, refund fee).
2. Change the maximum drinks per order from 10 to **5** and save.
3. Ask the patron tester to order 6 milkshakes, then 5.
4. Set the maximum back to **10**.

**Expected:** the setting saves with a message that it applies to orders placed from now on; 6 is refused and 5 is
accepted; after step 4, 10 is allowed again.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-05 · FR-22 · Changes affect new transactions only

**Role:** manager with a patron tester · **Preconditions:** a **paid** order containing Vanilla placed before M-01
(or place one now, then change the price).

1. Note the Vanilla price on the existing order (patron's **My orders**, and the manager's **Orders** found by its
   reference).
2. Change the Vanilla price again.
3. Re-open the existing order; ask the patron to start a new order with Vanilla.

**Expected:** the existing order keeps its original prices, VAT and total; only the new quote uses the new price.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-06 · FR-20 · Frequent-customer discount configuration (5 settings)

**Role:** manager · **Preconditions:** signed in as a manager.

1. Open **Discount tiers**. Read the notice at the top.
2. For tier 1, set **Paid orders needed**, **Drinks per order** and **Discount**; save. Do the same for tiers 2 and 3.
3. Open **Settings** and set the maximum discount amount.
4. Reload the pages.

**Expected:** all five URS settings can be set (tier-1 minimum orders, minimum drinks per qualifying order, tier-2
and tier-3 thresholds, maximum discount amount) and are kept after reloading. The notice says discounts are switched
off: the values are prepared but have no effect on orders until the stakeholder approves rates (decision Q-01–Q-04).
**Stakeholder decision to record on the sign-off form:** keep discounts off, or supply rates to switch them on.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-07 · FR-23 · Audit of lookup management changes

**Role:** manager · **Preconditions:** the changes made in M-01 to M-06.

1. Open **Reports → Audit Lookup**, choose **Lookup changes**.
2. Find the price change, the topping rename and the settings change.

**Expected:** each change is listed with who made it, when, the item, and the value before and after.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-08 · MR-04 · Full auditing on any field changes and transactions

**Role:** manager · **Preconditions:** a paid order from the patron scripts; the changes from M-01 to M-06.

1. In **Audit Lookup**, look at the changes for the paid order (order created, payment, status change, email).
2. Look at sign-in events.
3. Check how patron details appear.

**Expected:** every change to the order, its payment and its emails is recorded with what changed, from what to what,
when and by whom (a person, or the system for automatic steps); sign-ins and failed sign-ins are recorded; patron
personal details are masked in the audit views (decision M11 A4). Audit entries cannot be edited or deleted.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-09 · FR-09 · Full audit of transactions with a report and user-operable filters

**Role:** manager · **Preconditions:** as M-08.

1. In **Audit Lookup**, filter by a date range, then by area (e.g. orders, prices), then clear the filters.
2. Export the filtered list as CSV and open it.

**Expected:** each filter narrows the list as expected and clearing restores it; the CSV contains the same rows as the
screen.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-10 · FR-25 · Orders per selected period

**Role:** manager · **Preconditions:** orders placed today by the patron testers.

1. Open **Reports → Orders**. Choose today's date.
2. Filter by status (e.g. paid only, then unpaid only).
3. Choose a date range; then a date with no orders.
4. Export as CSV and compare with the screen.

**Expected:** the orders listed match what the testers placed (reference, pickup, status, total); filters work; an
empty period says there are no orders; the CSV matches the screen.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-11 · FR-26 · Customer order volumes per day of the week (trend)

**Role:** manager · **Preconditions:** orders placed during UAT.

1. Open **Reports → Trends**.
2. Read the day-of-week chart, then the weekly and monthly views.
3. Export as CSV.

**Expected:** the day-of-week chart shows order counts per weekday (today's weekday includes the UAT orders), the
bars distinguish all orders and paid orders (decision R2), and the export matches the chart.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-12 · FR-27 · Lookup change report: who changed what to what

**Role:** manager · **Preconditions:** the changes from M-01 to M-06.

1. Open **Reports → Audit Lookup → Lookup changes**; filter to today.
2. Export the lookup changes as CSV.

**Expected:** a report of each lookup or setting change, showing who changed it, which item, from what value to what
value, and when; the CSV matches.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### M-13 · MR-10 · Management report(s)

**Role:** manager · **Preconditions:** M-10 to M-12 done.

1. Review **Reports** (Orders, Trends, Audit Lookup), **Orders** (find an order by reference, cancellation requests,
   refunds needing attention) and **Emails** as the management reporting of the business.

**Expected:** a manager can see today's and past orders, payment and refund state, emails sent or failed, trends and
the audit of changes without help from the developer. Note any report you expected but did not find.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

---

## Whole-system scripts

### C-01 · MR-01 · Use of a database

**Role:** patron and manager · **Preconditions:** P-04 and M-01 done.

1. Sign out and back in (or use another browser or device) and open your orders and the manager's price change.
2. Ask the facilitator to show the read-only data integrity check results for the UAT period.

**Expected:** everything entered earlier is still there from any device; the integrity checks report no problems.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### C-02 · FR-08 · All details stored and updated in a database

**Role:** patron and manager · **Preconditions:** a paid order.

1. Compare the order as the patron sees it, as the manager sees it under **Orders**, and in the email.
2. Change your mobile number under **My account** (if offered) or ask the manager to view the order's patron details.

**Expected:** the same details everywhere (one stored record); status changes (unpaid → paid) show for both patron and
manager without re-entering anything.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### C-03 · FR-28 · Funky, modern website with its own look and feel

**Role:** every tester · **Preconditions:** the testers have used the site in the other scripts, on a computer and a
phone.

1. Give your opinion: does the site look modern and distinctive (not a template)? Is it easy to read and use on a
   phone? Would you be happy to show it to customers?

**Expected:** the business owner agrees the look and feel is acceptable for the demonstration. Write specific changes
you want as defects (usually Low or Medium).

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

---

## Additional scripts (built features outside the §11 catalogue)

### X-01 · Cancellation by the patron with a fee and refund (decisions B1.1–B1.4)

**Role:** patron · **Preconditions:** a paid order with a pickup time still in the future.

1. Open the order and choose **Cancel order**; read the explanation, then confirm.
2. Wait a few minutes and refresh the order; check your inbox.

**Expected:** the explanation states the cancellation fee (10 % of the amount paid) and the refund amount before you
confirm; the order becomes Cancelled; the refund is shown as pending, then completed once Paystack confirms; you receive
an email about the cancellation and refund.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### X-02 · The restaurant asks the patron to cancel (full refund, no fee)

**Role:** manager with a patron tester · **Preconditions:** a paid order of the patron tester with a future pickup.

1. Manager: **Orders** → find the order → **Ask the patron to cancel**, with a reason.
2. Patron: open the order (and the email) and choose **Accept cancellation**. Repeat with a second order and decline.

**Expected:** the patron is told why; accepting cancels the order with a full refund and no fee; declining keeps the
order paid with no refund; the manager sees each outcome under **Cancellation requests** and **Refunds**.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

### X-03 · Keyboard and phone use

**Role:** patron · **Preconditions:** a phone, and a computer with a keyboard.

1. On a phone: register or sign in, order one milkshake, and pay.
2. On a computer: do the same using only the keyboard (Tab, Shift+Tab, Enter, Space, arrow keys).

**Expected:** no sideways scrolling on the phone; every step can be completed with the keyboard alone, with a visible
focus outline showing where you are.

| Actual result | Pass / Fail | Evidence ref | Tester | Date | Defect ref |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

---

## Index: requirement → script

| Req | Script | Req | Script | Req | Script | Req | Script |
| --- | --- | --- | --- | --- | --- | --- | --- |
| MR-01 | C-01 | MR-06 | P-17 | FR-04 | P-14 | FR-14 | P-11 |
| MR-02 | M-01 | MR-07 | P-03 | FR-05 | P-15 | FR-15 | P-12 |
| MR-03 | M-03 | MR-08 | P-20 | FR-06 | P-16 | FR-16 | P-13 |
| MR-04 | M-08 | MR-09 | P-22 | FR-07 | P-18 | FR-17 | P-10 |
| MR-05 | P-19 | MR-10 | M-13 | FR-08 | C-02 | FR-18 | P-06 |
| FR-01 | P-01 | FR-02 | P-02 | FR-09 | M-09 | FR-19 | M-02 |
| FR-03 | P-04 | FR-10 | P-05 | FR-11 | P-07 | FR-20 | M-06 |
| FR-12 | P-08 | FR-13 | P-09 | FR-21 | M-04 | FR-22 | M-05 |
| FR-23 | M-07 | FR-24 | P-21 | FR-25 | M-10 | FR-26 | M-11 |
| FR-27 | M-12 | FR-28 | C-03 | | | | |

38 requirements, 38 scripts. B-01 and B-02 (mobile app) are tested in M15.
