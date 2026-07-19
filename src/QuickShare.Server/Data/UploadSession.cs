namespace QuickShare.Server.Data;

[DynamicSort]
public record UploadSession : BaseAuditEntity
{
    public Guid SenderId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string Status { get; set; } = "InProgress";
    public DateTime ExpiresAt { get; init; }
}
