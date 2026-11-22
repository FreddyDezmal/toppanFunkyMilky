# Milky Shaky Custom Drinks – Manager guide

How managers run the online shop: prices, menu, restaurants, settings, orders, emails, reports and the audit trail.

**Site:** https://milkyshaky.onrender.com (Management is in the menu after signing in)

> **Demonstration site.** Paystack runs in test mode (no real money). Emails go only to the approved address; others
> are listed under Emails as *Suppressed*. After 15 minutes without visitors the site sleeps and the first page takes
> about a minute; background work (sending emails, checking payments) resumes as soon as it wakes.

## 1. Signing in

Manager accounts are created by the system administrator (see the operations guide); managers cannot register on the
site. Sign in with **Log in** and choose **Management**. Managers do not place orders; use a separate patron account
for that.

## 2. The management sections

On a computer the sections are in the sidebar on the left, grouped as *Menu*, *Operations*, *Insights* and *Setup*. On
a phone, choose the **Sections** button at the top of any management page. On a phone, lists such as the menu and
reports show one card per row, with every value and action (Prices, Edit, Deactivate) visible.

| Section | What it is for |
| --- | --- |
| **Overview** | Today's orders and milkshakes, refunds needing attention, email counts (failed, waiting, sent) and the system status, each with a link to its section |
| **Flavours, Consistencies, Toppings** | The menu: add, rename, reorder, take off sale, and set prices |
| **Restaurants** | Pickup restaurants: add, edit name and address, take off or put back on the list |
| **Orders** | Orders and their payment, cancellation requests, refunds that need attention |
| **Emails** | Every email the system sent or tried to send; send one again |
| **Reports** | Orders for a period, trends, and the Audit Lookup |
| **Settings** | Fixed values: drinks per order, VAT, maximum discount, pickup window, refund fee |
| **Discount tiers** | Frequent-customer discount thresholds and rates |

## 3. Prices and the menu

- Open **Flavours**, **Consistencies** or **Toppings**. Add an item with its name and price, or open an item to rename
  it, change its display order, or take it off sale.
- **Prices** keep a history: a new price applies from the date you set. **Orders already placed keep the prices they
  were placed with**; only new orders use the new price.
- An item that is off sale disappears for patrons but still appears on old orders and reports.
- An item that is on sale but has no current price is hidden from patrons until it has one.

## 4. Settings

Each setting shows its current value; choose **Edit**, change it and save. Changes apply to orders placed from now
on.

| Setting | Meaning |
| --- | --- |
| Minimum / maximum drinks per order | How many milkshakes one order may have (the URS range is 1–10) |
| VAT rate | Added to the order total (15 %) |
| Maximum discount amount | Cap on any frequent-customer discount |
| Minimum pickup lead time / maximum advance days | How soon, and how far ahead, a pickup can be booked |
| Refund fee rate | Fee kept when a patron cancels a paid order (10 %) |

## 5. Discount tiers

Three tiers, each with **Paid orders needed**, **Drinks per order** and **Discount**, plus the maximum discount amount
in Settings. **Discounts are currently switched off**: you can prepare the values, but every patron sees R 0,00 until
the business approves the rates and they are switched on.

## 6. Orders

- Enter an **Order reference** (e.g. `MS-7KQ2-X9TD`) to open an order and see the patron, the milkshakes, pickup,
  payment and refunds. To browse orders by date, use **Reports → Orders**; a reference there opens the order here.
- **Ask the patron to cancel** (for example, the restaurant cannot make the order): the patron is emailed and can accept
  (full refund, no fee) or decline. Track answers under **Cancellation requests**.
- **Refunds** lists refunds that need attention: a refund that failed, or one Paystack holds for bank details. Resolve
  these in the Paystack dashboard and contact the patron.
- A payment received with the wrong amount is not confirmed; it is held for a manual refund in Paystack.

## 7. Emails

Every email has a status: **Pending** (queued), **Sending**, **Sent**, **Failed** (gave up after retries) or
**Suppressed** (the address is not on the approved list for this demonstration). Open an email to see its history and
choose to send it again if the patron did not receive it.

## 8. Reports

- **Orders:** orders for a date or a range, filtered by status, with totals. **Export** saves exactly what is on screen
  as a CSV file (up to 10,000 rows).
- **Trends:** order volumes by day of the week (all orders and paid orders), and weekly and monthly views. Exportable.
- **Audit Lookup:**
  - **Lookup changes** – who changed which price, menu item, restaurant or setting, from what to what, and when.
  - Changes to other records (orders, payments, emails) one area at a time, with date filters.
  - Sign-in events (successful and failed).
  Patron personal details are masked here. The audit trail cannot be changed or deleted by anyone.

## 9. Good practice

- Change prices with a start date rather than editing history; never "fix" an old order's price.
- Check the **Overview** daily: it shows refunds needing attention and failed emails at a glance.
- Do not share manager accounts: the audit trail records who made each change.
