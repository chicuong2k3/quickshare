---
id: SPEC-payment
companions:
  - kofi-products.md
  - payment-events-schema.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# QuickShareCloud Plan Upgrade Payment

## Why

QuickShareCloud has a pricing page with three tiers and an Upgrade page with payment buttons that post to an endpoint that doesn't exist. The pricing spec explicitly deferred payment processing. This spec fills that gap — capturing revenue from users who choose Pro or Studio plans while establishing the Plan state infrastructure that future tier enforcement and quota checks will consume.

## Capabilities

- **CAP-1**
  - **intent:** An authenticated user on `/upgrade` selects a plan and billing period, clicks "Pay with Ko-fi", and the server returns a redirect URL to the correct Ko-fi product page.
  - **success:** `POST /api/upgrade/initiate` with plan and billing returns `303 See Other` with `Location: https://ko-fi.com/...` for the matching product. Invalid plan returns 400. Unauthenticated returns 401.

- **CAP-2**
  - **intent:** Ko-fi sends real-time webhook events (subscription created, renewed, cancelled, expired) to QuickShareCloud to keep plan state in sync. The webhook validates the Ko-fi verification token before processing.
  - **success:** Valid webhook with correct token maps product ID to plan name and updates `User.Plan`. Invalid token returns 401. Unknown product ID returns 200 with `{ status: "ignored" }`.

- **CAP-3**
  - **intent:** `User.Plan` reflects the sender's actual subscription state — set to `"pro"` or `"studio"` on payment, preserved during cancellation grace period, dropped to `"free"` on expiry.
  - **success:** Subscription created → `User.Plan = "pro"`. Subscription cancelled → `User.Plan` unchanged (still "pro"). Subscription expired → `User.Plan = "free"`. Each transition is logged but no notification is sent.

- **CAP-4**
  - **intent:** After completing payment on Ko-fi, the user is redirected back to the QuickShareCloud homepage.
  - **success:** The Ko-fi product's redirect URL points to `https://quicksharecloud.com/`. User sees their homepage (still on free plan until the webhook fires — acceptable latency).

- **CAP-5**
  - **intent:** An authenticated user navigates to `/account/billing` and views their subscription status — current plan, billing period (monthly/yearly), next billing date, and plan features. They can cancel or change their plan via links that redirect to Ko-fi.
  - **success:** Page shows current `User.Plan`, billing period, plan feature list matching the Pricing page tiers. "Cancel Subscription" and "Change Plan" buttons redirect to the appropriate Ko-fi pages. Free-tier users see an "Upgrade" CTA linking to `/pricing`.

- **CAP-6**
  - **intent:** Every Ko-fi webhook event is stored as a payment event record with date, event type, plan, amount, invoice ID, and status. The `/account/billing` page displays this history chronologically with downloadable receipts (Ko-fi-hosted).
  - **success:** `payment_events` table stores each webhook event. `/account/billing` renders a timeline of events. Each entry shows date, event label (e.g. "Subscription created — Pro Monthly"), amount, and a "View Receipt" link to the Ko-fi invoice URL.

## Constraints

- Ko-fi is the only payment provider for plan upgrades. No VietQR, MoMo, Stripe, or PayPal direct integration.
- Ko-fi manages recurring subscription billing. QuickShareCloud does not implement subscription logic — it only reacts to webhook events.
- The existing `/upgrade?plan=X&billing=Y` URL contract from the Pricing page is preserved. Plan and billing values are passed as query parameters.
- The `/upgrade` page requires authentication (`[Authorize]`). Unauthenticated users are redirected to `/login`.
- `User.Plan` is a single string column on the `users` table (`"free"`, `"pro"`, `"studio"`). The webhook endpoint writes directly to this field. Payment event history is stored in a separate `payment_events` table — the `users` table is not denormalized with billing fields.
- Every Ko-fi webhook request must carry a valid `verification_token` matching the configured secret. Untrusted requests return 401.
- Tech stack: .NET 10, Blazor Static SSR, FastEndpoints, EF Core, PostgreSQL. No new infrastructure or services.

## Non-goals

- No VietQR, MoMo, or ZaloPay for plan upgrades. (VietQR remains for per-link paid deliveries — a different flow.)
- No proration, partial refunds, or custom billing math. Ko-fi's billing engine is authoritative.
- No team or enterprise multi-seat licensing.
- Change plan and cancel subscription redirect to Ko-fi — no in-app subscription management API calls.

## Success signal

A Ko-fi subscription-created webhook arrives with a valid token and a Pro product ID. The handler updates `User.Plan` from `"free"` to `"pro"` in the database and returns 200. The user visits their dashboard and sees their plan as Pro. When the subscription expires and the expired webhook fires, their plan drops to `"free"`.
