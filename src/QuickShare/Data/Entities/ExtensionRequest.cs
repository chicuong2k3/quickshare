namespace QuickShare.Data.Entities;

/// <summary>
/// A recipient's request for more time on an expired delivery (issue #233). Anonymous:
/// the requester is identified by a browser cookie token, never an identity.
/// </summary>
[DynamicSort]
public record ExtensionRequest : BaseAuditEntity
{
    public const int RejectMessageMaxLength = 500;
    public const int RequesterTokenMaxLength = 64;

    public Guid DeliveryId { get; init; }
    public string RequesterToken { get; init; } = string.Empty;
    public string Status { get; set; } = ExtensionRequestStatus.Pending;
    public DateTime? RespondedAt { get; set; }
    public string? RejectMessage { get; set; }

    public Delivery? Delivery { get; init; }
}

public static class ExtensionRequestStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
