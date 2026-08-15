# QuickShare — Payment Architecture

## Overview

QuickShare uses **PayOS as a payment gateway** — it processes payments (bank transfer via VietQR, card, e-wallet) and sends webhooks. The server generates a unique **`PayOSOrderCode`** (numeric) for each upgrade attempt, calls PayOS API to create a payment link, and the user scans the QR code or clicks the checkout URL to pay. The webhook matches the payment by `orderCode` and activates the subscription.

> User → `/upgrade` → server creates `PaymentIntent` with `PayOSOrderCode` → calls PayOS API → user scans QR/pays → PayOS webhook arrives → matches by `orderCode` → `Subscription` activated → `User.Plan` set.

All subscription management (cancel, re-activate, expire, downgrade) is handled in-app. PayOS only collects the money.

**Why PayOS instead of Ko-fi?** Ko-fi pre-filled the payment message field with the tracking code (`QSC-XXXXXX`), showing it to the buyer in the checkout textarea — a UX issue. PayOS's numeric `orderCode` is passed server-to-server via webhook, invisible to the user.

---

## 1. Component Map

| Layer | File | Role |
|-------|------|------|
| Upgrade Page | `Pages/Upgrade.razor` | `<form method="post">` handler — creates `PaymentIntent` with `PayOSOrderCode`, calls `PayOSService` to create PayOS payment link. Renders QR + checkout URL. In-process (no API endpoint). |
| PayOS Service | `Features/Payments/PayOS/PayOSService.cs` | `PayOSClient` wrapper — creates payment links via `POST /v2/payment-requests`, generates unique numeric `PayOSOrderCode` |
| Webhook Handler | `Features/Payments/PayOS/PayOSWebhookEndpoint.cs` | `POST /api/payments/payos/webhook` — verifies HMAC-SHA256 signature via SDK, matches `PaymentIntent` by `PayOSOrderCode`, records `PaymentEvent`, delegates to `SubscriptionService` |
| Webhook Registration | `Features/Payments/PayOS/PayOSWebhookRegistrationHostedService.cs` | Hosted service — auto-registers the webhook URL with PayOS at startup |
| Billing History Service | `Features/Payments/BillingHistoryService.cs` | Shared billing-history query (used by SSR page directly) |
| Subscription Service | `Features/Payments/Subscriptions/SubscriptionService.cs` | Static service: `HandlePaymentAsync`, `CancelAsync`, `ExpireStaleAsync`, `GetCurrentAsync` |
| Billing Page | `Pages/AccountBilling.razor` | `<form method="post" Action=cancel>` — calls `SubscriptionService.CancelAsync()` directly. Loads subscription + billing history via services directly. |
| Expiry Job | `Infrastructure/SubscriptionExpiryJob.cs` | Background service (15-min interval) expires stale subscriptions |
| PaymentIntent Model | `Data/Entities/PaymentIntent.cs` | Entity: `Code`, `PayOSOrderCode`, `UserId`, `Plan`, `Billing`, `Amount`, `Status`, `ExpiresAt` |
| Subscription Model | `Data/Entities/Subscription.cs` | Entity: `UserId`, `Plan`, `Status`, `BillingPeriod`, `CurrentPeriodStart/End` |
| PaymentEvent Model | `Data/Entities/PaymentEvent.cs` | Entity: `UserId`, `EventType`, `Plan`, `Amount`, `Currency`, `InvoiceId` |

---

## 2. Data Models

### 2.1 PaymentIntent

Represents a single upgrade attempt with internal tracking code + PayOS order code.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK (Guid v7) |
| `UserId` | `Guid` | FK → User |
| `Code` | `string` | Internal format `QSC-XXXXXX` (for backward compatibility) |
| `PayOSOrderCode` | `long` | PayOS numeric order code (unique, indexed). Generated as `timestamp * 1000 + random`. |
| `Plan` | `string` | `"pro"` / `"studio"` |
| `Billing` | `string` | `"monthly"` / `"yearly"` |
| `Amount` | `decimal` | Expected amount in VND (from Pricing config) |
| `Currency` | `string` | `"VND"` |
| `Status` | `PaymentIntentStatus` | `Pending` / `Completed` / `Expired` |
| `CreatedAt` | `DateTime` | UTC |
| `CompletedAt` | `DateTime?` | UTC, set when webhook processes the order code |
| `ExpiresAt` | `DateTime` | UTC, 1 hour after creation |

**Lifetime**: Created on `/upgrade` → `Pending` → PayOS webhook matches → `Completed`. Or 1h auto-expiry (not enforced by job; webhook ignores stale codes).

### 2.2 Subscription

Tracks the full in-app subscription lifecycle (unchanged from Ko-fi era).

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

### 2.3 PaymentEvent

Immutable audit log of every PayOS webhook event.

| Field | Type | Notes |
|-------|------|-------|
| `Id` (inherited) | `Guid` | PK |
| `UserId` | `Guid` | FK → User |
| `EventType` | `string` | `"payment"` for PayOS payments |
| `Plan` | `string` | Resolved plan code (`"pro"` / `"studio"`) |
| `PlanDisplay` | `string` | Human-readable (`"Pro"` / `"Studio"`) |
| `Billing` | `string` | `"monthly"` / `"yearly"` |
| `Amount` | `decimal` | Payment amount in VND |
| `Currency` | `string` | `"VND"` |
| `InvoiceId` | `string` | PayOS transaction reference |
| `InvoiceUrl` | `string` | PayOS payment link URL |

