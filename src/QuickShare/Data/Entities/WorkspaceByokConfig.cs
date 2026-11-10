namespace QuickShare.Data.Entities;

/// <summary>
/// Workspace BYOK (Bring Your Own Key) KMS configuration for Studio tier (issue #163).
/// Stores customer KMS provider, key identifier, region affinity, and active/revoked state.
/// Raw KMS credentials or plaintext KEKs are never stored in the database.
/// </summary>
public record WorkspaceByokConfig : BaseAuditEntity
{
    public const int ProviderMaxLength = 24;
    public const int KeyIdMaxLength = 256;
    public const int RoleArnMaxLength = 256;

    public const string ProviderAwsKms = "aws_kms";
    public const string ProviderAzureKeyVault = "azure_kv";
    public const string ProviderGcpKms = "gcp_kms";

    public static readonly string[] ValidProviders = [ProviderAwsKms, ProviderAzureKeyVault, ProviderGcpKms];

    public const string StatusActive = "active";
    public const string StatusRevoked = "revoked";
    public const string StatusDisabled = "disabled";

    public Guid WorkspaceId { get; set; }
    public string Provider { get; set; } = ProviderAwsKms;
    public string KeyId { get; set; } = string.Empty;
    public string? RoleArn { get; set; }
    public string? KeyRegion { get; set; }
    public int KeyVersion { get; set; } = 1;
    public string Status { get; set; } = StatusActive;
    public DateTime? LastRotatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
