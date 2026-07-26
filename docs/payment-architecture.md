# QuickShare — Payment Architecture

## Overview

QuickShare uses **Ko-fi as a pure payment gateway** — it processes payments and sends webhooks, but does not manage subscription lifecycle. There are no Ko-fi product URLs. Instead, the server generates a unique **payment code** (`QSC-XXXXXX`) for each upgrade attempt, and the user enters this code in their Ko-fi payment message. The webhook extracts the code from the message to match the payment to the user.

> User → `/upgrade` → server creates `PaymentIntent` → user copies code → pays on Ko-fi with code in message → webhook matches by code → `Subscription` activated → `User.Plan` set.

All subscription management (cancel, re-activate, expire, downgrade) is handled in-app. Ko-fi only collects the money.

---

## 1. Component Map

| Layer | File | Role |
|-------|------|------|
| Upgrade Page | `Pages/Upgrade.razor` | `<form method="post">` handler — creates `PaymentIntent` with unique code, renders payment UI + QR. In-process (no API endpoint). |
| Webhook Handler | `Features/Payments/Kofi/KofiWebhookEndpoint.cs` | `POST /api/payments/kofi/webhook` — receives Ko-fi events, resolves plan by amount, matches user by payment code (fallback email), records `PaymentEvent`, delegates to `SubscriptionService` |
| Billing History Service | `Features/Payments/Kofi/BillingHistoryService.cs` | Shared billing-history query (used by SSR page directly) |
| Subscription Service | `Features/Payments/Subscriptions/SubscriptionService.cs` | Static service: `HandlePaymentAsync`, `CancelAsync`, `ExpireStaleAsync`, `GetCurrentAsync` |
| Billing Page | `Pages/AccountBilling.razor` | `<form method="post" Action=cancel>` — calls `SubscriptionService.CancelAsync()` directly. Loads subscription + billing history via services directly. |
| Expiry Job | `BackgroundJobs/SubscriptionExpiryJob.cs` | Background service (15-min interval) expires stale subscriptions |
| PaymentIntent Model | `Models/PaymentIntent.cs` | Entity: Code, UserId, Plan, Billing, Amount, Status, ExpiresAt |
| Subscription Model | `Models/Subscription.cs` | Entity: UserId, Plan, Status, BillingPeriod, CurrentPeriodStart/End |
| PaymentEvent Model | `Models/PaymentEvent.cs` | Entity: UserId, EventType, Plan, Amount, InvoiceId, InvoiceUrl |

---

## 2. Data Models

### 2.1 PaymentIntent

Represents a single upgrade attempt with a unique payment code.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK (Guid v7) |
| `UserId` | `Guid` | FK → User |
| `Code` | `string` | Unique, format `QSC-XXXXXX` (6 alphanumeric chars excluding 0/O/1/I) |
| `Plan` | `string` | `"pro"` / `"studio"` |
| `Billing` | `string` | `"monthly"` / `"yearly"` |
| `Amount` | `decimal` | Expected amount from Pricing config (e.g. $9, $239) |
| `Currency` | `string` | `"USD"` or `"$"` |
| `Status` | `PaymentIntentStatus` | `Pending` / `Completed` / `Expired` |
| `CreatedAt` | `DateTime` | UTC |
| `CompletedAt` | `DateTime?` | UTC, set when webhook processes the code |
| `ExpiresAt` | `DateTime` | UTC, 1 hour after creation |

**Lifetime**: Created on `/upgrade` → `Pending` → webhook matches → `Completed`. Or 1h auto-expiry (not enforced by job; webhook ignores stale codes).

### 2.2 Subscription

Tracks the full in-app subscription lifecycle.

| Field | Type | Notes |
|-------|------|-------|
| `Id` (inherited) | `Guid` | PK |
| `UserId` | `Guid` | FK → User |
| `Plan` | `string` | `"pro"` / `"studio"` |
| `Status` | `string` | `"active"` / `"cancelled"` / `"expired"` |
| `BillingPeriod` | `string` | `"monthly"` / `"yearly"` |
| `CurrentPeriodStart` | `DateTime` | UTC |
| `CurrentPeriodEnd` | `DateTime` | UTC |
| `CancelledAt` | `DateTime?` | UTC |
| `CreatedAt` (inherited) | `DateTime` | UTC |
| `UpdatedAt` (inherited) | `DateTime?` | UTC |

