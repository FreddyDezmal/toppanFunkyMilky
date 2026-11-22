# Milky Shaky redesign — Stage 1: Design audit and proposal

Status: approved 22 Nov 2026 (see Approval record).
Evidence: live site v1.0.2 (https://milkyshaky.onrender.com), viewed 22 Nov 2026 at 1280×800 and 375×812 as a patron and as a manager (view only; nothing saved, exported or resent), plus a read-only review of `frontend/src`.

## 1. Current UI/UX weaknesses (prioritised audit)

What already works and must be kept: every field has a real label, hint and error wiring (`aria-describedby`, `aria-invalid`); `ConfirmDialog` traps focus and returns it; axe passes; Review and Order details pages are well structured; the Trends chart is clear.

| # | Severity | Finding | Pages | Recommendation |
|---|---|---|---|---|
| A1 | High | The home page is still the build-time test page: "Welcome / Foundation build…", and the System status card is shown to the public. There is no hero, brand or call to action. | Home | New landing page (section 5). Show System status only to managers (Manager overview). |
| A2 | High | The builder uses three native dropdowns per drink, with prices buried in option text ("Strawberry (R 32,00)"). The estimate sits below the fold, and on mobile a finished drink collapses into a plain text line. | Order → Milkshakes | Card-based builder with a sticky summary (section 6). |
| A3 | High | Internal or outdated text is visible to patrons: "You will not be charged now: payment is not available yet" (wrong since M8), "Pickup times are provisional…", "Development build" in every footer, and restaurant addresses that read "Development sample data". | Review, Pickup, all pages | Fix the copy in the frontend. The restaurant address is database data, not code: decision D-R6. |
| A4 | High | On mobile the header wraps to three rows (logo / links / "Hi, name" + Log out). Management tabs wrap to four rows on mobile and two on desktop, leaving "Discount tiers" on its own line. | All, Management | Compact header with a menu button on mobile. Sidebar for management on desktop and a section menu on mobile (section 7). |
| A5 | Medium | Every protected page first shows a full blank screen with "Checking your session…". The nav then jumps when links appear after sign-in is confirmed. | All protected pages | Keep the header frame steady and show a skeleton in the content area. Reserve space for the account area. |
| A6 | Medium | There is no design system. The only tokens are six brand pinks. Buttons are written out inline about five different ways. Page widths vary (xs/sm/md/xl/2xl/3xl/4xl). There is no type scale, and fonts are the system default. The hot pink (#db2777) looks generic. | All | Tokens and shared components (section 4). |
| A7 | Medium | Management tables scroll sideways inside their card on mobile with no cue. On Flavours, Status and the Prices/Edit/Deactivate actions sit off-screen (table 561 px wide in a 295 px box). | Catalogue ×3, Restaurants, Discount tiers, Prices, Reports | Stacked row cards under 640 px; tables at 640 px and wider. |
| A8 | Medium | Order status is plain text ("Confirmed · Paid"), with no visual difference between paid, unpaid, cancelled and refund states. | My orders, Order details, Manager orders | Status badges with text plus colour (never colour alone). |
| A9 | Medium | The Quantity step is one dropdown on an otherwise empty page. The step pills wrap on mobile. | Order → Quantity, all order steps | Stepper buttons (−/+) within the configured min/max. Compact "Step 2 of 4" stepper on mobile. |
| A10 | Medium | Manager Overview is empty ("Choose a section above…", "Manager access confirmed"). | Manager | Overview built only from data the API already returns (section 7). No invented metrics. |
| A11 | Low | Small text carries important information: text-xs hints, the fee explanation, the price note. | Builder, Order details, Settings | Type scale with a 14 px minimum for meaningful text. |
| A12 | Low | Native date and datetime pickers look different on every browser and show "yyyy/mm/dd" placeholders. | Pickup, Reports | Keep the native inputs (best for accessibility and validation) and style their frames. Group pickup date and time clearly. |
| A13 | Low | DEF-04 (estimate layout shift) is handled with a placeholder. A new layout could bring the shift back. | Builder, Review | Fixed-height summary rows and a regression check in E2E. |

## 2. Proposed visual direction — "Soda-fountain modern"

The feel is warm, creamy and friendly, like a modern milk bar, not a toy shop. That means cream backgrounds, chocolate-brown type and strawberry and pistachio as flavour accents. Corners are soft and rounded (16–24 px on cards). There are a few hand-drawn original SVG shapes: a glass, drips, sprinkles. One confident accent colour marks actions. The patron side is expressive. The manager side uses the same tokens in a calmer, denser layout.

## 3. Colour palette and typography

Contrast was measured with the WCAG 2.x formula.

| Token | Hex | Use | Contrast |
|---|---|---|---|
| cream | #FFF8F1 | Page background | — |
| white | #FFFFFF | Cards | — |
| chocolate | #58352E | Body text, headings, secondary buttons | 10.2:1 on cream, 10.7:1 on white ✔ |
| warm-grey | #746C68 | Secondary text, on cream or white only | 4.9:1 on cream ✔. Fails on pink tint (4.36): never use it there. |
| strawberry | #F4A6B8 | Decoration, highlights, illustration | White on it 1.9:1 ✘. **Never** text or a button fill. Chocolate text on it 5.6:1 ✔ (tags). |
| **raspberry** (new) | #B3264F | **Primary actions**, links, selected-card border | White on it 6.35:1 ✔; 6.0:1 on cream ✔ |
| raspberry-dark | #9E1F45 | Hover/pressed | White on it 7.7:1 ✔ |
| strawberry-tint | #FDE7EC | Selected card fill, soft panels | Chocolate on it 9.1:1 ✔ |
| pistachio | #B9D9C3 | Secondary accent fills | Chocolate on it 7.0:1 ✔; 1.5:1 against white, so it can't be a boundary |
| pistachio-deep (new) | #2F6B48 | "Paid/Active/Ready" text and success | 6.3:1 on white ✔ |
| amber-deep / error-red | #8A5A00 / #B42318 | Warning / error text | 5.9:1 / 6.6:1 ✔ |

Rule: strawberry and pistachio can't meet the 3:1 non-text contrast needed for borders and focus rings. Selected states, borders that matter and focus rings therefore use raspberry or chocolate, plus a ✓ mark, so selection never depends on colour alone.

**Typography (needs approval, D-R1):**
- Display: **Fraunces** (SIL Open Font License), a soft, warm serif for headings and the wordmark.
- Text: **Plus Jakarta Sans** (SIL OFL), a friendly, readable sans for UI and body text.
- Loading: self-hosted Latin-subset woff2 files in `frontend/public/fonts`, two weights each, `font-display: swap`, heading font preloaded. Committing the font files avoids adding an npm package or calling Google's servers, so the CSP stays unchanged. The licence file goes alongside.
- Scale: 14 / 16 / 18 / 22 / 28 / 36 / 48 px; body 16 px; line height 1.5.

## 4. Design-system architecture

- **Tokens** in `src/index.css` `@theme`: colours above, `--font-display/--font-sans`, radius (sm 8, md 12, lg 20, pill), shadows (soft, raised), spacing on Tailwind's scale, content widths (`--container-narrow` 28rem, `--container-page` 72rem), motion durations. The old `brand-*` tokens are kept as aliases during migration, then removed.
- **Primitives** in `src/components/ui/`: `Button` (primary/secondary/ghost/danger; sizes; `asChild`-free link variant), `Card`, `Badge`/`StatusBadge`, `Alert`, `Skeleton`, `EmptyState`, `PageHeader`, `Stepper` (progress), `QuantityStepper` (−/+), `Tabs` (reports), `DataTable` (table ≥640 px, stacked cards below), `Pagination`, `Icon` (about 15 hand-written inline SVG icons, no icon library), `ConfirmDialog` (existing, restyled).
- **Form controls** in `src/components/forms/`: `TextField` and `SelectField` restyled with the same API. New `ChoiceCardGroup`: a real `<fieldset>`/`<legend>` with native `<input type="radio">` inside each card, so keyboard, screen-reader and form behaviour come for free.
- **Layouts**: `AppLayout` (patron), `AuthLayout` (split screen), `ManagementLayout` (sidebar), `OrderLayout` (stepper plus summary rail).
- Every primitive gets Vitest tests and axe checks. No new dependencies.

## 5. Landing-page concept

1. **Hero**: "Your Milkshake. Your Rules." with "Pick your flavour, choose your consistency, add your favourite topping, and collect your perfect shake." Primary CTA **Build Your Milkshake** (to /order, or to sign-in first). Secondary: "My orders" when signed in, "Log in" otherwise. An original SVG milkshake glass whose colours cycle through real flavours (static when reduced motion is on).
2. **How it works**: three steps (Choose → Pay → Collect), taken from the real flow.
3. **The menu**: real catalogue only, loaded from the API: the 7 flavours, 4 consistencies and 6 toppings with their current prices, shown as chips or cards ("from R 30,00").
4. **Pickup**: active restaurant names from the API (addresses only once real ones are set, D-R6).
5. Footer: wordmark and links. No testimonials, ratings, awards, opening hours or claims.

## 6. Milkshake-builder concept (highest priority)

- **Layout, desktop**: left column is the builder; right column is a **sticky summary** (drinks, discount, VAT, total from the server estimate, "Continue" button). The summary has fixed row heights and a reserved price area, so it can't shift (DEF-04).
- **Layout, mobile**: single column. A **sticky bottom bar** shows "2 milkshakes · R 124,20 · Continue", and its sheet opens the full summary.
- **Per drink**: tabs or an accordion "Milkshake 1 / 2…". Three sections, Flavour → Consistency → Topping, each a `ChoiceCardGroup` of radio cards with the name, price ("+ R 10,00" / "Included" for R 0) and a small decorative swatch. The selected card gets a raspberry border, a tint fill and a ✓. Exactly one choice per section stays mandatory, exactly as today.
- **Quantity**: −/+ stepper limited to the server-configured min/max, with a live count.
- **Step indicator**: Quantity → Milkshakes → Pickup → Review, showing done, current and upcoming, with `aria-current="step"`.
- **Unchanged rules**: the server stays the authority on price (the UI shows the estimate endpoint's figures, never its own sums), quantity limits, pickup validation and the idempotency key. The draft keeps using sessionStorage.
- **Pickup**: restaurant as radio cards, date and time as styled native inputs, validation messages unchanged.

## 7. Manager-dashboard concept

- **Desktop**: a left sidebar grouped as *Menu* (Flavours, Consistencies, Toppings, Prices), *Operations* (Orders, Refunds, Emails), *Insights* (Reports), *Setup* (Restaurants, Settings, Discount tiers), and a slim top bar with "Manager" and Log out. Denser type, neutral surfaces, raspberry only for primary actions.
- **Mobile**: a "Sections" button opens a full-height menu (a dialog with focus trap) in place of four rows of tabs.
- **Overview**: only cards backed by existing endpoints: today's orders and milkshakes (Reports "single date" query), refunds needing attention (existing list), emails failed or waiting (existing counts), and System status (moved here from the public home page). Each card links to its section. No new metrics, no changes to calculations.
- **Tables**: `DataTable` with clear row actions; below 640 px they become stacked cards with all actions visible (A7).

## 8. Responsive strategy

Mobile first. Breakpoints are Tailwind defaults: 640 (sm, tables), 768 (md, two-column auth and order details), 1024 (lg, sticky summary rail and management sidebar). Test widths: 320, 375, 768, 1024, 1280, 1440. Content stays within 72 rem. Touch targets are at least 44×44 px. Nothing scrolls sideways at 320 px except data inside a labelled scroll region. Text reflows at 200 % zoom.

## 9. Accessibility considerations (WCAG 2.2 AA)

- Contrast as tabled. Visible 3 px raspberry or chocolate focus ring with offset (2.4.7, 2.4.11 not obscured: sticky bars get `scroll-padding`).
- Radio cards stay native radios in a fieldset/legend, with arrow-key navigation. Selection is shown by ✓ and border, not colour alone.
- Status badges always carry text. The live estimate total is announced politely (`aria-live`) without repeating every keystroke.
- Target size 24 px minimum (2.5.8), 44 px in practice. No drag-only actions.
- Headings in order on every page, a skip link, landmarks, the page `<title>` updated per route, and focus moved to the page heading on route change.
- `prefers-reduced-motion`: decorative animation and transitions are turned off.
- The existing Vitest/axe and Playwright/axe suites are run at every stage, with axe added to the new primitives.

## 10. Files likely to change

- **Tokens/fonts**: `frontend/src/index.css`, `frontend/index.html` (preload, meta), new `frontend/public/fonts/*` and `OFL.txt`, new `frontend/public/illustrations/*.svg` (original).
- **New UI**: `src/components/ui/{Button,Card,Badge,StatusBadge,Alert,Skeleton,EmptyState,PageHeader,Stepper,QuantityStepper,Tabs,DataTable,Pagination,Icon}.tsx`, `src/components/forms/ChoiceCardGroup.tsx`.
- **Restyled**: `components/forms/{TextField,SelectField}.tsx`, `components/ui/ConfirmDialog.tsx`, `components/layout/AppLayout.tsx`, new `components/layout/AuthLayout.tsx`, `features/auth/RequireAuth.tsx` (skeleton).
- **Patron pages**: `pages/HomePage.tsx`, `LoginPage.tsx`, `RegisterPage.tsx`, `AccountPage.tsx`, `NotFoundPage.tsx`, `pages/ordering/{OrderQuantityPage,OrderDrinksPage,OrderPickupPage,OrderReviewPage}.tsx`, `features/ordering/{OrderLayout,DrinkCard,PriceSummary,EmptyDraft,PaymentPanel,CancellationPanel,CancellationRequestNotice}.tsx`, `pages/orders/{MyOrdersPage,OrderDetailsPage}.tsx`.
- **Manager**: `features/management/ManagementLayout.tsx` (sidebar), `pages/ManagerPage.tsx` (overview), all `pages/management/*.tsx` (tables to `DataTable`), `features/management/{TrendChart,AuditPanel,ExportButton}.tsx` (restyle only), `features/system-status/SystemStatusCard.tsx` (moved).
- **Tests**: matching `*.test.tsx`. In `e2e-local/support/stack.ts` and the ordering specs, `selectOption` for Flavour/Consistency/Topping/Quantity/Pickup restaurant becomes `getByRole('radio', { name })` or the stepper buttons. This is a justified change from select to radio. Labels and names stay the same.
- **Not touched**: anything in `backend/`, `features/*/api.ts` contracts, auth and session logic, payment and webhook and refund code, the CSP.

## 11. Implementation sequence (one stage per approval)

| Stage | Content | Estimate |
|---|---|---|
| 2 | Tokens, fonts, primitives, restyled form controls, AppLayout/AuthLayout, auth skeleton | 1–1.5 days |
| 3 | Landing page, Login/Register/Account, 404 | 0.5–1 day |
| 4 | Builder: quantity, drink cards, sticky summary, pickup, review, DEF-04 check, selector updates | 1.5–2 days |
| 5 | Checkout/payment panel, My orders, Order details, cancellation | 0.5–1 day |
| 6 | Management sidebar, overview, DataTable across all sections, reports styling | 1.5–2 days |
| 7 | Accessibility and responsive pass (320–1440, zoom, keyboard, reduced motion), full Vitest + Playwright + axe | 0.5–1 day |
| 8 | Release notes, screenshots, deploy **only on your explicit approval** | 0.5 day |

Total is about 6–9 working days of effort. Each stage ends with tests green and screenshots for your review.

## 12. Decisions requiring approval

- **D-R1 Fonts**: Fraunces + Plus Jakarta Sans (both OFL), self-hosted woff2 committed to the repo. Alternative: keep system fonts.
- **D-R2 Primary action colour**: add raspberry #B3264F for buttons and links, with strawberry pink kept for decoration only.
- **D-R3 Imagery**: original hand-made SVGs only (glass, drips, sprinkles, swatches per flavour, about 6–10 files). No photos and no stock or third-party art. Alternative: no illustration.
- **D-R4 Icons**: about 15 hand-written inline SVG icons, with no icon package added.
- **D-R5 Test selectors**: allow updating builder, quantity and pickup E2E/Vitest selectors from `selectOption` to radio and stepper interactions (same accessible names). No test is removed or weakened.
- **D-R6 Restaurant addresses**: patrons currently see "Development sample data". Options: (a) leave it and hide addresses that equal that text, (b) you edit the addresses through the Manager UI (a data change I won't make), (c) leave as is.
- **D-R7 Copy fixes**: replace the stale "payment is not available yet" line, the "Pickup times are provisional" note and the "Development build" footer, and move System status to the manager overview.
- **D-R8 Quantity control**: −/+ stepper in place of the dropdown (same min/max from the server).
- **D-R9 Scope check**: no backend changes are expected. If one turns out to be needed, I'll stop and ask separately.

## Approval record

22 Nov 2026: the project owner approved the design direction and decisions D-R1 to D-R9. For D-R6, option **(c)** was chosen: restaurant addresses are left as they are (no data change, no hiding).

## Stage log

| Stage | Status | Notes |
|---|---|---|
| 1 Audit and proposal | Approved 22 Nov 2026 | This document |
| 2 Design system | Done, awaiting approval | Tokens and self-hosted fonts in `src/index.css`; shared components in `src/components/ui` and `src/components/forms`; new header (mobile menu, skip link, no "Development build"); session skeleton. Existing pages pick up the palette through the remapped `brand-*` aliases. Tabs, DataTable and Pagination are built with the stages that use them (5 and 6); AuthLayout with Stage 3. |
