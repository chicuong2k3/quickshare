namespace QuickShare.Data.Entities;

public sealed class PaymentIntent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public long PayOSOrderCode { get; set; }
    public string Plan { get; set; } = string.Empty;
    public string Billing { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentIntentStatus Status { get; set; } = PaymentIntentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public enum PaymentIntentStatus
{
    Pending,
    Completed,
    Expired
}
