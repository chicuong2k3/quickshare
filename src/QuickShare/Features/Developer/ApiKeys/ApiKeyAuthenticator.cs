using System.Security.Cryptography;
using System.Text;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Developer.ApiKeys;

/// <summary>
/// Resolves the account behind an API key (issue #115).
///
/// Machine callers have no cookie, so the key is the whole authorisation. Every lookup is by
/// hash: a database dump must not hand anybody a working credential, which is the same rule the
/// invite tokens and the SCIM tokens already follow.
/// </summary>
public sealed class ApiKeyAuthenticator(AppDbContext db)
{
    public const string HeaderName = "Authorization";
    private const string BearerPrefix = "Bearer ";
    /// <summary>Also accepted, because half the world reaches for this header name first.</summary>
    public const string AlternateHeaderName = "X-Api-Key";

    /// <summary>The key row for this request, or null. Records use, so a key's age is visible.</summary>
    public async Task<ApiKey?> ResolveAsync(HttpContext context, CancellationToken ct)
    {
        var raw = ReadRawKey(context);
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith(ApiKey.TokenPrefix, StringComparison.Ordinal))
            return null;

        var hash = WorkspaceService.HashToken(raw);
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, ct);

        if (key is null || !key.IsUsable(DateTime.UtcNow))
            return null;

        // Written every call. API traffic is the one place where "when was this last used" is the
        // question an owner asks before revoking, and a stale answer makes them keep a live key.
        key.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return key;
    }

    private static string? ReadRawKey(HttpContext context)
    {
        var header = context.Request.Headers[HeaderName].ToString();
        if (!string.IsNullOrEmpty(header) && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return header[BearerPrefix.Length..].Trim();

        var alternate = context.Request.Headers[AlternateHeaderName].ToString();
        return string.IsNullOrWhiteSpace(alternate) ? null : alternate.Trim();
    }

    /// <summary>
    /// A fresh key: the raw value for the owner, the hash to store, and the prefix to show back.
    ///
    /// Base64url rather than hex — shorter for the same entropy, and it survives being pasted
    /// into a shell, a YAML file and an environment variable without quoting surprises.
    /// </summary>
    public static (string Raw, string Hash, string Prefix) Generate()
    {
        var body = Convert.ToBase64String(RandomNumberGenerator.GetBytes(ApiKey.SecretBytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var raw = ApiKey.TokenPrefix + body;
        return (raw, WorkspaceService.HashToken(raw), raw[..Math.Min(raw.Length, ApiKey.PrefixLength)]);
    }

    /// <summary>
    /// HMAC-SHA256 over <c>{timestamp}.{body}</c> (issue #172).
    ///
    /// The timestamp is inside the signed material on purpose: signing only the body means a
    /// captured request stays valid forever, and the receiver has no way to tell a replay from
    /// the real thing. This is the same construction the SIEM sink uses, so there is one shape to
    /// document and one to get right.
    /// </summary>
    public static string Sign(string secret, long timestamp, string body)
    {
        var material = $"{timestamp}.{body}";
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }
}