**Lifecycle**:

```
                         ┌─────────────────┐
                         │                 │
                         │    Active       │
                         │                 │
                         └──┬──────────┬───┘
                    cancel │          │ handle payment (renew)
                           ▼          │ (extend period)
                    ┌────────────┐    │
                    │            │    │
                    │ Cancelled  │────┘
                    │            │ (re-activate)
                    └──────┬─────┘
                           │ CurrentPeriodEnd passes
                           ▼
                    ┌────────────┐
                    │            │
                    │  Expired   │
                    │            │
                    │ User.Plan  │
                    │ → "free"   │
                    └────────────┘
```

- **Active**: User has paid. `User.Plan` matches subscription plan.
- **Cancelled**: User cancelled via in-app API. Plan continues until `CurrentPeriodEnd`. Can re-activate by paying again.
- **Expired**: `CurrentPeriodEnd` passed. `User.Plan` set to `"free"`. Terminal state.

### 2.3 PaymentEvent

Immutable audit log of every Ko-fi webhook event.

| Field | Type | Notes |
|-------|------|-------|
| `Id` (inherited) | `Guid` | PK |
| `UserId` | `Guid` | FK → User (may be `Guid.Empty` if unlinked) |
| `EventType` | `string` | `"subscription_created"`, `"subscription_renewed"`, `"tip"`, etc. |
| `Plan` | `string` | Resolved plan code (`"pro"` / `"studio"` / `""`) |
| `PlanDisplay` | `string` | Human-readable (`"Pro"` / `"Studio"`) |
| `Billing` | `string` | `"monthly"` / `"yearly"` / `""` |
| `Amount` | `decimal` | Payment amount |
| `Currency` | `string` | `"USD"` |
| `InvoiceId` | `string` | Ko-fi transaction ID |
| `InvoiceUrl` | `string` | Link to Ko-fi receipt |

---

## 3. Upgrade Flow

### 3.1 Flow Diagram

```
User Browser                     QuickShare (9000)                  Ko-fi
     │                                │                              │
     │  GET /upgrade?plan=pro&        │                              │
     │  billing=monthly               │                              │
     │───────────────────────────────▶│                              │
     │◀───────────────────────────────│ (Upgrade.razor HTML)         │
     │                                │                              │
     │  POST /upgrade?plan=pro&       │                              │
     │  billing=monthly               │                              │
     │  (form: Plan=pro,              │                              │
     │   Billing=monthly)             │                              │
     │───────────────────────────────▶│                              │
     │                                │── OnInitializedAsync:        │
     │                                │   detect POST, read form     │
     │                                │                              │
     │                                │── Lookup pricing:           │
     │                                │   Pricing:ProMonthly = $9   │
     │                                │                              │
     │                                │── Generate unique code:     │
     │                                │   QSC-A3X9K2                │
     │                                │                              │
     │                                │── Create PaymentIntent:     │
     │                                │   Pending, expires 1h       │
     │                                │   Save to DB                │
     │                                │                              │
     │                                │── Build kofiUrlWithCode:    │
     │                                │   https://ko-fi.com/...     │
     │                                │   ?message=QSC-A3X9K2       │
     │                                │                              │
     │◀───────────────────────────────│                              │
     │  (re-rendered Upgrade.razor    │                              │
     │   with payment section:        │                              │
     │   code, QR, Ko-fi link)        │                              │
     │                                │                              │
     │  Page shows code prominently.  │                              │
     │  QR auto-renders via JS on     │                              │
     │  page load.                    │                              │
     │                                │                              │
     │  User clicks "Pay with Ko-fi"  │                              │
     │  → opens kofiUrlWithCode       │                              │
     │    (message pre-filled).       │                              │
     │  OR scans QR code on phone.    │                              │
     │─────────────────────────────────────────────────────────────▶│
     │                                │                              │
     │  User completes payment on     │                              │
     │  Ko-fi (code already in        │                              │
     │  message field).               │                              │
     │                                │                              │
     │                                │  POST /api/payments/kofi/    │
     │                                │  webhook (verification_token)│
     │                                │◀─────────────────────────────│
     │                                │                              │
     │                                │── Verify token              │
     │                                │── Extract "QSC-A3X9K2" from │
     │                                │   message field              │
     │                                │── Lookup PaymentIntent      │
     │                                │── Find user via intent.UserId│
     │                                │                              │
     │                                │── Resolve plan from amount  │
     │                                │   $9 → Pro Monthly          │
     │                                │                              │
     │                                │── HandlePaymentAsync()      │
     │                                │   → Create/update           │
     │                                │     Subscription (Active)   │
     │                                │   → User.Plan = "pro"       │
     │                                │── Mark PaymentIntent        │
     │                                │   Completed                 │
     │                                │── Save PaymentEvent         │
     │                                │                              │
     │◀───────────────────────────────│  (redirect from Ko-fi)      │
     │                                │                              │
```

