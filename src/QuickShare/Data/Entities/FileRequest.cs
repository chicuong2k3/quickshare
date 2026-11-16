namespace QuickShare.Data.Entities;

/// <summary>
/// A link that lets anyone (no account needed) send files to the owner — the
/// "file request" page (issue #16). The owner sets the title, an optional
/// message, and per-plan caps (total bytes, file count, expiry). Anonymous
/// submitters upload into a Delivery that references this request.
/// </summary>
[DynamicSort]
public record FileRequest : BaseAuditEntity
{
    public const int CodeMaxLength = 16;
    public const int TitleMaxLength = 100;
    public const int MessageMaxLength = 500;

    public Guid OwnerId { get; init; }
    /// <summary>URL-safe public identifier used in /r/{code}. Unique.</summary>
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Message { get; init; }
    /// <summary>Combined byte cap for all files submitted to this request.</summary>
    public long MaxTotalBytes { get; init; }
    /// <summary>Max number of files that can be submitted.</summary>
    public int MaxFiles { get; init; }
    public DateTime ExpiresAt { get; init; }
    /// <summary>Soft-close timestamp (owner closed it); null = still open.</summary>
    public DateTime? ClosedAt { get; set; }
    public string Kind { get; init; } = FileRequestKind.Request;

    public User? Owner { get; init; }
    public ICollection<Delivery> Deliveries { get; init; } = [];
}

public static class FileRequestKind
{
    public const string Request = "request";
    /// <summary>Shared room (issue #17) — everyone can upload and see each other's files.</summary>
    public const string Room = "room";
}