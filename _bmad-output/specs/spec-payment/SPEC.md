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
  - **intent:** An authenticated user on `/upgrade` selects a plan and billing period, clicks "Pay with Ko-fi", and the server generates a `PaymentIntent` with a unique code (`QSC-XXXXXX`). The page shows the code prominently so the user copies it into their Ko-fi payment message.
  - **success:** `POST /api/upgrade/initiate` with plan and billing creates a `PaymentIntent` record and returns `{ code, kofiPageUrl, expectedAmount, currency }`. Invalid plan returns 400. Unauthenticated returns 401.

- **CAP-2**
  - **intent:** Ko-fi sends real-time webhook events to QuickShareCloud. Ko-fi is a **pure payment gateway** — it processes payments and sends webhooks. The webhook matches the user by **payment code** (`QSC-XXXXXX`) from the Ko-fi message field — looks up the `PaymentIntent` record, then finds the user. Falls back to email matching if no code present. Subscription lifecycle management (cancel, change, expiry) is handled in-app.
  - **success:** Valid webhook with correct token: extracts message text → searches for `QSC-` code → looks up `PaymentIntent` → finds user → records `PaymentEvent` and delegates to `SubscriptionService`. No code found → falls back to email-based user lookup. Invalid token returns 401. Non-subscription payments (tips, commissions, shop orders) are recorded as `PaymentEvent` without affecting `User.Plan`.

- **CAP-3**
  - **intent:** `User.Plan` reflects the sender's actual subscription state — set to `"pro"` or `"studio"` on payment, preserved during cancellation grace period, dropped to `"free"` on expiry. A `Subscription` entity tracks the full lifecycle with billing periods.
  - **success:** Subscription created → new `Subscription` record (Active) + `User.Plan = "pro/studio"`. Subscription cancelled via in-app API → `Subscription.Status = Cancelled`, `User.Plan` unchanged until `CurrentPeriodEnd`. Subscription expired → `Subscription.Status = Expired` + `User.Plan = "free"`. Each transition is logged.

- **CAP-4**
  - **intent:** After completing payment on Ko-fi, the user is redirected back to the QuickShareCloud homepage.
  - **success:** The Ko-fi page's redirect URL points to `https://quicksharecloud.com/`. User sees their homepage (still on free plan until the webhook fires — acceptable latency).

- **CAP-5**
  - **intent:** An authenticated user navigates to `/account/billing` and views their subscription status — current plan, billing period (monthly/yearly), next billing date (from `Subscription.CurrentPeriodEnd`), and plan features. They can cancel in-app or change plan via `/pricing`.
  - **success:** Page shows current `User.Plan`, subscription status (Active/Cancelled/Expired), billing period, plan feature list, and next billing date. "Cancel Subscription" button sends `POST /api/subscriptions/cancel` via htmx. "Change Plan" links to `/pricing`. Free-tier users see "Upgrade" CTA linking to `/pricing`. Cancelled subscriptions show "Renew Subscription" linking to `/pricing`.

- **CAP-6**
  - **intent:** Every Ko-fi webhook event is stored as a payment event record with date, event type, plan, amount, invoice ID, and status. The `/account/billing` page displays this history chronologically with downloadable receipts (Ko-fi-hosted).
  - **success:** `payment_events` table stores each webhook event. `/account/billing` renders a timeline of events. Each entry shows date, event label (e.g. "Subscription created — Pro Monthly"), amount, and a "View Receipt" link to the Ko-fi invoice URL.

## Constraints

- Ko-fi is the only payment provider for plan upgrades. No VietQR, MoMo, Stripe, or PayPal direct integration.
- Ko-fi handles payment processing. Subscription lifecycle management (cancel, change, expiry) is owned by QuickShareCloud in-app.
- The existing `/upgrade?plan=X&billing=Y` URL contract from the Pricing page is preserved. Plan and billing values are passed as query parameters.
- The `/upgrade` page requires authentication (`[Authorize]`). Unauthenticated users are redirected to `/login`.
- `User.Plan` is a single string column on the `users` table (`"free"`, `"pro"`, `"studio"`). It is synced by `SubscriptionService` and should be treated as a **denormalized cache** of the canonical `Subscription` state.
- `Subscription` entity tracks: `UserId`, `Plan`, `Status` (Active/Cancelled/Expired), `BillingPeriod` (monthly/yearly), `CurrentPeriodStart`, `CurrentPeriodEnd`, `CancelledAt`, `CreatedAt`, `UpdatedAt`.
- `PaymentIntent` entity tracks: `UserId`, `Code` (`QSC-XXXXXX`, unique), `Plan`, `Billing`, `Amount`, `Currency`, `Status` (Pending/Completed/Expired), `CreatedAt`, `ExpiresAt` (1 hour).
- Every Ko-fi webhook request must carry a valid `verification_token` matching the configured secret. Untrusted requests return 401.
- `SubscriptionExpiryJob` (background service, 15-min interval) expires stale subscriptions past `CurrentPeriodEnd` and downgrades `User.Plan` to `"free"`.
- Tech stack: .NET 10, Blazor Static SSR, FastEndpoints, EF Core, PostgreSQL. No new infrastructure or services.

## Non-goals

- No VietQR, MoMo, or ZaloPay for plan upgrades. (VietQR remains for per-link paid deliveries — a different flow.)
- No proration, partial refunds, or custom billing math. Ko-fi's billing engine is authoritative.
- No team or enterprise multi-seat licensing.
- No self-service plan changes — user must go to `/pricing` to pick a new plan and re-enter Ko-fi checkout.
- No `ManageSubscriptionUrl` / `CancelSubscriptionUrl` config — these were removed. All subscription management is in-app.
- No Ko-fi product URL redirects. The upgrade flow creates a `PaymentIntent` code; the user enters this code manually in their Ko-fi payment message for webhook matching.

## Success signal

A user initiates an upgrade → creates a `PaymentIntent` with code `QSC-ABC123`. The user copies the code, pays on Ko-fi, and includes the code in the message field. The Ko-fi webhook arrives → extracts `QSC-ABC123` from message → looks up `PaymentIntent` → finds the user → creates an Active `Subscription` record and sets `User.Plan` to `"pro"`. The user visits `/account/billing` and sees their plan as Pro with the next billing date. The user clicks "Cancel Subscription" — `POST /api/subscriptions/cancel` marks the subscription as Cancelled. The page shows "Access continues until <date>". When the expiry job runs after `CurrentPeriodEnd`, `User.Plan` drops to `"free"`.
