namespace QuickShare.Features.Users.Shared;

/// <summary>
/// Append-only writer for the account security audit trail (issue #126). Every
/// event is a new row — nothing is ever updated or deleted by this service. The IP
/// is hashed server-side before it is stored (privacy #26), so the raw address
/// never reaches the database; the user-agent is summarized to a stable label.
/// </summary>
public static class SecurityEventService
{
    /// <summary>
    /// Appends one immutable security event. IP is hashed via the same SHA-256 used
    /// by device fingerprints; user-agent is collapsed with the shared summarizer.
    /// </summary>
    public static async Task AppendAsync(
        AppDbContext db,
        Guid userId,
        SecurityEventType type,
        string userAgent,
        string ip,
        CancellationToken ct,
        string? metadataJson = null)
    {
        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            EventType = type,
            IpHash = DeviceTrackerService.HashIp(ip),
            UserAgentLabel = DeviceTrackerService.SummarizeUserAgent(userAgent),
            MetadataJson = metadataJson,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}
