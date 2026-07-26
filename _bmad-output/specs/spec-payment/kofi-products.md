# Ko-fi Product Mapping

## Product IDs → Plan

Each plan/billing combination has a unique Ko-fi product. The webhook handler maps incoming product IDs to QuickShareCloud plans.

| Plan | Billing | Ko-fi Product Slug | Price |
|------|---------|-------------------|-------|
| Pro | Monthly | `pro-monthly` | $9 |
| Pro | Yearly | `pro-yearly` | $77 |
| Studio | Monthly | `studio-monthly` | $29 |
| Studio | Yearly | `studio-yearly` | $239 |

## Redirect URL Template

`https://ko-fi.com/{product-slug}?return_url=https://quicksharecloud.com/`

## Webhook Configuration

- **Webhook URL:** `https://quicksharecloud.com/api/payments/kofi/webhook`
- **Verification token:** configured via `Kofi:VerificationToken` in appsettings / environment variable
- **Events received:** `subscription_created`, `subscription_renewed`, `subscription_cancelled`, `subscription_expired`
- **Signature validation:** Ko-fi sends a `verification_token` field in the JSON body. The handler compares it against `Kofi:VerificationToken`. Mismatch → 401.

## Webhook Event → Plan Transition

| Event | Plan Change | Notes |
|-------|------------|-------|
| `subscription_created` | free → pro / studio | First payment confirmed |
| `subscription_renewed` | no change | Extends existing plan period |
| `subscription_cancelled` | no change | Graceful — user stays on plan until period end |
| `subscription_expired` | pro/studio → free | Plan drops; quota enforcement applies next upload |

## Configuration Keys (appsettings.json or env)

```json
{
  "Kofi": {
    "VerificationToken": "",
    "Products": {
      "ProMonthly": "pro-monthly",
      "ProYearly": "pro-yearly",
      "StudioMonthly": "studio-monthly",
      "StudioYearly": "studio-yearly"
    },
    "ManageSubscriptionUrl": "https://ko-fi.com/account/manage",
    "CancelSubscriptionUrl": "https://ko-fi.com/account/cancel"
  }
}
```

## Subscription Management Redirect URLs

| Action | Target | Notes |
|--------|--------|-------|
| Change Plan | `Kofi:ManageSubscriptionUrl` | Ko-fi subscription management page |
| Cancel | `Kofi:CancelSubscriptionUrl` | Direct link to cancel flow |
| View Receipt | From webhook's `invoice_url` field | Per-event Ko-fi receipt URL; stored in `payment_events.invoice_url` |
