# Milky Shaky Custom Drinks — Complete UI/UX Redesign

## Your Role

Act as my Senior UI/UX Designer, Creative Director, Frontend Architect, React Engineer, Accessibility Specialist and Design Systems Engineer.

We have completed and submitted the **Order Milkshake for Pickup** application for Milky Shaky Custom Drinks.

The application is functionally complete, but its current visual presentation feels like a low-fidelity prototype.

Your task is to transform the existing frontend into a **beautiful, professional, modern, responsive and production-quality web application**.

The redesign should make Milky Shaky feel like a genuine, premium milkshake brand rather than a university project prototype.

**This is a visual redesign and usability improvement project, not a backend redevelopment.**

Do not start implementing immediately. First inspect the existing frontend, understand the application journeys, and present a redesign proposal for my approval.

---

## 1. Design Vision

Create a visually distinctive milkshake ordering experience that feels:

- Playful and welcoming.
- Premium without being overly formal.
- Modern and visually polished.
- Colourful but not overwhelming.
- Easy to navigate.
- Fast and responsive.
- Accessible.
- Trustworthy during checkout and payment.
- Consistent across Patron and Manager interfaces.

Think of a modern boutique dessert or specialty drinks brand.

Avoid the appearance of generic admin templates, default browser controls, unstyled forms, oversized empty spaces or disconnected components.

The final application should feel intentionally designed.

---

## 2. Proposed Visual Identity

Use this palette as a starting proposal, not an unchangeable specification.

| Colour | Suggested hex | Purpose |
|---|---|---|
| Vanilla Cream | `#FFF8F1` | Main background |
| Strawberry Pink | `#F4A6B8` | Brand accents |
| Chocolate Brown | `#58352E` | Primary text and buttons |
| Pistachio Green | `#B9D9C3` | Secondary accents |
| Soft White | `#FFFFFF` | Cards and elevated surfaces |
| Warm Grey | `#746C68` | Secondary text |

Consider a richer accent for primary actions if the softer pink lacks sufficient contrast.

### Typography

Propose a deliberate pairing:

- An expressive display font for headings and brand moments.
- A highly readable sans-serif for body text, forms, tables and navigation.

Use appropriately licensed fonts with efficient loading.

### Visual Language

- Rounded cards and buttons.
- Soft, restrained shadows.
- Warm surfaces.
- Clear spacing hierarchy.
- Attractive milkshake photography or illustrations.
- Tasteful gradients.
- Distinctive iconography.
- Elegant form fields.
- Clear interactive states.
- Subtle transitions and microinteractions.

Avoid excessive glassmorphism, distracting animation and visual clutter.

All colour combinations must meet applicable WCAG AA contrast requirements.

---

## 3. Existing Technology

Preserve the current stack:

- React 19
- TypeScript
- Vite 8
- Tailwind CSS v4
- Existing routing
- Existing authentication
- Existing API contracts
- Existing form validation
- Existing accessibility and security controls

Inspect the repository before recommending additional packages.

Prefer CSS, Tailwind and existing dependencies over introducing a large component framework.

Do not add dependencies without approval.

---

## 4. First Task — UI/UX Audit

Inspect every existing page and shared component.

Identify:

1. Visual inconsistencies.
2. Poor spacing and alignment.
3. Weak typography.
4. Low-contrast elements.
5. Inconsistent buttons.
6. Unattractive forms.
7. Unclear information hierarchy.
8. Confusing navigation.
9. Excessive empty space.
10. Weak mobile responsiveness.
11. Missing hover and focus states.
12. Unpolished loading and error states.
13. Layout shifts.
14. Areas that feel like low-fidelity wireframes.

Capture screenshots of the current application at desktop and mobile widths.

Review the actual rendered interface, not only the source code.

Create a prioritised audit with severity, affected pages and recommended improvements.

---

## 5. Design System

Before redesigning individual screens, create a reusable design system.

Define:

### Foundations

- Colour tokens.
- Typography scale.
- Spacing scale.
- Border radii.
- Shadows.
- Breakpoints.
- Container widths.
- Motion durations.
- Focus styles.
- Icon usage.

### Components

Design and implement reusable versions of:

- Primary, secondary and destructive buttons.
- Text inputs.
- Select controls.
- Checkboxes and radio controls.
- Cards.
- Badges.
- Tabs.
- Navigation.
- Modals.
- Toast notifications.
- Alerts.
- Skeleton loaders.
- Empty states.
- Tables.
- Pagination.
- Form validation messages.
- Page headings.
- Confirmation panels.

Document component variants and states.

Use semantic HTML and accessible interactions.

Avoid unnecessary duplication.

---

## 6. Landing Page Redesign

Make the landing page feel like a real milkshake brand.

### Hero Section

Create an attractive hero with:

- A bold, memorable headline.
- A short, compelling description.
- A beautiful milkshake image or illustration.
- A prominent **Build Your Milkshake** call to action.
- A secondary action where appropriate.

Suggested copy:

**Your Milkshake. Your Rules.**

Pick your flavour, choose your consistency, add your favourite topping, and collect your perfect shake.

