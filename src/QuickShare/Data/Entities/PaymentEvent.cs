namespace QuickShare.Data.Entities;

[DynamicSort]
public record PaymentEvent : BaseAuditEntity
{
    public Guid UserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public string PlanDisplay { get; set; } = string.Empty;
    public string Billing { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public string InvoiceId { get; set; } = string.Empty;
    public string InvoiceUrl { get; set; } = string.Empty;

    public User User { get; init; } = null!;
}
