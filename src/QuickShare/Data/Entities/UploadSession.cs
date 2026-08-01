namespace QuickShare.Data.Entities;

[DynamicSort]
public record UploadSession : BaseAuditEntity
{
    public Guid SenderId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Status { get; set; } = UploadSessionStatus.InProgress;
    public DateTime ExpiresAt { get; init; }
    public string? R2UploadId { get; set; }
    public string? StoragePath { get; set; }
}

public static class UploadSessionStatus
{
    public const string InProgress = "InProgress";
}
