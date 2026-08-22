namespace QuickShare.Data.Entities;

/// <summary>
/// A checkout in flight. Carries the promo code and proration credit that produced its amount
/// (issues #98, #114).
/// </summary>
public sealed class PaymentIntent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public long PayOSOrderCode { get; set; }

    /// <summary>
    /// Which rail this intent is being paid on (issue #225). Defaults to the card/bank provider
    /// so every row written before crypto existed still says something true.
    /// </summary>
    public string Provider { get; set; } = PaymentProviders.PayOS;

    /// <summary>The provider's own id for a hosted charge; null for the PayOS order-code path.</summary>
    public string? ProviderChargeId { get; set; }
    public string Plan { get; set; } = string.Empty;
    public string Billing { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentIntentStatus Status { get; set; } = PaymentIntentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The promo code this amount reflects (issue #98), or null. Kept so completion knows which
    /// code to consume — the quote happened at checkout, the redemption happens on payment.
    /// </summary>
    public string? PromoCode { get; set; }

    /// <summary>
    /// Proration credit applied (issue #114), in the smallest currency unit. Kept so a discounted
    /// payment can still be reconciled against a list price rather than looking like underpayment.
    /// </summary>
    public int ProrationCredit { get; set; }
}

public enum PaymentIntentStatus
{
    Pending,
    Completed,
    Expired
}
