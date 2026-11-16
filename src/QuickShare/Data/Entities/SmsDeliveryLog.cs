namespace QuickShare.Data.Entities;

/// <summary>
/// SMS share log and opt-out registry (issue #269).
/// Supports Vietnamese (+84) and international E.164 numbers, 160-character SMS budget,
/// PDPD recipient consent, and STOP opt-out suppression.
/// </summary>
public record SmsDeliveryLog : BaseAuditEntity
{
    public const int PhoneMaxLength = 24;
    public const int MessageMaxLength = 160;
    public const decimal CostPerSmsVnd = 800m;

    public const string StatusSent = "sent";
    public const string StatusSuppressed = "suppressed";

    public Guid DeliveryId { get; set; }
    public Guid SenderUserId { get; set; }
    public string RecipientPhone { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
    public string Status { get; set; } = StatusSent;
    public decimal CostVnd { get; set; } = CostPerSmsVnd;
}

/// <summary>
/// Phone numbers that replied STOP or opted out of SMS delivery notifications (issue #269).
/// </summary>
public record SmsOptOut : BaseAuditEntity
{
    public const int PhoneMaxLength = 24;

    public string PhoneNumber { get; set; } = string.Empty;
    public string Reason { get; set; } = "STOP";
}
