namespace QuickShare.Data.Entities;

/// <summary>
/// What an API key is allowed to do (issue #115).
///
/// Deliberately coarse. Fine-grained scopes look thorough and end up as a list nobody reads,
/// picked by clicking every box — the useful distinction in practice is "can this key create
/// and destroy things, or only look at them?"
/// </summary>
public static class ApiScopes
{
    /// <summary>Read deliveries, files and stats.</summary>
    public const string Read = "read";
    /// <summary>Create and commit uploads.</summary>
    public const string Write = "write";
    /// <summary>Revoke, delete, change settings.</summary>
    public const string Manage = "manage";

    public static readonly string[] All = [Read, Write, Manage];

    /// <summary>Normalises a comma-separated list, dropping anything unrecognised.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Read;

        var scopes = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(All.Contains)
            .Distinct()
            .ToList();

        return scopes.Count == 0 ? Read : string.Join(',', scopes);
    }

    public static bool Grants(string? granted, string required) =>
        (granted ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(required, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A personal API credential (issue #115).
///
/// Stored as a SHA-256 hash, like every other credential here. The visible half —
/// <see cref="Prefix"/> — exists so somebody staring at a leaked key in a log can tell *which*
/// of their keys to revoke without being able to reconstruct it. That is the whole point of a
/// prefix: identification without capability.
/// </summary>
public record ApiKey : BaseAuditEntity
{
    public const int NameMaxLength = 60;
    public const int HashLength = 64;
    /// <summary>Characters of the raw key shown back to the owner. Enough to recognise, useless to use.</summary>
    public const int PrefixLength = 12;
    /// <summary>256 bits. Guessing is not a threat model; leaking is, which is why it is hashed.</summary>
    public const int SecretBytes = 32;
    /// <summary>Prefixes the raw value so a leaked key is greppable in a codebase or a log.</summary>
    public const string TokenPrefix = "qs_";

    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string Scopes { get; set; } = ApiScopes.Read;

    /// <summary>Null means it never expires. An expiry is the cheapest limit on a key that leaks.</summary>
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public User User { get; init; } = null!;

    public bool IsUsable(DateTime now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);
}
