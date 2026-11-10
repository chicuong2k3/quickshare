namespace QuickShare.Data.Entities;

/// <summary>
/// One-time pay-per-transfer credit pass (issue #85).
/// Allows anonymous or Free-tier senders to pay once via PayOS to send a single large transfer
/// (up to 10 GB) with 30-day retention without subscribing to a monthly plan.
/// </summary>
public record TransferCreditPass : BaseAuditEntity
{
    public const int TokenLength = 32;
    public const int EmailMaxLength = 254;
    public const long MaxSingleTransferBytes = 10L * 1024 * 1024 * 1024; // 10 GB
    public const int ExtendedExpiryDays = 30;
    public const int DefaultPassPriceVnd = 19000;
    public const int DefaultPassPriceUsd = 2;
    public const int DefaultPassPriceEur = 2;

    public const string StatusPendingPayment = "pending_payment";
    public const string StatusAvailable = "available";
    public const string StatusRedeemed = "redeemed";
    public const string StatusExpired = "expired";

    public Guid? UserId { get; set; }
    public string? BuyerEmail { get; set; }
    public string Token { get; set; } = string.Empty;
    public int CreditsRemaining { get; set; } = 1;
    public long MaxFileSizeBytes { get; set; } = MaxSingleTransferBytes;
    public int MaxExpiryDays { get; set; } = ExtendedExpiryDays;
    public string Status { get; set; } = StatusPendingPayment;
    public Guid? PaymentIntentId { get; set; }
    public Guid? RedeemedDeliveryId { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
