using System.Security.Claims;
using QuickShare.Extensions;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Users.Sessions;

/// <summary>A signed-in session, as the account screen lists it (issue #105).</summary>
public sealed record SessionDto(
    Guid Id, string UserAgentLabel, DateTime CreatedAt, DateTime LastSeenAt, bool IsCurrent);

/// <summary>GET /api/account/sessions — where this account is signed in (issue #105).</summary>
public sealed class ListSessionsEndpoint : EndpointWithoutRequest<IReadOnlyList<SessionDto>>
{
    public override void Configure()
    {
        Get("/api/account/sessions");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "List active sessions";
            s.Description = "Newest first. The caller's own session is flagged so it is not revoked by accident.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        var current = User.FindFirstValue(SessionService.SessionClaimType);
        var cutoff = DateTime.UtcNow - UserSession.IdleTimeout;

        var sessions = await db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id && s.RevokedAt == null && s.LastSeenAt > cutoff)
            .OrderByDescending(s => s.LastSeenAt)
            .Select(s => new SessionDto(s.Id, s.UserAgentLabel, s.CreatedAt, s.LastSeenAt, s.SessionId == current))
            .ToListAsync(ct);

        await Send.OkAsync(sessions, ct);
    }
}

/// <summary>DELETE /api/account/sessions/{Id} — sign one device out (issue #105).</summary>
public sealed class RevokeSessionEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/account/sessions/{Id}");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Revoke one session");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        if (!await Resolve<SessionService>().RevokeAsync(user.Id, Route<Guid>("Id"), ct))
        {
            ThrowError(ErrorCodes.SessionNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.SessionRevoked,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// POST /api/account/sessions/revoke-all — sign out everywhere else (issue #173).
///
/// Keeps the caller's own session. "Sign out my other devices" is what people actually want, and
/// signing them out too turns a security action into a chore — which is something people skip.
/// </summary>
public sealed class RevokeAllSessionsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/account/sessions/revoke-all");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Sign out of every other session";
            s.Description = "The current session stays signed in. Revoked sessions stop working on their next request.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        var current = User.FindFirstValue(SessionService.SessionClaimType);

        var revoked = await Resolve<SessionService>().RevokeAllAsync(user.Id, current, ct);

        if (revoked > 0)
        {
            await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.SessionRevoked,
                HttpContext.Request.Headers.UserAgent.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct,
                System.Text.Json.JsonSerializer.Serialize(new { revoked }));
        }

        await Send.NoContentAsync(ct);
    }
}
