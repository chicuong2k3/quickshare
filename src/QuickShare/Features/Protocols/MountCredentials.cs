using System.Text;
using QuickShare.Features.Developer.ApiKeys;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Protocols;

/// <summary>Who is mounting, and what their key is allowed to do (issues #195, #265).</summary>
public sealed record MountSession(User User, Guid WorkspaceId, string Scopes)
{
    public bool Grants(string scope) => ApiScopes.Grants(Scopes, scope);
}

/// <summary>
/// Resolves a mount request to a workspace seat (issues #195, #265).
///
/// The credential is an existing API key (#115) — no second kind of secret, no second place to
/// revoke. Filesystem and object-storage clients send it as HTTP Basic, because that is the only
/// authentication every one of them speaks; the username is ignored and the password is the key,
/// which is the same shape GitHub and every S3-alike use for tokens over Basic.
///
/// There is no anonymous mount and there never will be: a drive with no credential is a
/// workspace's files on the open internet.
/// </summary>
public sealed class MountCredentials(ApiKeyAuthenticator keys, WorkspaceService workspaces, AppDbContext db)
{
    public const string BasicPrefix = "Basic ";
    public const string Realm = "QuickShare";

    /// <summary>Null when the key is missing, unusable, or holds no live workspace seat.</summary>
    public async Task<MountSession?> ResolveAsync(HttpContext context, CancellationToken ct)
    {
        var key = await keys.ResolveAsync(context, ct) ?? await ResolveBasicAsync(context, ct);
        if (key is null)
            return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == key.UserId, ct);
        if (user is null)
            return null;

        // A mount is a team surface. No seat, no drive — a personal account has nothing to
        // mount, and saying so is clearer than presenting an empty one.
        var seat = await workspaces.GetSeatAsync(user.Id, ct);
        if (seat is null || !seat.IsActive)
            return null;

        return new MountSession(user, seat.WorkspaceId, key.Scopes);
    }

    /// <summary>Writes the challenge that makes a client ask its user for the key.</summary>
    public static void Challenge(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{Realm}\", charset=\"UTF-8\"";
    }

    private async Task<ApiKey?> ResolveBasicAsync(HttpContext context, CancellationToken ct)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BasicPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[BasicPrefix.Length..].Trim()));
        }
        catch (FormatException)
        {
            return null;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
            return null;

        // The password position carries the key. Some clients refuse an empty username, so the
        // username is accepted and ignored rather than checked against anything.
        var secret = decoded[(separator + 1)..];
        if (string.IsNullOrEmpty(secret))
            return null;

        var hash = WorkspaceService.HashToken(secret);
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, ct);

        if (key is null || !key.IsUsable(DateTime.UtcNow))
            return null;

        key.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return key;
    }
}