### Additional Sections

Consider:

- How it works.
- Featured flavours.
- Customisation highlights.
- Pickup convenience.
- Clear navigation to ordering.
- A polished footer.

Do not invent testimonials, customer ratings, awards, store locations or business claims.

Use real catalogue information where available.

---

## 7. Authentication Pages

Redesign:

- Registration.
- Login.
- Authentication-related errors.

Use a cohesive, welcoming layout.

Consider a split-screen design on desktop, with branding or milkshake imagery on one side and the form on the other.

On mobile, use a clean single-column layout.

Preserve:

- Existing authentication flow.
- Cookie handling.
- CSRF protection.
- Validation.
- Error handling.
- Accessibility.

Do not weaken security for visual convenience.

---

## 8. Milkshake Builder — Highest Priority

The ordering experience should be the visual centrepiece of the application.

Redesign the existing steps:

1. Choose milkshake components.
2. Choose restaurant and pickup time.
3. Review the order.
4. Continue to payment.

### Component Selection

Instead of plain form controls, consider attractive selectable cards for:

- Flavours.
- Consistencies.
- Toppings.

Each card may include:

- Name.
- Relevant imagery or icon.
- Price information.
- Clear selected state.
- Hover and keyboard-focus states.

Use actual catalogue data and prices from the API.

Do not invent products or prices.

### Order Summary

Create a prominent, attractive order summary showing:

- Selected components.
- Quantity.
- Current estimated price.
- Pickup details where relevant.
- Clear next action.

On desktop, consider a two-column layout with a sticky summary panel.

On mobile, use a compact, accessible summary that does not obscure content.

### Progress Indicator

Add a polished step indicator showing where the Patron is in the ordering journey.

### DEF-04

Resolve the previously reported layout shift where the live price estimate pushes the **Continue to pickup** button downward.

The redesign must prevent unexpected movement when pricing loads.

### Critical Constraint

Preserve all existing business rules, including exact component-selection requirements, server-authoritative pricing, quantity limits, pickup validation and order idempotency.

---

## 9. Checkout and Payment

Redesign the review and payment experience to inspire confidence.

Use:

- Clear order summaries.
- Well-aligned price breakdowns.
- Readable VAT and total amounts.
- Clear payment status.
- Helpful loading indicators.
- Distinct success and failure states.
- Accessible confirmation messages.

Make it obvious that payments use Paystack's hosted checkout.

**Paystack must remain in test mode only.**

Do not alter payment verification, webhook handling, amount calculation or refund logic.

---

## 10. My Orders

Transform the Patron order history into a polished, easy-to-scan experience.

Each order should clearly display:

- Order reference.
- Date.
- Milkshake details.
- Pickup information.
- Total.
- Order status.
- Payment or refund status.

Consider responsive order cards on mobile and a structured list on desktop.

Use distinct status indicators with both text and colour.

Keep cancellation actions clearly separated from ordinary navigation.

Preserve pagination and Patron data isolation.

---

## 11. Manager Dashboard

The Manager interface should look professional, but remain consistent with the Milky Shaky brand.

Use a more restrained visual style than the Patron-facing pages.

Redesign:

- Manager navigation.
- Catalogue management.
- Restaurant configuration.
- Pricing.
- Settings.
- Discounts.
- Cancellation requests.
- Refund-related views.
- Email notification monitoring.
- Reporting.
- Audit Lookup.

### Dashboard Layout

Consider:

- A desktop sidebar.
- A compact mobile navigation pattern.
- Clear page titles.
- Consistent action buttons.
- Search and filter controls.
- Responsive tables.
- Status indicators.
- Confirmation dialogs.
- Useful empty states.

### Reporting

Improve the presentation of:

- Orders report.
- Weekly trends.
- Monthly trends.
- Growth charts.
- Audit changes.
- Sign-in events.
- CSV export actions.

Charts should be visually appealing without distorting their meaning.

Do not invent metrics or alter report calculations.

---

## 12. Responsive Design

The entire application must work well on:

- Small mobile phones.
- Larger phones.
- Tablets.
- Laptops.
- Desktop monitors.

Test at representative viewport widths.

Requirements:

- No unintended horizontal scrolling.
- No clipped buttons.
- No overlapping content.
- Readable typography.
- Touch-friendly targets.
- Functional mobile navigation.
- Responsive tables.
- Correct modal sizing.
- Stable order summaries.

Prioritise the mobile Patron experience.

---

## 13. Motion and Microinteractions

Add subtle, purposeful motion where it improves usability.

Examples:

- Button hover feedback.
- Card selection transitions.
- Step changes.
- Loading skeletons.
- Success confirmations.
- Smooth panel expansion.
- Toast notifications.

Respect `prefers-reduced-motion`.

Avoid animations that delay important actions or interfere with keyboard navigation.

---

## 14. Accessibility

Preserve and improve accessibility.

Target WCAG 2.2 AA where applicable.

Verify:

