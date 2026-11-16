namespace QuickShare.Data.Entities;

/// <summary>
/// Third-party automation connection for Zapier / Make no-code workflows (issue #186).
/// Scoped using <see cref="ApiScopes"/>, revocable at any time, and audited in <see cref="SecurityEvent"/>.
/// </summary>
[DynamicSort]
public record AutomationConnection : BaseAuditEntity
{
    public const int NameMaxLength = 80;
    public const int PlatformMaxLength = 32;
    public const int PrefixLength = 12;
    public const int HashLength = 64;
    public const string TokenPrefix = "qsa_";

    public const string PlatformZapier = "zapier";
    public const string PlatformMake = "make";
    public const string PlatformCustom = "custom";

    public Guid UserId { get; init; }
    public Guid? WorkspaceId { get; set; }
    public string Platform { get; set; } = PlatformZapier;
    public string Name { get; set; } = string.Empty;
    public string Scopes { get; set; } = ApiScopes.Read;
    public string TokenPrefixValue { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public Guid? WebhookEndpointId { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null;
}
