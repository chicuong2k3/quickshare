namespace QuickShare.Data.Entities;

/// <summary>
/// A workspace's own hostname for the pages its clients see (issue #81).
///
/// White-label is the composition of a brand (#285/#286) and an address: a client who lands on
/// <c>files.studio-nam.com</c> and sees the studio's logo never learns which vendor moved the
/// bytes. The brand was the easy half — this is the half that has to be *earned*, because a
/// hostname is a claim about property somebody else may own.
///
/// Verification is therefore a DNS check, not a checkbox: the host must already resolve to the
/// same place the canonical app does, which is exactly the CNAME the agency has to publish for
/// the domain to work at all. Nothing is served under an unverified host, so a typo — or a host
/// somebody else controls — costs its owner nothing.
/// </summary>
public record WorkspaceDomain : BaseAuditEntity
{
    public const int HostMaxLength = 253;
    public const int TokenLength = 32;
    public const int ErrorMaxLength = 200;

    public Guid WorkspaceId { get; set; }

    /// <summary>Lowercased hostname, no scheme and no port. Unique across all workspaces.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Published by the admin as part of the setup record, and shown back to them so support can
    /// match a request to a workspace without reading the database.
    /// </summary>
    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>Null until DNS agreed. Everything that reads a domain reads this, not <see cref="Host"/>.</summary>
    public DateTime? VerifiedAt { get; set; }

    public DateTime? LastCheckedAt { get; set; }
    /// <summary>Why the last check failed, for the admin's own screen. Never shown to a recipient.</summary>
    public string? LastError { get; set; }

    public Workspace Workspace { get; init; } = null!;

    public bool IsVerified => VerifiedAt is not null;
}
