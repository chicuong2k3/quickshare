namespace QuickShare.Data.Entities;

/// <summary>Identity providers a workspace can federate to (issue #181).</summary>
public static class SsoProvider
{
    /// <summary>Google Workspace. Restricted by the `hd` (hosted domain) claim.</summary>
    public const string GoogleWorkspace = "google";
    /// <summary>Microsoft Entra ID. Restricted by the `tid` (tenant) claim.</summary>
    public const string Entra = "entra";

    public static readonly string[] All = [GoogleWorkspace, Entra];
}

/// <summary>
/// A workspace's enterprise SSO binding (issue #181).
///
/// This is deliberately *not* a second login stack. The consumer OAuth path is unchanged; what
/// this adds is the enterprise half of the same OIDC sign-in — proof that the account belongs to
/// a named directory, and a rule for what that entitles it to.
///
/// The proof is a claim the identity provider issues and the user cannot forge: Google's `hd`
/// and Entra's `tid`. An email address ending in @acme.com is not proof — anybody can hold a
/// consumer Google account with an @acme.com address on a custom domain. So
/// <see cref="AllowedEmailDomains"/> is a *narrowing* filter applied after the directory claim,
/// never the thing that establishes membership on its own.
/// </summary>
public record WorkspaceSsoConfig : BaseAuditEntity
{
    public const int DirectoryIdMaxLength = 128;
    public const int DomainsMaxLength = 512;
    public const int GroupIdMaxLength = 128;

    public Guid WorkspaceId { get; set; }
    /// <summary>See <see cref="SsoProvider"/>.</summary>
    public string Provider { get; set; } = SsoProvider.GoogleWorkspace;

    /// <summary>
    /// The directory this workspace federates to: a Google hosted domain (`acme.com`) or an
    /// Entra tenant GUID. Compared against the provider's claim on every sign-in.
    /// </summary>
    public string DirectoryId { get; set; } = string.Empty;

    /// <summary>
    /// Optional comma-separated narrowing filter on the email address, applied *after* the
    /// directory claim matches. Blank means every account in the directory qualifies.
    /// </summary>
    public string? AllowedEmailDomains { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Members of this workspace may only sign in through this provider. Off by default,
    /// because switching it on can lock out the one admin whose account is not in the
    /// directory — <c>SsoLoginService</c> refuses to enforce it against the owner for
    /// exactly that reason.
    /// </summary>
    public bool RequireSso { get; set; }

    /// <summary>
    /// New directory accounts get a seat automatically at this role (JIT provisioning).
    /// Null switches JIT off: the directory proves who somebody is, and an admin still decides
    /// whether they belong in this workspace.
    /// </summary>
    public WorkspaceRole? JitRole { get; set; } = WorkspaceRole.Member;

    /// <summary>
    /// A directory group whose members are provisioned as workspace admins instead of
    /// <see cref="JitRole"/>. Entra emits group ids in the `groups` claim; Google does not
    /// emit groups in an ID token at all, so this is Entra-only and documented as such.
    /// </summary>
    public string? AdminGroupId { get; set; }

    public Workspace Workspace { get; init; } = null!;
}

/// <summary>
/// A bearer token an identity provider uses to drive SCIM 2.0 provisioning (issue #146).
///
/// One token per workspace at a time. Only the SHA-256 lands in the database: this is a
/// credential that grants seat creation and revocation, so the same rule the invite tokens
/// follow applies here — the raw value is shown once, at creation, and never again.
/// </summary>
public record WorkspaceScimToken : BaseAuditEntity
{
    public const int TokenHashLength = 64;
    public const int LabelMaxLength = 60;
    /// <summary>256 bits of entropy, base64url — long enough that guessing is not a threat model.</summary>
    public const int TokenBytes = 32;

    public Guid WorkspaceId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Workspace Workspace { get; init; } = null!;

    public bool IsActive => RevokedAt is null;
}