### 3.2 QR Code

After the form POST, `Upgrade.razor` re-renders with the payment section containing a `<div id="kofi-qrcode">` placeholder. A `<script>` block auto-invokes `renderKofiQR()` on page load to generate an SVG QR code of the `kofiUrlWithCode`. Uses the existing `qrcode.min.js` library (loaded globally via `App.razor`):

```js
function renderKofiQR(text) {
    var el = document.getElementById("kofi-qrcode");
    if (!el) return;
    el.innerHTML = "";
    if (!text || !window.qrcode) {
        if (!window.qrcode) el.textContent = "QR library not loaded.";
        return;
    }
    var qr = window.qrcode(0, "M");     // Type 0, error correction M
    qr.addData(text);
    qr.make();
    var svg = qr.createSvgTag(5, 2);    // cell 5px, margin 2
    el.innerHTML = svg;
}
```

The QR allows users to scan with their phone and pay on mobile while staying on the desktop page.

---

## 4. Webhook User Matching Strategy

The `KofiWebhookEndpoint` matches incoming payments to users in this order:

1. **Extract `QSC-` code from `message` field**: If the Ko-fi payment has a message containing `QSC-XXXXXX`, extract the code.
2. **Lookup `PaymentIntent`**: Find a `Pending` `PaymentIntent` with that code → get `UserId`.
3. **Email fallback**: If no code found (e.g. direct Ko-fi payment without going through upgrade page), fall back to `Email` field in webhook payload.

```
Ko-fi webhook payload:
{
  "message": "QSC-A3X9K2",
  "email": "user@example.com",
  "amount": "9.00",
  "currency": "USD",
  "type": "Subscription",
  "verification_token": "...",
  ...
}

Matching logic:
  message.contains("QSC-") ? → Lookup PaymentIntent by code → find User
                              → Fallback: lookup User by email
```

Amount-to-plan mapping is done via `Pricing` config:
- `Pricing:ProMonthly` = $9 → `("pro", "monthly")`
- `Pricing:ProYearly` = $77 → `("pro", "yearly")`
- `Pricing:StudioMonthly` = $29 → `("studio", "monthly")`
- `Pricing:StudioYearly` = $239 → `("studio", "yearly")`

---

## 5. Subscription Management

Subscription mutations are handled via **Blazor SSR form handlers** — no API endpoints. The billing page (`AccountBilling.razor`) calls `SubscriptionService` directly:

### Cancel Subscription — Form Handler in `AccountBilling.razor`

```
User clicks "Cancel Subscription" button
  → <form method="post"> with hidden Action=cancel
  → OnInitializedAsync detects POST + reads form
  → await SubscriptionService.CancelAsync(user, db, ct)
  → await db.SaveChangesAsync(ct)
  → Page re-renders with updated subscription state
```

Effects:
  - `Subscription.Status` → `"cancelled"`
  - `Subscription.CancelledAt` → now
  - `User.Plan` → unchanged (remains until `CurrentPeriodEnd`)

### Get Current Subscription — Direct Service Call

`AccountBilling.razor` calls `SubscriptionService.GetCurrentAsync(user, db, ct)` directly in `OnInitializedAsync`. No API endpoint needed — the SSR page runs in-process on the server.

---

## 6. Background Job: SubscriptionExpiryJob

Runs every 15 minutes on a `BackgroundService`.

```
SubscriptionExpiryJob
  ↓
  FOR each subscription WHERE
    status IN ("active", "cancelled")
    AND currentPeriodEnd <= now
  ↓
    SET status = "expired"
    SET User.Plan = "free"
  ↓
  SaveChangesAsync()
```

