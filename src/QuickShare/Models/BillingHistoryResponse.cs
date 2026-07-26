namespace QuickShare.Models;

public sealed record BillingHistoryResponse(
    string Plan,
    string PlanDisplay,
    List<PaymentEventItem> Events
);

public sealed record PaymentEventItem(
    string EventType,
    string EventLabel,
    string PlanDisplay,
    string Billing,
    decimal Amount,
    string Currency,
    string InvoiceId,
    string InvoiceUrl,
    DateTime CreatedAt
);
