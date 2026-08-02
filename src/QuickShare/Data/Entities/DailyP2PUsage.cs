namespace QuickShare.Data.Entities;

/// <summary>
/// Tracks daily P2P share-link creation counts per identity.
/// Authenticated users are keyed by <see cref="UserId"/>; anonymous
/// users (no auth) are keyed by <see cref="IpAddress"/>.
/// </summary>
[DynamicSort]
public record DailyP2PUsage
{
    public Guid Id { get; init; }

    /// <summary>Null for anonymous users — those rows are keyed by <see cref="IpAddress"/>.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Used only for anonymous rows (UserId is null).</summary>
    public string IpAddress { get; init; } = string.Empty;

    public DateOnly Date { get; init; }

    /// <summary>Number of P2P share links created today by this identity.</summary>
    public int UploadCount { get; set; }

    /// <summary>Extra daily P2P upload slots granted today by rewarded ads.</summary>
    public int UnlockedUploads { get; set; }

    public User? User { get; init; }
}
