namespace QuickShare.Data.Entities;

/// <summary>
/// A paid subscription bought as a gift for another person (issue #102).
///
/// The buyer pays through the normal checkout flow; once the intent completes, a single-use
/// claim token is issued to <see cref="RecipientEmail"/>. Claiming activates or extends the
/// recipient's subscription without charging them again. Self-gifting is refused at creation.
/// </summary>
public record SubscriptionGift : BaseAuditEntity
{
    public const int EmailMaxLength = 254;
    public const int MessageMaxLength = 500;
    public const int TokenLength = 32;
    public const int ClaimWindowDays = 90;

    public const string StatusPendingPayment = "pending_payment";
    public const string StatusClaimable = "claimable";
    public const string StatusClaimed = "claimed";
    public const string StatusExpired = "expired";

    public Guid SenderUserId { get; set; }
    public string RecipientEmail { get; set; } = string.Empty;
    public string Plan { get; set; } = PlanTierLimits.Pro;
    public string Billing { get; set; } = BillingPeriods.Monthly;
    public int Months { get; set; } = 1;
    public string? Message { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Status { get; set; } = StatusPendingPayment;
    public Guid? PaymentIntentId { get; set; }
    public Guid? ClaimedByUserId { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    public User Sender { get; init; } = null!;
}
