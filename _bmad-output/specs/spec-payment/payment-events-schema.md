# Payment Events Schema

## `payment_events` Table

Stores every Ko-fi webhook event for subscription history display.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `UserId` | `uuid` | FK → `users.Id` |
| `EventType` | `text` | `subscription_created`, `subscription_renewed`, `subscription_cancelled`, `subscription_expired` |
| `Plan` | `text` | `pro` or `studio` |
| `PlanDisplay` | `text` | Human-friendly: `"Pro"`, `"Studio"` |
| `Billing` | `text` | `monthly` or `yearly` |
| `Amount` | `decimal` | Payment amount in USD |
| `Currency` | `text` | `"USD"` (Ko-fi default) |
| `InvoiceId` | `text` | Ko-fi invoice/reference ID |
| `InvoiceUrl` | `text` | Ko-fi receipt URL (from webhook body) |
| `CreatedAt` | `timestamp with time zone` | When webhook was received (set by `AuditableEntityInterceptor`) |

## Webhook Event → Row Mapping

| Webhook Field | Column |
|---------------|--------|
| `type` | `EventType` |
| `tier_name` (or product lookup) | `Plan`, `PlanDisplay` |
| `is_recurring_yearly` | `Billing` |
| `amount` | `Amount` |
| `currency` | `Currency` |
| `kofi_transaction_id` | `InvoiceId` |
| `receipt_url` or `invoice_url` | `InvoiceUrl` |

## Account Billing Page Query

The `/account/billing` page needs:
1. Current user's `Plan` + `PlanDisplay` from the `users` table
2. Most recent `payment_events` row for this user where `EventType = 'subscription_created'` or `'subscription_renewed'` (to show next billing date: `CreatedAt + 1 month/year`)
3. All `payment_events` for this user, ordered by `CreatedAt DESC`, for the history timeline

## Billing Period Display Logic

| Billing | Next Billing |
|---------|-------------|
| `monthly` | `CreatedAt` + 30 days |
| `yearly` | `CreatedAt` + 365 days |

## EF Core Entity

```csharp
public class PaymentEvent : BaseAuditEntity
{
    public Guid UserId { get; set; }
    public string EventType { get; set; } = "";
    public string Plan { get; set; } = "";
    public string PlanDisplay { get; set; } = "";
    public string Billing { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string InvoiceId { get; set; } = "";
    public string InvoiceUrl { get; set; } = "";

    public User User { get; set; } = null!;
}
```