- Text contrast.
- Focus visibility.
- Keyboard navigation.
- Screen-reader labels.
- Error announcements.
- Semantic headings.
- Form descriptions.
- Modal focus management.
- Touch target sizes.
- Reduced motion.
- Responsive text scaling.

Run the existing Playwright and axe tests.

Do not sacrifice accessibility for aesthetics.

---

## 15. Imagery and Brand Assets

Propose a consistent imagery strategy.

Prefer:

- High-quality milkshake photography.
- Tasteful original illustrations.
- Optimised WebP or AVIF assets.
- Appropriate responsive image sizes.
- Descriptive alt text.
- Lazy loading for noncritical imagery.

Do not use unlicensed or copyrighted imagery without permission.

Avoid adding large image assets that significantly slow down the application.

If new imagery is needed, first propose the asset list and obtain approval.

---

## 16. Functional Preservation

This requirement is non-negotiable.

The redesign must not break:

- Patron registration and login.
- Manager authentication.
- CSRF protection.
- Milkshake configuration.
- Server-authoritative pricing.
- Pickup scheduling.
- Order placement.
- Paystack test payments.
- Cancellation.
- Refunds.
- Email notifications.
- Order history.
- Manager configuration.
- Reports.
- Audit history.
- CSV exports.
- Role-based authorisation.

Avoid backend changes.

If a backend change appears necessary, explain why and obtain separate approval.

---

## 17. Testing and Quality Assurance

After each redesign stage, run the relevant tests.

At minimum:

- TypeScript build.
- ESLint.
- Vitest.
- Playwright.
- Accessibility tests.
- Responsive layout checks.

Run backend regression tests where integration behaviour could be affected.

Compare before-and-after screenshots.

Review browser console errors.

Verify that redesigned controls preserve their original actions and validation.

Do not remove or weaken existing tests merely to accommodate new markup.

Update selectors only when justified by a legitimate UI change.

---

## 18. Implementation Strategy

Work in the following stages.

**Stop after each stage and obtain my approval.**

### Stage 1 — Design Audit and Proposal

Inspect the existing frontend.

Capture screenshots.

Produce:

- Current UI audit.
- Design direction.
- Proposed palette.
- Typography.
- Component strategy.
- Page-by-page improvement plan.
- Accessibility risks.
- Estimated implementation effort.

Do not modify code.

### Stage 2 — Visual Concept Approval

Create visual mockups or working prototypes for:

1. Landing page.
2. Milkshake builder.
3. Manager dashboard.

Show desktop and mobile layouts.

Wait for approval.

### Stage 3 — Design System

Implement approved tokens and shared components.

Test the components.

### Stage 4 — Patron-Facing Redesign

Redesign:

- Landing page.
- Authentication.
- Milkshake builder.
- Pickup selection.
- Order review.
- Payment states.
- My Orders.

Test each workflow.

### Stage 5 — Manager Redesign

Redesign the manager navigation, forms, tables, reports and audit screens.

Preserve functionality.

### Stage 6 — Responsive and Accessibility Refinement

Perform mobile, tablet, desktop and keyboard testing.

Fix layout and accessibility issues.

### Stage 7 — Full Regression and Visual Review

Run all applicable tests.

Capture final screenshots.

Check visual consistency.

Review performance and accessibility.

### Stage 8 — Final Approval and Deployment

Present:

- Before-and-after comparisons.
- Changed-file summary.
- Test results.
- Accessibility results.
- Performance impact.
- Known limitations.
- Deployment plan.

**Do not deploy the redesign automatically.**

Wait for explicit approval.

---

## 19. Definition of Done

The redesign is complete when:

- [ ] The application has a coherent, professional visual identity.
- [ ] The landing page feels like a real milkshake brand.
- [ ] The milkshake builder is attractive and intuitive.
- [ ] The ordering journey is easy to understand.
- [ ] Authentication screens are polished.
- [ ] My Orders is visually clear.
- [ ] Manager screens are consistent and usable.
- [ ] Forms, buttons, cards and tables use shared design tokens.
- [ ] Responsive layouts work across target devices.
- [ ] No significant layout shifts remain.
- [ ] Accessibility requirements pass.
- [ ] Existing business logic remains unchanged.
- [ ] Security controls remain intact.
- [ ] Automated regression tests pass.
- [ ] New assets are optimised and licensed appropriately.
- [ ] Final screenshots are reviewed.
- [ ] Deployment is approved separately.

---

## 20. Your First Response

Begin with **Stage 1 — Design Audit and Proposal**.

Inspect the existing React frontend, routing, Tailwind configuration, shared components, screenshots and Playwright tests.

Identify the exact pages and components requiring redesign.

Present:

1. Current UI/UX weaknesses.
2. Proposed visual direction.
3. Colour palette and typography.
4. Reusable design-system architecture.
5. Landing-page redesign concept.
6. Milkshake-builder redesign concept.
7. Manager-dashboard redesign concept.
8. Responsive strategy.
9. Accessibility considerations.
10. Exact files likely to change.
11. Proposed implementation sequence.
12. Any decisions requiring my approval.

**Do not modify code yet. Wait for my approval of the design direction.**