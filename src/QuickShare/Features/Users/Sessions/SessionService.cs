using System.Security.Claims;
using QuickShare.Features.Users.Shared;
using System.Security.Cryptography;

namespace QuickShare.Features.Users.Sessions;

/// <summary>
/// Issues, validates and revokes sessions (issues #105, #173).
///
/// The rule the whole thing turns on: **a revoked session must stop working on its next request,
/// not when its cookie happens to expire.** Anything that caches the answer trades that away, so
/// validation reads the row every time. One indexed lookup is what a working revoke button costs.
/// </summary>
public sealed class SessionService(AppDbContext db)
{
    /// <summary>Claim carrying the session id. Short, because it rides in every cookie.</summary>
    public const string SessionClaimType = "sid";

    /// <summary>Starts a session and returns the claim to put in the cookie.</summary>
    public async Task<UserSession> StartAsync(Guid userId, HttpContext context, CancellationToken ct)
    {
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            SessionId = GenerateSessionId(),
            UserAgentLabel = DeviceTrackerService.SummarizeUserAgent(context.Request.Headers.UserAgent.ToString()),
            IpHash = DeviceTrackerService.HashIp(context.Connection.RemoteIpAddress?.ToString() ?? string.Empty),
        };

        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return session;
    }

    /// <summary>
    /// Whether this principal's session still counts, touching it if so.
    ///
    /// Returns false for a session that is unknown, revoked or idle past the timeout — all three
    /// mean the same thing to the caller: sign this cookie out.
    ///
    /// A principal with no session claim is **allowed through**. Those are cookies issued before
    /// this feature existed, and rejecting them would sign out every logged-in user on deploy to
    /// enforce a rule none of them had a chance to satisfy. They expire on their own within 30
    /// days, and every new sign-in gets a session.
    /// </summary>
    public async Task<bool> ValidateAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var sessionId = principal.FindFirstValue(SessionClaimType);
        if (string.IsNullOrEmpty(sessionId))
            return true;

        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.SessionId == sessionId, ct);
        if (session is null)
            return false;

        var now = DateTime.UtcNow;
        if (!session.IsActive(now))
            return false;

        // Written at most once every few minutes: on every request this would be a database write
        // per page load for a value nobody reads at that precision.
        if (session.LastSeenAt.Add(UserSession.TouchInterval) < now)
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>Revokes one session of one account. Scoped by user, never by id alone.</summary>
    public async Task<bool> RevokeAsync(Guid userId, Guid sessionRowId, CancellationToken ct)
    {
        var session = await db.UserSessions
            .FirstOrDefaultAsync(s => s.Id == sessionRowId && s.UserId == userId && s.RevokedAt == null, ct);

        if (session is null)
            return false;

        session.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Signs an account out everywhere (issue #173).
    ///
    /// <paramref name="exceptSessionId"/> keeps the caller's own session alive, because "sign out
    /// my other devices" is the thing people actually want — signing yourself out as well turns a
    /// security action into a chore, and a chore is something people skip.
    ///
    /// Passing null revokes everything, which is what offboarding needs (issue #146).
    /// </summary>
    public async Task<int> RevokeAllAsync(Guid userId, string? exceptSessionId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        return await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .Where(s => exceptSessionId == null || s.SessionId != exceptSessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.RevokedAt, now), ct);
    }

    /// <summary>256 bits, base64url. Opaque and unguessable; it is a bearer value in a cookie.</summary>
    private static string GenerateSessionId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
