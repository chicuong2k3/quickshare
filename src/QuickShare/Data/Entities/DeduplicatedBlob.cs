namespace QuickShare.Data.Entities;

/// <summary>
/// Content-addressed storage blob registry for chunk/file-level deduplication (issue #197).
/// Scoped per tenant (<see cref="ScopeKey"/>) so deduplication never creates a cross-tenant
/// existence oracle (#190), and never entered in privacy mode (#187).
/// </summary>
[DynamicSort]
public record DeduplicatedBlob : BaseAuditEntity
{
    public const int ScopeKeyMaxLength = 64;
    public const int Sha256Length = 64;
    public const int StoragePathMaxLength = 512;

    /// <summary>Tenant scope key ("ws:{workspaceId:N}" or "user:{userId:N}").</summary>
    public string ScopeKey { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public string? StorageRegion { get; init; }
    public long FileSize { get; init; }
    public int RefCount { get; set; } = 1;
}
