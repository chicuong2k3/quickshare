# Payment Events Schema

## `payment_events` Table

Stores every PayOS webhook event for subscription history display.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `UserId` | `uuid` | FK → `users.Id` |
| `EventType` | `text` | `"payment"` for PayOS one-time/subscription payments |
| `Plan` | `text` | `pro` or `studio` |
| `PlanDisplay` | `text` | Human-friendly: `"Pro"`, `"Studio"` |
| `Billing` | `text` | `monthly` or `yearly` |
| `Amount` | `decimal` | Payment amount in VND |
| `Currency` | `text` | `"VND"` (PayOS default) |
| `InvoiceId` | `text` | PayOS transaction reference |
| `InvoiceUrl` | `text` | PayOS receipt URL (optional, not always available) |
| `CreatedAt` | `timestamp with time zone` | When webhook was received (set by `AuditableEntityInterceptor`) |

## Webhook Data → Row Mapping

| PayOS Webhook Field | Column |
|---------------------|--------|
| `data.orderCode` | Stored on `PaymentIntent.PayOSOrderCode` |
| `data.amount` | `Amount` |
| `data.currency` | `Currency` |
| `data.reference` | `InvoiceId` |
| `data.paymentLinkId` | `InvoiceUrl` (optional) |

## Account Billing Page Query

The `/account/billing` page needs:
1. Current user's `Plan` + `PlanDisplay` from the `users` table
2. Most recent `payment_events` row for this user (to show next billing date: `CreatedAt + 1 month/year`)
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
    public string Currency { get; set; } = "VND";
    public string InvoiceId { get; set; } = "";
    public string InvoiceUrl { get; set; } = "";

    public User User { get; set; } = null!;
}
```
