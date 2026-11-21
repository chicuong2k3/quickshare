using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Users.Sessions;

/// <summary>
/// Issues a cookie backed by a server-side revocable session and records the sign-in event.
///
/// One place, so a new sign-in method cannot forget the session — which would silently issue a
/// cookie nothing can revoke.
/// </summary>
public static class AuthSignIn
{
    public static async Task CompleteAsync(
        HttpContext context, AppDbContext db, User user, string provider, CancellationToken ct)
    {
        var login = await db.UserLogins
            .AsNoTracking()
            .Where(l => l.UserId == user.Id)
            .OrderBy(l => l.Provider)
            .FirstOrDefaultAsync(ct);

        // Reuses the account's existing provider identity rather than inventing a second one:
        // GetUserAsync resolves by (provider, providerKey), and a new pair would look like a
        // different account to everything downstream.
        var subject = login?.ProviderKey ?? user.Id.ToString();
        var subjectProvider = login?.Provider ?? provider;

        var session = await context.RequestServices
            .GetRequiredService<SessionService>()
            .StartAsync(user.Id, context, ct);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, subject),
            new Claim(AuthConstants.ProviderClaimType, subjectProvider),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(SessionService.SessionClaimType, session.SessionId),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.Login,
            context.Request.Headers.UserAgent.ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);
    }
}
