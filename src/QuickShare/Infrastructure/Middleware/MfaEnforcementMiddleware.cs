using QuickShare.Extensions;
using QuickShare.Features.Auth.Mfa;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;
using QuickShare.Features.Auth.Totp;

namespace QuickShare.Infrastructure.Middleware;

/// <summary>
/// Holds a session out of the product until its second factor is answered (issues #128, #219).
///
/// Two separate reasons to challenge, and they are not the same thing:
///
/// 1. **The account has an authenticator.** Then every session must answer it. An enrolled
///    account whose sessions sail past the challenge has a decoration, not a second factor.
/// 2. **A workspace policy requires one and the account has not enrolled** (issue #219). Inside
///    the grace window the request goes through and the page shows a nudge; past it the account
///    is held out, with the enrolment route still open so "blocked" never means "stuck".
///
/// The route allowlist is the part worth being careful about: it must contain everything needed
/// to *satisfy* the challenge and nothing else. Get it wrong in one direction and the product is
/// open; in the other, the only way to enrol is behind the wall that enrolment would remove.
/// </summary>
public sealed class MfaEnforcementMiddleware(RequestDelegate next)
{
    /// <summary>Header a blocked API caller can read to tell this apart from a plain 403.</summary>
    public const string ChallengeHeader = "X-QuickShare-Mfa";

    /// <summary>Paths that must stay reachable: answering, enrolling, leaving, and the assets to do it.</summary>
    private static readonly string[] AllowedPrefixes =
    [
        RoutePaths.MfaChallenge,
        "/api/account/totp",
        RoutePaths.Logout,
        RoutePaths.ScopedStyles,
        "/login",
        "/signin-",
        "/css",
        "/js",
        "/lib",
        "/health",
        "/antiforgery",
        "/_framework",
        "/_content",
    ];

    /// <summary>
    /// Enrolment is cached briefly: this runs on every authenticated request, and without it
    /// the check would be a database round trip per page load for the majority of accounts that
    /// have no authenticator at all. A minute of staleness costs nothing — the session that just
    /// enrolled is marked verified in the same call, so it never sees the stale answer.
    /// </summary>
    private static readonly TimeSpan EnrolmentCacheTtl = TimeSpan.FromMinutes(1);

    public async Task InvokeAsync(HttpContext context, MfaPolicyService policies, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true || IsAllowed(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (MfaSession.IsVerified(context.User))
        {
            await next(context);
            return;
        }

        var user = await context.User.GetUserOrDefaultAsync(db, context.RequestAborted);
        if (user is null)
        {
            await next(context);
            return;
        }

        var enrolled = await StaticCache.Instance.GetOrSetAsync(
            EnrolmentCacheKey(user.Id),
            ct => db.UserTotps.AsNoTracking().AnyAsync(t => t.UserId == user.Id && t.ConfirmedAt != null, ct),
            duration: EnrolmentCacheTtl,
            token: context.RequestAborted);

        if (enrolled)
        {
            await ChallengeAsync(context, ErrorCodes.MfaRequired);
            return;
        }

        var verdict = await policies.EvaluateAsync(user.Id, context.RequestAborted);
        if (!verdict.Blocked)
        {
            await next(context);
            return;
        }

        // Recorded once per block so an admin can see the policy biting rather than fielding a
        // "the site stopped working" ticket with nothing behind it.
        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.MfaBlocked,
            context.Request.Headers.UserAgent.ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, context.RequestAborted);
        await db.SaveChangesAsync(context.RequestAborted);

        await ChallengeAsync(context, ErrorCodes.MfaRequired);
    }

    /// <summary>Cache key for the enrolment flag. Public so enrolment can drop it immediately.</summary>
    public static string EnrolmentCacheKey(Guid userId) => $"mfa:enrolled:{userId}";

    private static bool IsAllowed(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value) || value == "/")
            return true;

        foreach (var prefix in AllowedPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>API callers get a code they can act on; a browser gets sent to the challenge.</summary>
    private static Task ChallengeAsync(HttpContext context, string code)
    {
        context.Response.Headers[ChallengeHeader] = code;

        if (context.Request.Path.StartsWithSegments(RoutePaths.Api))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(RoutePaths.MfaChallenge);
        return Task.CompletedTask;
    }
}
