namespace QuickShare.Data.Entities;

/// <summary>
/// An access key pair for the S3-compatible gateway (issue #265).
///
/// This is the only credential in the product whose secret is stored **reversibly**, and the
/// reason is the protocol rather than convenience: SigV4 is a shared-secret HMAC, so verifying a
/// signature means recomputing it from the same bytes the client holds. A hash cannot do that.
///
/// So it is a separate row from the API key (#115) instead of reusing one. Reusing would have
/// meant storing API keys reversibly too — trading the hashing invariant that protects every
/// other caller for the convenience of one protocol. Here the blast radius is exactly this
/// credential: it is protected with the app's DataProtection keyring, it is scoped to one
/// workspace, and revoking it is a single row.
/// </summary>
public record WorkspaceS3Credential : BaseAuditEntity
{
    public const int LabelMaxLength = 60;
    /// <summary>Twenty characters, like the keys every S3 client expects to paste.</summary>
    public const int AccessKeyIdLength = 20;
    /// <summary>Random bytes behind the secret. 30 bytes → a 40-character base64 secret.</summary>
    public const int SecretBytes = 30;
    public const int ProtectedMaxLength = 1000;
    /// <summary>Prefix so a leaked key is greppable and obviously ours.</summary>
    public const string AccessKeyPrefix = "QS";

    public Guid WorkspaceId { get; set; }
    /// <summary>The account the gateway acts as: quota, plan and audit all follow this user.</summary>
    public Guid UserId { get; set; }

    public string Label { get; set; } = string.Empty;
    public string AccessKeyId { get; set; } = string.Empty;
    /// <summary>DataProtection-protected secret. Never returned after the row is created.</summary>
    public string SecretProtected { get; set; } = string.Empty;

    /// <summary>Same coarse scopes the API keys use, so one mental model covers both.</summary>
    public string Scopes { get; set; } = ApiScopes.Read;

    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Workspace Workspace { get; init; } = null!;
    public User User { get; init; } = null!;

    public bool IsUsable => RevokedAt is null;
}
