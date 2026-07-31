---
id: SPEC-payment
companions:
  - payos-products.md
  - payment-events-schema.md
sources: []
---

> **Canonical contract.** This SPEC and the files in `companions:` are the complete, preservation-validated contract for what to build, test, and validate.

# QuickShareCloud Plan Upgrade Payment

## Why

QuickShareCloud has a pricing page with three tiers and an Upgrade page with payment buttons that post to an endpoint that doesn't exist. The pricing spec explicitly deferred payment processing. This spec fills that gap — capturing revenue from users who choose Pro or Studio plans while establishing the Plan state infrastructure that future tier enforcement and quota checks will consume.

## Capabilities

- **CAP-1**
  - **intent:** An authenticated user on `/upgrade` selects a plan and billing period, clicks "Pay with PayOS", and the server creates a `PaymentIntent` with a unique `PayOSOrderCode` (numeric). The server calls PayOS API to create a payment link and returns the `checkoutUrl` and `qrCode`. The page shows the QR code (VietQR for bank transfer) and a checkout button.
  - **success:** `POST /upgrade` form handler with plan and billing creates a `PaymentIntent` record with `PayOSOrderCode`, calls PayOS `POST /v2/payment-requests` to create a payment link, and renders the checkout URL + QR code. Invalid plan returns 400. Unauthenticated returns 401.

- **CAP-2**
  - **intent:** PayOS sends real-time webhook events to QuickShareCloud. PayOS is a **pure payment gateway** — it processes payments and sends webhooks. The webhook matches the user by **`orderCode`** (numeric `PayOSOrderCode`) — looks up the `PaymentIntent` record, then finds the user. Signature verification via HMAC-SHA256 prevents forgery.
  - **success:** Valid webhook with correct HMAC signature: extracts `orderCode` → looks up `PaymentIntent` → finds user → records `PaymentEvent` and delegates to `SubscriptionService`. Invalid signature returns 401.

- **CAP-3**
  - **intent:** `User.Plan` reflects the sender's actual subscription state — set to `"pro"` or `"studio"` on payment, preserved during cancellation grace period, dropped to `"free"` on expiry. A `Subscription` entity tracks the full lifecycle with billing periods.
  - **success:** Subscription created → new `Subscription` record (Active) + `User.Plan = "pro/studio"`. Subscription cancelled via in-app API → `Subscription.Status = Cancelled`, `User.Plan` unchanged until `CurrentPeriodEnd`. Subscription expired → `Subscription.Status = Expired` + `User.Plan = "free"`. Each transition is logged.

- **CAP-4**
  - **intent:** After completing payment, the user is redirected back to the QuickShareCloud upgrade page (`returnUrl`).
  - **success:** PayOS redirects to `https://quicksharecloud.com/upgrade` after payment. User sees their upgrade page (plan activates when webhook fires — acceptable latency).

- **CAP-5**
  - **intent:** An authenticated user navigates to `/account/billing` and views their subscription status — current plan, billing period (monthly/yearly), next billing date (from `Subscription.CurrentPeriodEnd`), and plan features. They can cancel in-app or change plan via `/pricing`.
  - **success:** Page shows current `User.Plan`, subscription status (Active/Cancelled/Expired), billing period, plan feature list, and next billing date. "Cancel Subscription" button sends `POST /api/subscriptions/cancel`. "Change Plan" links to `/pricing`. Free-tier users see "Upgrade" CTA linking to `/pricing`. Cancelled subscriptions show "Renew Subscription" linking to `/pricing`.

- **CAP-6**
  - **intent:** Every PayOS webhook event is stored as a payment event record with date, event type, plan, amount, transaction reference, and currency. The `/account/billing` page displays this history chronologically.
  - **success:** `payment_events` table stores each webhook event. `/account/billing` renders a timeline of events. Each entry shows date, event label (e.g. "Subscription created — Pro Monthly"), and amount.

## Constraints

- PayOS is the only payment provider for plan upgrades. No VietQR-direct, MoMo, Stripe, or PayPal.
- PayOS handles payment processing. Subscription lifecycle management (cancel, change, expiry) is owned by QuickShareCloud in-app.
- The existing `/upgrade?plan=X&billing=Y` URL contract from the Pricing page is preserved. Plan and billing values are passed as query parameters.
- The `/upgrade` page requires authentication (`[Authorize]`). Unauthenticated users are redirected to `/login`.
- `User.Plan` is a single string column on the `users` table (`"free"`, `"pro"`, `"studio"`). It is synced by `SubscriptionService` and should be treated as a **denormalized cache** of the canonical `Subscription` state.
- `Subscription` entity tracks: `UserId`, `Plan`, `Status` (Active/Cancelled/Expired), `BillingPeriod` (monthly/yearly), `CurrentPeriodStart`, `CurrentPeriodEnd`, `CancelledAt`, `CreatedAt`, `UpdatedAt`.
- `PaymentIntent` entity tracks: `UserId`, `Code` (`QSC-XXXXXX`, unique), `PayOSOrderCode` (numeric, unique), `Plan`, `Billing`, `Amount`, `Currency`, `Status` (Pending/Completed/Expired), `CreatedAt`, `ExpiresAt` (1 hour).
- Every PayOS webhook request must carry a valid HMAC-SHA256 signature matching the configured `ChecksumKey`. Untrusted requests return 401.
- `SubscriptionExpiryJob` (background service, 15-min interval) expires stale subscriptions past `CurrentPeriodEnd` and downgrades `User.Plan` to `"free"`.
- Tech stack: .NET 10, Blazor Static SSR, FastEndpoints, EF Core, PostgreSQL. PayOS .NET SDK (`payOS` NuGet package).

## Non-goals

- No MoMo, ZaloPay, or direct VietQR for plan upgrades.
- No proration, partial refunds, or custom billing math.
- No team or enterprise multi-seat licensing.
- No in-app subscription management UI beyond cancel/change-plan. PayOS dashboard handles refunds and disputes.
- No payment code message field — payment matching uses PayOS `orderCode` (numeric, server-generated).

## Success signal

A user on `/upgrade` selects Pro Monthly → clicks "Pay with PayOS" → server creates `PaymentIntent` with `PayOSOrderCode=1722345678000` → calls PayOS API → renders QR code + checkout URL. User scans QR via banking app and pays. PayOS webhook arrives → verifies HMAC signature → extracts `orderCode` → looks up `PaymentIntent` → finds user → creates Active `Subscription` + sets `User.Plan = "pro"`. User visits `/account/billing` and sees Pro plan with next billing date.