- **Interval**: 15 minutes (`TimeSpan.FromMinutes(15)`)
- **Safety**: Only downgrades subscriptions past `CurrentPeriodEnd`
- **No double-dip**: `CurrentPeriodEnd` is always in the future for active subscriptions (extended on renew)

---

## 7. Billing History

### Direct Service Call — `AccountBilling.razor`

The billing page calls `BillingHistoryService.GetForUserAsync(user, db, ct)` directly in `OnInitializedAsync`. No API endpoint — the SSR page runs in-process on the server.

`BillingHistoryResponse` contains `User.Plan` + chronological list of `PaymentEvent` items:

```json
{
  "plan": "pro",
  "planDisplay": "Pro",
  "events": [
    {
      "eventType": "subscription_created",
      "eventLabel": "Subscription created — Pro Monthly",
      "planDisplay": "Pro",
      "billing": "monthly",
      "amount": 9.00,
      "currency": "USD",
      "invoiceId": "abc123",
      "invoiceUrl": "https://ko-fi.com/...",
      "createdAt": "2026-07-26T..."
    }
  ]
}
```

### Billing Page (`/account/billing`)

- **Free users**: Shows "Upgrade" CTA linking to `/pricing`
- **Active subscribers**: Shows plan name, status (Active), billing period, next billing date, "Cancel Subscription" button using `<form method="post">` handler
- **Cancelled subscribers**: Shows "Access continues until <date>", "Renew Subscription" linking to `/pricing`
- **Expired subscribers**: Shows expired banner, "Upgrade Again" linking to `/pricing`
- All users see chronological payment event timeline

---

## 8. Configuration

| Key | Purpose | Example |
|-----|---------|---------|
| `Kofi:PageUrl` | Ko-fi page URL for "Pay with Ko-fi" link | `https://ko-fi.com/miraclefoundation` |
| `Kofi:VerificationToken` | Secret token for webhook auth | GUID |
| `Pricing:ProMonthly` | Pro monthly price | `9` |
| `Pricing:ProYearly` | Pro yearly price | `77` |
| `Pricing:StudioMonthly` | Studio monthly price | `29` |
| `Pricing:StudioYearly` | Studio yearly price | `239` |
| `Pricing:Currency` | Currency symbol | `$` |

No `Products`, `ManageSubscriptionUrl`, or `CancelSubscriptionUrl` — these were removed.

---

## 10. Security Model

| Concern | Protection |
|---------|-----------|
| **Unauthorized webhook** | `verification_token` must match configured secret |
| **Replay / stale payments** | `PaymentIntent.Code` is single-use (set to `Completed` after first match) |
| **Unlinked payments** | Payments without matching code or email are recorded as `PaymentEvent` but don't affect any `User.Plan` |
| **Amount tampering** | Plan is resolved from server-side amount config, not from the client's request |
| **Payment code forgery** | `QSC-XXXXXX` codes are generated server-side with alphanumeric pool (no 0/O/1/I), uniqueness checked against DB |
| **Code expiry** | `PaymentIntent.ExpiresAt` = 1 hour; stale codes are ignored by webhook (status check) |

---

## 10. File Reference

| File | Role |
|------|------|
| `Models/PaymentIntent.cs` | Entity: upgrade payment code |
| `Models/Subscription.cs` | Entity: subscription lifecycle |
| `Models/PaymentEvent.cs` | Entity: webhook event audit log |
| `Models/BillingHistoryResponse.cs` | Response DTO for billing page |
| `Features/Payments/Kofi/KofiWebhookEndpoint.cs` | `POST /api/payments/kofi/webhook` — payment code matching + event recording |
| `Features/Payments/Kofi/BillingHistoryService.cs` | Shared billing history query |
| `Features/Payments/Subscriptions/SubscriptionService.cs` | Core subscription logic (activate, cancel, expire) |
| `BackgroundJobs/SubscriptionExpiryJob.cs` | 15-min background expiry |
| `Pages/Upgrade.razor` | Upgrade page — form handler creates PaymentIntent, renders payment code + QR |
| `Pages/AccountBilling.razor` | Billing page — form handler for cancel, direct service calls for data |
| `Constants/PlanTierLimits.cs` | Plan limit constants |
