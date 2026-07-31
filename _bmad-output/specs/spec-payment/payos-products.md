# PayOS Product Mapping

## Pricing → Plan

Pricing is configured in `appsettings.json` under `Pricing`. Each plan/billing combination maps to a PayOS payment link amount.

| Plan | Billing | Amount (VND) |
|------|---------|-------------|
| Pro | Monthly | 50,000 |
| Pro | Yearly | 500,000 |
| Studio | Monthly | 200,000 |
| Studio | Yearly | 2,000,000 |

## PayOS API Usage

- **Create payment link:** `POST /v2/payment-requests` — creates a PayOS-hosted checkout page with VietQR code
- **Checkout URL:** `https://pay.payos.vn/web/{paymentLinkId}` — user opens in browser or scans QR
- **Return URL:** `https://quicksharecloud.com/upgrade` — user redirected here after payment
- **Cancel URL:** `https://quicksharecloud.com/upgrade` — user redirected here if cancelled

## Webhook Configuration

- **Webhook URL:** `https://quicksharecloud.com/api/payments/payos/webhook`
- **Authentication:** HMAC-SHA256 signature using `ChecksumKey` (configured via `PayOS:ChecksumKey`)
- **Signature verification:** SDK `Webhooks.VerifyAsync()` re-computes signature from received `data` and compares with `signature` field. Mismatch → 401.

## Webhook Event → Plan Transition

The PayOS webhook confirms a one-time payment. Plan activation is handled in-app by `SubscriptionService`:

| Event | Plan Change | Notes |
|-------|------------|-------|
| Payment confirmed | free → pro / studio | First payment activates the plan |
| Payment confirmed (renewal) | no change | Extends existing plan period |

## Configuration Keys (appsettings.json or env)

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

## PayOS Webhook Payload

```json
{
  "code": "00",
  "desc": "success",
  "success": true,
  "data": {
    "orderCode": 1722345678000,
    "amount": 50000,
    "description": "UPGRADE",
    "accountNumber": "12345678",
    "reference": "TF230204212323",
    "transactionDateTime": "2024-01-15 10:30:00",
    "currency": "VND",
    "paymentLinkId": "124c33293c43417ab7879e14c8d9eb18",
    "code": "00",
    "desc": "Thành công"
  },
  "signature": "8d8640d802576397a1ce45ebda7f835055768ac7ad2e0bfb77f9b8f12cca4c7f"
}
```

## Matching Flow

1. PayOS webhook arrives → `code = "00"` + `success = true` → valid payment
2. SDK verifies HMAC signature → rejects if tampered
3. `data.orderCode` (numeric) → lookup `PaymentIntent.PayOSOrderCode`
4. Found → activate subscription via `SubscriptionService`
5. Not found → log warning, return 200 (acknowledge webhook)

## EF Core Entities

### PaymentIntent (updated for PayOS)

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `Guid` | PK |
| `UserId` | `Guid` | FK → users |
| `Code` | `string` | Internal QSC-XXXXXX code (for tracking) |
| `PayOSOrderCode` | `long` | PayOS numeric order code (unique, indexed) |
| `Plan` | `string` | `pro` / `studio` |
| `Billing` | `string` | `monthly` / `yearly` |
| `Amount` | `decimal` | Amount in VND |
| `Currency` | `string` | `"VND"` |
| `Status` | `enum` | Pending / Completed / Expired |
| `CreatedAt` | `DateTime` | UTC |
| `CompletedAt` | `DateTime?` | UTC |
| `ExpiresAt` | `DateTime` | UTC (1 hour from creation) |
