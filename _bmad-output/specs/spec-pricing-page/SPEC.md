---
id: SPEC-pricing-page
companions:
  - pricing-tiers.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# TeleCloud Pricing Page

## Why

TeleCloud currently operates as an anonymous, free file-sharing service with no monetization path. Adding a pricing page captures the opportunity to present TeleCloud as a tiered SaaS product — converting casual free users into paying subscribers while establishing the product's value proposition. Modeled after WeTransfer's proven tier structure, the page serves as both a marketing surface and the entry point for future subscription conversion, even before the full auth and payment infrastructure is built.

## Capabilities

- **CAP-1**
  - **intent:** Display a pricing page at `/pricing` showing the 3 individual plan tiers (Free, Starter, Ultimate) in a comparison layout with a monthly/yearly billing toggle. Teams and Enterprise tiers are hidden until auth/team infrastructure exists.
  - **success:** Page renders at `/pricing` with all 3 visible tiers, correct features per tier, correct prices for both monthly and yearly billing. Toggle switches displayed prices and the URL updates to reflect the selected billing period.

- **CAP-2**
  - **intent:** Present a feature comparison table showing which capabilities are available per tier, with visual distinction between included and unavailable features. Team-gated features show "Coming Soon" since Teams/Enterprise tiers are hidden.
  - **success:** Each feature row shows a checkmark (available) or dash/x (unavailable) per tier. Team-gated features show "Coming Soon" badge. The table is visually scannable and responsive across device sizes.

- **CAP-3**
  - **intent:** Provide distinct call-to-action buttons per tier — "Get Started" for Free tier (links to /upload page), "Subscribe" for paid tiers (routes to in-app checkout flow to be built).
  - **success:** Each tier card has a uniquely styled CTA button. Free CTA navigates to the existing upload page. Paid CTAs navigate to the in-app checkout route. Ultimate CTA is visually emphasized as the recommended tier.

- **CAP-4**
  - **intent:** Include an FAQ section answering common questions about plans, cancellation, over-limit behavior, payment methods, and recipient requirements.
  - **success:** FAQ section with expandable/collapsible items. Answers are consistent with tier definitions in `pricing-tiers.md`. All stated questions from the WeTransfer reference FAQ are addressed.

## Constraints

- Must use RizzyUI Razor components exclusively for UI elements: RzButton (instead of raw `<button>`/`<a>`), RzBadge (for status indicators), RzCard (card containers), RzTable (comparison table), RzSeparator, RzSpinner, etc. from [rizzyui.jalex.io](https://rizzyui.jalex.io). Plain `<div>`, `<span>`, `<p>`, `<header>`, `<footer>`, `<main>`, `<nav>` wrappers and `<svg>` icons are allowed for layout/structure only. Tailwind CSS v4 remains for spacing, alignment, and overrides. Alpine.js for client interactivity (billing toggle, FAQ accordion).
- Telegram's 2 GB per-file upload limit is a hard ceiling across ALL tiers. No plan offers unlimited file size. Tier differentiation is via monthly volume cap, transfer count, expiry duration, and branding/team features.
- Tier definitions, prices, and feature matrices are defined in `pricing-tiers.md` companion as the single source of truth. The page reads from this canonical data — no hardcoding prices or features in Razor components.

## Non-goals

- No payment processing, subscription lifecycle management, or billing infrastructure. The Subscribe button triggers checkout navigation only — the actual payment flow (Stripe, Paddle, etc.) is a separate capability.
- No backend enforcement of tier limits on uploads/downloads. The pricing page is informational/marketing. Per-tier rate limiting, quota tracking, and enforcement require auth and subscription state.
- No admin dashboard or CMS for managing tiers and prices. Tier data is defined in the `pricing-tiers.md` companion and compiled at build time.
- No user authentication or account creation. The pricing page is accessible to anonymous visitors. Free tier CTA links to the existing anonymous upload flow.

## Success signal

A visitor navigates to `/pricing`, sees 3 tiers (Free, Starter, Ultimate) with their features and prices, toggles between monthly and yearly billing (prices update correctly), clicks a CTA button that routes to the appropriate destination (/upload for Free, in-app checkout route for paid), and can expand FAQ items to read answers. The page renders correctly on mobile, tablet, and desktop viewports using the existing RizzyUI + Tailwind stack.

## Assumptions

- Pricing page is purely static informational content. All tier data (prices, features, limits) is compiled into the app at build time from `pricing-tiers.md` / a JSON config. No runtime API calls are needed to render tiers.
- Monthly/Yearly toggle applies a 20% discount for yearly billing (standard SaaS convention). The WeTransfer reference input shows the billing toggle but does not specify a discount rate.
