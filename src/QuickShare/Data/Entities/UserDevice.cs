namespace QuickShare.Data.Entities;

/// <summary>
/// Known device for a user (issue #149). Fingerprint is a hash of UA + IP —
/// never the raw values (privacy #26). First-seen is the trigger for the
/// "new sign-in" alert email.
/// </summary>
public record UserDevice
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string FingerprintHash { get; init; } = string.Empty;
    public string UserAgentLabel { get; init; } = string.Empty;
    public string IpHash { get; set; } = string.Empty;
    public DateTime FirstSeenAt { get; init; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; }
    public string? Name { get; set; }
}
