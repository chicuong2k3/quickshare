namespace QuickShare.Data.Entities;

/// <summary>
/// Append-only security audit trail for an account (issue #126). One row per
/// security-relevant action (sign-in, new device, session revoke, ...). The IP is
/// captured server-side and hashed — the raw address is never stored (privacy #26).
/// Rows are immutable; retention is 90 days via <see cref="SecurityEventCleanupJob"/>.
/// </summary>
public record SecurityEvent
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public SecurityEventType EventType { get; init; }
    public string IpHash { get; init; } = string.Empty;
    public string UserAgentLabel { get; init; } = string.Empty;
    public string? MetadataJson { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Security-relevant actions recorded on the account timeline (issue #126). Only
/// the events wired into existing flows are written today (Login, DeviceAdded,
/// SessionRevoked); the rest are reserved for the features that will record them.
/// </summary>
public enum SecurityEventType
{
    Login,
    LoginFailed,
    DeviceAdded,
    SessionRevoked,
    PasskeyAdded,
    PasskeyRemoved,
    RecoveryUsed,
    ApiKeyCreated,
    ApiKeyRevoked,
    EmailChanged,
    PasswordChanged,
}
