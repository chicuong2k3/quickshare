namespace QuickShare.Data.Entities;

/// <summary>
/// Per-workspace SSH public key authorized for SFTP ingest into workspace folders (issue #196).
/// Stores SHA256 fingerprint and public key text; revoked keys stay for audit history.
/// </summary>
public record WorkspaceSftpKey : BaseAuditEntity
{
    public const int LabelMaxLength = 100;
    public const int PublicKeyMaxLength = 2000;
    public const int FingerprintMaxLength = 128;

    public Guid WorkspaceId { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string Label { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public Guid? DefaultFolderId { get; set; }
    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// Linked GitHub repository for automatic release-asset mirroring into QuickShare deliveries (issue #198).
/// </summary>
public record GitHubReleaseRepo : BaseAuditEntity
{
    public const int RepoFullNameMaxLength = 200;
    public const int WebhookSecretMaxLength = 128;

    public Guid WorkspaceId { get; init; }
    public Guid CreatedByUserId { get; init; }
    public string RepoFullName { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public Guid? TargetFolderId { get; set; }
    public int DefaultExpiryHours { get; set; } = 168;
    public bool Active { get; set; } = true;
}