---

## 3. Upgrade Flow

### 3.1 Flow Diagram

```
User Browser                     QuickShare (9000)                  PayOS API
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
     │                                │   Pricing:ProMonthly = 50000│
     │                                │                              │
     │                                │── Generate unique codes:    │
     │                                │   Code = QSC-A3X9K2         │
     │                                │   PayOSOrderCode = 1722345..│
     │                                │                              │
     │                                │── Create PaymentIntent:     │
     │                                │   Pending, expires 1h       │
     │                                │   Save to DB                │
     │                                │                              │
     │                                │── Call PayOS API:           │
     │                                │   POST /v2/payment-requests │
     │                                │   { orderCode, amount, .. } │
     │                                │──────────────────────────────▶│
     │                                │                              │
     │                                │◀─────────────────────────────│
     │                                │   { checkoutUrl, qrCode, ..}│
     │                                │                              │
     │◀───────────────────────────────│                              │
     │  (re-rendered Upgrade.razor    │                              │
     │   with: QR code (VietQR),      │                              │
     │   checkout button)             │                              │
     │                                │                              │
     │  User scans QR via banking app │                              │
     │  OR clicks checkout link.      │                              │
     │──────────────────────────────────────────────────────────────│
     │                                │                              │
     │  User completes payment.       │                              │
     │                                │                              │
     │                                │  POST /api/payments/payos/   │
     │                                │  webhook ({code, data, sign} )│
     │                                │◀─────────────────────────────│
     │                                │                              │
     │                                │── Verify HMAC signature     │
     │                                │   via SDK                    │
     │                                │                              │
     │                                │── Extract data.orderCode    │
     │                                │   = 1722345...              │
     │                                │                              │
     │                                │── Lookup PaymentIntent      │
     │                                │   by PayOSOrderCode          │
     │                                │                              │
     │                                │── Find user via intent      │
     │                                │                              │
     │                                │── HandlePaymentAsync()      │
     │                                │   → Create/update           │
     │                                │     Subscription (Active)   │
     │                                │   → User.Plan = "pro"       │
     │                                │                              │
     │                                │── Mark PaymentIntent        │
     │                                │   Completed                 │
     │                                │                              │
     │                                │── Save PaymentEvent         │
     │                                │                              │
     │◀───────────────────────────────│  (redirect from PayOS       │
     │                                │   returnUrl)                │
     │                                │                              │
```

### 3.2 QR Code

After the form POST, `Upgrade.razor` re-renders with the payment section containing a `<div id="payos-qrcode">` placeholder. A `<script>` block auto-invokes `renderPayOSQR()` on page load to generate an SVG QR code from the PayOS VietQR string. Uses the existing `qrcode.min.js` library (loaded globally via `App.razor`):

```js
function renderPayOSQR(qrContent) {
    var el = document.getElementById("payos-qrcode");
    if (!el) return;
    el.innerHTML = "";
    if (!qrContent || !window.qrcode) {
        if (!window.qrcode) el.textContent = "QR library not loaded.";
        return;
    }
    var qr = window.qrcode(0, "M");
    qr.addData(qrContent);
    qr.make();
    var svg = qr.createSvgTag(5, 2);
    el.innerHTML = svg;
}
```

The QR allows users to scan with their banking app and pay via VietQR while staying on the desktop page.

---

## 4. Webhook Processing

The `PayOSWebhookEndpoint` processes incoming PayOS webhooks:

1. **Verify HMAC-SHA256 signature**: SDK `Webhooks.VerifyAsync()` re-computes signature from received `data` and compares with `signature` field. Invalid → 401.
2. **Extract `orderCode`**: `data.orderCode` is the numeric PayOS order code.
3. **Lookup `PaymentIntent`**: Find a `Pending` `PaymentIntent` with `PayOSOrderCode == orderCode`.
4. **Find user**: `intent.UserId` → user lookup.
5. **Activate subscription**: `SubscriptionService.HandlePaymentAsync()`.
6. **Record event**: `PaymentEvent` with type `"payment"`.

```
PayOS webhook payload (verified):
{
  "code": "00",
  "desc": "success",
  "success": true,
  "data": {
    "orderCode": 1722345678000,
    "amount": 50000,
    "currency": "VND",
    "reference": "TF230204212323",
    ...
  },
  "signature": "8d8640d802576397a1ce45ebda..."
}

Matching logic:
  Verify signature via SDK
  → Lookup PaymentIntent by PayOSOrderCode == data.orderCode
  → Find user via PaymentIntent.UserId
  → Activate subscription
```

---

## 5. Subscription Management

(Unchanged — same as Ko-fi era.)

Subscription mutations are handled via **Blazor SSR form handlers** — no API endpoints. The billing page (`AccountBilling.razor`) calls `SubscriptionService` directly.

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

