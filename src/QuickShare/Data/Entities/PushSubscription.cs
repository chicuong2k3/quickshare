namespace QuickShare.Data.Entities;

/// <summary>
/// A browser's web-push subscription (issue #103). Opt-in: a user grants permission and
/// the browser hands back endpoint + keys, stored per device.
/// </summary>
[DynamicSort]
public record PushSubscription : BaseAuditEntity
{
    public const int EndpointMaxLength = 1024;
    public const int KeyMaxLength = 256;
    public const int DeviceNameMaxLength = 100;

    public Guid UserId { get; init; }
    public string Endpoint { get; init; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? DeviceName { get; init; }

    public User? User { get; init; }
}
