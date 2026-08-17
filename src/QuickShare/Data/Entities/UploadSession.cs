namespace QuickShare.Data.Entities;

[DynamicSort]
public record UploadSession : BaseAuditEntity
{
    public Guid? SenderId { get; init; }
    public string? SenderEmail { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Status { get; set; } = UploadSessionStatus.InProgress;
    public DateTime ExpiresAt { get; init; }
    public string? R2UploadId { get; set; }
    public string? StoragePath { get; set; }
    /// <summary>When set, this session replaces an existing delivery (issue #72) instead of creating a new one.</summary>
    public Guid? ReplaceDeliveryId { get; init; }
    /// <summary>When set, this upload is a submission to a file request (issue #16).</summary>
    public Guid? FileRequestId { get; init; }
}

public static class UploadSessionStatus
{
    public const string InProgress = "InProgress";
    public const string Committed = "Committed";
}