(Unchanged — same as Ko-fi era.)

Runs every 15 minutes as a Hangfire recurring job (`subscription-expiry`).

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

---

## 7. PayOS Integration Details

### 7.1 PayOS SDK Initialization

```csharp
var client = new PayOSClient(clientId, apiKey, checksumKey);
```

Credentials from `PayOS:ClientId`, `PayOS:ApiKey`, `PayOS:ChecksumKey` in `appsettings.json` or environment variables (`PAYOS_CLIENT_ID`, `PAYOS_API_KEY`, `PAYOS_CHECKSUM_KEY`).

### 7.2 Payment Link Request

```csharp
var request = new CreatePaymentLinkRequest
{
    OrderCode = payOSOrderCode,     // numeric, unique
    Amount = amountVnd,             // in VND (e.g. 50000)
    Description = "UPGRADE",        // max 9 chars for non-linked banks
    ReturnUrl = "https://quicksharecloud.com/upgrade",
    CancelUrl = "https://quicksharecloud.com/upgrade",
    ExpiredAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
};

var response = await client.PaymentRequests.CreateAsync(request);
// response.CheckoutUrl, response.QrCode
```

### 7.3 Webhook URL Registration

PayOS requires the webhook URL to be registered via `POST /confirm-webhook` before it sends payment notifications.
This is handled automatically at startup by `PayOSWebhookRegistrationHostedService`:

```csharp
var result = await client.Webhooks.ConfirmAsync("https://your-domain.com/api/payments/payos/webhook");
```

The webhook URL is read from `PayOS:WebhookUrl` in config. It is **not hardcoded** in `appsettings.json` —
it MUST be set per environment:

| Environment | How to set | Example value |
|-------------|-----------|---------------|
| **Development** | `PayOS__WebhookUrl` env var or `appsettings.Development.json` | `https://xxx.ngrok-free.app/api/payments/payos/webhook` |
| **Production** | Env var via CI/CD / Docker / Azure App Settings | `https://quicksharecloud.com/api/payments/payos/webhook` |

**Development with tunnel (ngrok / Cloudflare Tunnel):**
```bash
# 1. Start tunnel
ngrok http 9001
# 2. Copy the tunnel URL, set env var
set PAYOS__WEBHOOK_URL=https://xxx.ngrok-free.app/api/payments/payos/webhook
# 3. Run the app, the hosted service will auto-register the URL with PayOS
```

**Fallback**: If `WebhookUrl` is empty or points to `localhost`, the hosted service logs a warning
and skips registration. The webhook URL can then be configured manually on the
[PayOS merchant dashboard](https://my.payos.vn) (Cài đặt → Webhook URL) if needed.

### 7.4 Webhook Verification

```csharp
var verifiedData = await client.Webhooks.VerifyAsync(webhookData);
// verifiedData.OrderCode, verifiedData.Amount, verifiedData.Reference
```

### 7.5 PayOSOrderCode Generation

```csharp
public static long GenerateOrderCode()
{
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var random = Random.Shared.Next(100, 999);
    return (timestamp * 1000) + random;
}
```

Combines Unix timestamp (seconds) with 3 random digits for uniqueness. Collision check against DB before assignment.

---

## 9. Configuration

```json
{
  "PayOS": {
    "ClientId": "",
    "ApiKey": "",
    "ChecksumKey": "",
    "ReturnUrl": "https://quicksharecloud.com/upgrade",
    "CancelUrl": "https://quicksharecloud.com/upgrade"
  },
  "Pricing": {
    "ProMonthly": 50000,
    "ProYearly": 500000,
    "StudioMonthly": 200000,
    "StudioYearly": 2000000,
    "Currency": "VND"
  }
}
```

---

## 10. Billing History

(Unchanged from Ko-fi era.)

`BillingHistoryService.GetForUserAsync()` queries `payment_events` table:

```sql
SELECT * FROM payment_events
WHERE "UserId" = @userId
ORDER BY "CreatedAt" DESC
```

Results are displayed in `/account/billing` as a chronological timeline with event labels (e.g. "Payment — Pro Monthly", "Subscription cancelled").

---

## 11. File Layout

```
Features/Payments/
├── PayOS/
│   ├── PayOSService.cs                    # Payment link creation + order code generation
│   ├── PayOSWebhookEndpoint.cs            # POST /api/payments/payos/webhook
│   └── PayOSWebhookRegistrationHostedService.cs  # Auto-registers webhook at startup
├── Subscriptions/
│   └── SubscriptionService.cs    # HandlePayment, Cancel, Expire, GetCurrent
├── BillingHistoryService.cs       # Payment event query
Infrastructure/
└── SubscriptionExpiryJob.cs       # 15-min expiry job
Data/Entities/
├── PaymentIntent.cs               # Code, PayOSOrderCode, Plan, Billing, Status
├── Subscription.cs                # Plan, Status, BillingPeriod, CurrentPeriodStart/End
└── PaymentEvent.cs                # EventType, Plan, Amount, Currency, InvoiceId
Pages/
├── Upgrade.razor                  # Plan selection + PayOS payment
└── AccountBilling.razor           # Subscription status + billing history
```
