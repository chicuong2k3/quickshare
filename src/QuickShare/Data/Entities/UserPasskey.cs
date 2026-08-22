namespace QuickShare.Data.Entities;

/// <summary>
/// A registered WebAuthn credential (issue #68).
///
/// The public key is public — storing it in the clear is the design, not a compromise. What makes
/// a passkey a passkey is that the private half never leaves the authenticator, so a database
/// dump yields nothing anybody can sign with.
/// </summary>
public record UserPasskey : BaseAuditEntity
{
    public const int NameMaxLength = 60;
    /// <summary>Base64url of the credential id. Long enough for the largest authenticators emit.</summary>
    public const int CredentialIdMaxLength = 512;
    public const int PublicKeyMaxLength = 2048;

    public Guid UserId { get; set; }

    /// <summary>Base64url credential id, as the authenticator reports it. Unique across accounts.</summary>
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>COSE public key, base64. Verifying an assertion needs it back, so it is not hashed.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// The authenticator's signature counter.
    ///
    /// A counter that goes backwards is the one clone signal WebAuthn gives us: two devices
    /// holding the same credential will drift apart. Many authenticators legitimately report 0
    /// and never move — that is not a clone, it is a platform that does not implement counters,
    /// so a zero counter is not treated as evidence of anything.
    /// </summary>
    public uint SignCount { get; set; }

    /// <summary>Authenticator model id. Shown so somebody can tell their phone from their key.</summary>
    public Guid? AaGuid { get; set; }

    public string? Name { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public User User { get; init; } = null!;
}

/// <summary>
/// A WebAuthn ceremony in flight (issue #68).
///
/// The challenge has to survive the round trip between "begin" and "complete", and it must be
/// server-generated and single-use or the whole protocol collapses into a replay.
///
/// Deliberately a table rather than the cache: `CacheService` no-ops when caching is disabled,
/// which would silently turn a security control off from a config file — the same reasoning the
/// SAML assertion store follows.
/// </summary>
public record PasskeyChallenge
{
    public const int HandleLength = 43;
    public const int OptionsMaxLength = 4096;

    /// <summary>How long a ceremony may take. Generous for a slow authenticator, short for a replay.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public Guid Id { get; init; }

    /// <summary>Opaque handle the client sends back. Random, unguessable.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>The serialized options, so completion verifies against exactly what was issued.</summary>
    public string OptionsJson { get; init; } = string.Empty;

    /// <summary>Set for registration (we know who); null for a login ceremony (we do not, yet).</summary>
    public Guid? UserId { get; init; }

    public DateTime ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// A single-use sign-in link (issue #132).
///
/// Stored as a hash, like every other credential here: the raw token goes in one email and
/// nowhere else. Anybody holding it can sign in, which is the entire point and also the reason
/// it lives for minutes rather than days.
/// </summary>
public record LoginToken
{
    public const int TokenHashLength = 64;
    public const int EmailMaxLength = 254;

    /// <summary>
    /// Short on purpose. A magic link sits in an inbox, and an inbox is a place other people
    /// sometimes have access to; the window is the main thing limiting that.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string TokenHash { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public bool IsUsable(DateTime now) => UsedAt is null && ExpiresAt > now;
}
