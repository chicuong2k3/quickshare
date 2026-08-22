using System.Security.Claims;
using QuickShare.Features.Auth.Totp;

namespace QuickShare.Features.Developer.ApiKeys;

/// <summary>
/// Lets an API key actually authenticate an API call (issue #115).
///
/// The keys were issued, scoped, listed and revocable, and until now **nothing accepted them**:
/// every endpoint reads the auth cookie, so a `write` key could not create an upload and a
/// `read` key could not read anything. A credential the product hands out and then ignores is
/// worse than one it does not offer — the scopes on the screen described permissions that were
/// never consulted.
///
/// Written as middleware rather than an authentication scheme so that every existing endpoint
/// keeps working untouched: the principal it builds carries the same provider claims a cookie
/// does, so `GetUserOrDefaultAsync` resolves the same account by the same route.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware(RequestDelegate next)
{
    public const string AuthenticationType = "ApiKey";
    /// <summary>Claim marking a principal as machine-authenticated, for anything that cares.</summary>
    public const string KeyIdClaimType = "api_key_id";

    private const string ApiPrefix = "/api/";

    public async Task InvokeAsync(HttpContext context, ApiKeyAuthenticator keys, AppDbContext db)
    {
        // A cookie wins: a browser session that also happens to carry a key is still a person,
        // and downgrading it to the key's scopes would be a surprise in the wrong direction.
        if (context.User.Identity?.IsAuthenticated == true
            || !context.Request.Path.StartsWithSegments(ApiPrefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var key = await keys.ResolveAsync(context, context.RequestAborted);
        if (key is null)
        {
            await next(context);
            return;
        }

        // Coarse, matching how the scopes are described: reading, writing, destroying. A
        // per-endpoint permission table would look thorough and drift the first time somebody
        // adds a route without remembering it exists.
        var required = RequiredScope(context.Request.Method);
        if (!ApiScopes.Grants(key.Scopes, required))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                new { error = ErrorCodes.ApiKeyScopeRequired, required }, context.RequestAborted);
            return;
        }

        var login = await db.UserLogins
            .AsNoTracking()
            .Where(l => l.UserId == key.UserId)
            .OrderBy(l => l.Id)
            .FirstOrDefaultAsync(context.RequestAborted);

        // An account with no login row cannot be resolved by anything downstream, so the key
        // authenticates nobody rather than half of somebody.
        if (login is null)
        {
            await next(context);
            return;
        }

        context.User = Build(login, key);
        await next(context);
    }

    /// <summary>
    /// The same claims a cookie carries, plus the key's id.
    ///
    /// It is marked second-factor-verified on purpose: a key *is* an independent credential, it
    /// cannot be phished out of a session, and it is revocable in one click — where a machine
    /// caller has no way at all to answer a TOTP prompt. Without this an account that enrolled
    /// an authenticator would find its integrations silently dead.
    /// </summary>
    private static ClaimsPrincipal Build(UserLogin login, ApiKey key)
    {
        var identity = new ClaimsIdentity(AuthenticationType);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, login.ProviderKey));
        identity.AddClaim(new Claim(AuthConstants.ProviderClaimType, login.Provider));
        identity.AddClaim(new Claim(KeyIdClaimType, key.Id.ToString()));
        identity.AddClaim(new Claim(MfaSession.VerifiedClaimType, bool.TrueString));

        return new ClaimsPrincipal(identity);
    }

    private static string RequiredScope(string method) => method switch
    {
        _ when HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) => ApiScopes.Read,
        _ when HttpMethods.IsDelete(method) => ApiScopes.Manage,
        _ => ApiScopes.Write,
    };
}
