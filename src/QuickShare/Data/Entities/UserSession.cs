namespace QuickShare.Data.Entities;

/// <summary>
/// One signed-in session (issues #105, #173).
///
/// The auth cookie alone is self-contained: it proves who somebody is and expires on its own
/// schedule, and nothing on the server can take it back. That is fine right up until it is not —
/// a stolen laptop, an offboarded employee, a shared machine at a client site — and at that point
/// "sign out everywhere" has to mean something.
///
/// So the cookie carries a session id and this row decides whether it still counts. The price is
/// one indexed read per authenticated request. That is the actual cost of sessions you can
/// revoke, and it is worth paying: the alternative is a revoke button that does nothing until
/// the cookie happens to expire.
/// </summary>
public record UserSession
{
    public const int SessionIdLength = 43;
    public const int LabelMaxLength = 100;

    /// <summary>
    /// How long a session may sit unused before it stops counting (issue #173).
    ///
    /// Separate from the cookie's own 30-day lifetime: that one asks "how long may somebody stay
    /// signed in", this asks "how long after they walked away". A shared machine is the case that
    /// makes the difference matter.
    /// </summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromDays(14);

    /// <summary>
    /// How stale <see cref="LastSeenAt"/> may get before it is written again.
    ///
    /// Writing on every request would turn every page load into a database write for a value
    /// nobody reads at that precision.
    /// </summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    public Guid Id { get; init; }
    public Guid UserId { get; init; }

    /// <summary>Opaque id carried in the cookie. Random, not derived from anything about the user.</summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>Summarised user agent — enough to recognise the device, not to fingerprint it.</summary>
    public string UserAgentLabel { get; init; } = string.Empty;

    /// <summary>Hashed, never the raw address (privacy #26).</summary>
    public string IpHash { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }

    /// <summary>Live: not revoked, and used recently enough to still count.</summary>
    public bool IsActive(DateTime now) =>
        RevokedAt is null && LastSeenAt.Add(IdleTimeout) > now;
}
