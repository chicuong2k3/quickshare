using System.Security.Claims;
using Fido2NetLib;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuickShare.Extensions;
using QuickShare.Features.Auth.MagicLink;
using QuickShare.Features.Auth.Totp;
using QuickShare.Features.Users.Sessions;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Auth.Passkeys;

/// <summary>A registered passkey as the security page lists it (issue #68).</summary>
public sealed record PasskeyDto(Guid Id, string? Name, DateTime CreatedAt, DateTime? LastUsedAt);

/// <summary>A ceremony in flight: the handle to send back, and the options to hand the browser.</summary>
public sealed record CeremonyDto(string Handle, object Options);

public sealed class CompleteRegistrationRequest
{
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public AuthenticatorAttestationRawResponse? Response { get; set; }
}

public sealed class CompleteAssertionRequest
{
    public string? Handle { get; set; }
    public AuthenticatorAssertionRawResponse? Response { get; set; }
}

public sealed class MagicLinkRequest
{
    public string? Email { get; set; }
}

/// <summary>GET /api/account/passkeys — this account's registered passkeys (issue #68).</summary>
public sealed class ListPasskeysEndpoint : EndpointWithoutRequest<IReadOnlyList<PasskeyDto>>
{
    public override void Configure()
    {
        Get("/api/account/passkeys");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "List registered passkeys");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var passkeys = await db.UserPasskeys
            .AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PasskeyDto(p.Id, p.Name, p.CreatedAt, p.LastUsedAt))
            .ToListAsync(ct);

        await Send.OkAsync(passkeys, ct);
    }
}

/// <summary>POST /api/account/passkeys/register/begin — start registration (issue #68).</summary>
public sealed class BeginPasskeyRegistrationEndpoint : EndpointWithoutRequest<CeremonyDto>
{
    public override void Configure()
    {
        Post("/api/account/passkeys/register/begin");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Begin passkey registration");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var (handle, options) = await Resolve<PasskeyService>().BeginRegistrationAsync(user, ct);
        await Send.OkAsync(new CeremonyDto(handle, options), ct);
    }
}

/// <summary>POST /api/account/passkeys/register/complete — finish registration (issue #68).</summary>
public sealed class CompletePasskeyRegistrationEndpoint : Endpoint<CompleteRegistrationRequest, PasskeyDto>
{
    public override void Configure()
    {
        Post("/api/account/passkeys/register/complete");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Complete passkey registration");
    }

    public override async Task HandleAsync(CompleteRegistrationRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        if (req.Response is null || string.IsNullOrWhiteSpace(req.Handle))
        {
            ThrowError(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest);
            return;
        }

        UserPasskey? passkey;
        try
        {
            passkey = await Resolve<PasskeyService>()
                .CompleteRegistrationAsync(user, req.Handle, req.Response, req.Name, ct);
        }
        catch (Fido2VerificationException)
        {
            // The attestation did not check out. There is nothing useful to tell the caller
            // beyond that, and the exception message is library internals.
            ThrowError(ErrorCodes.PasskeyVerificationFailed, StatusCodes.Status400BadRequest);
            return;
        }

        if (passkey is null)
        {
            ThrowError(ErrorCodes.PasskeyChallengeExpired, StatusCodes.Status400BadRequest);
            return;
        }

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.PasskeyAdded,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await Send.OkAsync(new PasskeyDto(passkey.Id, passkey.Name, passkey.CreatedAt, null), ct);
    }
}

/// <summary>DELETE /api/account/passkeys/{Id} — remove a passkey (issue #68).</summary>
public sealed class RemovePasskeyEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/account/passkeys/{Id}");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Remove a passkey";
            s.Description = "Removing the last one is allowed — the identity provider that created the account still works.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        if (!await Resolve<PasskeyService>().RemoveAsync(user.Id, Route<Guid>("Id"), ct))
        {
            ThrowError(ErrorCodes.PasskeyNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.PasskeyRemoved,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>POST /api/auth/passkey/begin — start a sign-in ceremony (issue #68).</summary>
public sealed class BeginPasskeyAssertionEndpoint : EndpointWithoutRequest<CeremonyDto>
{
    public override void Configure()
    {
        Post("/api/auth/passkey/begin");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Begin passkey sign-in";
            s.Description = "Usernameless: no address is asked for and no credential ids are returned.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var (handle, options) = await Resolve<PasskeyService>().BeginAssertionAsync(ct);
        await Send.OkAsync(new CeremonyDto(handle, options), ct);
    }
}

/// <summary>
/// POST /api/auth/passkey/complete — finish signing in with a passkey (issue #68).
///
/// Issues the same cookie and the same revocable session as every other sign-in path, because
/// everything downstream reads those and must not care which front door was used.
/// </summary>
public sealed class CompletePasskeyAssertionEndpoint : Endpoint<CompleteAssertionRequest>
{
    public override void Configure()
    {
        Post("/api/auth/passkey/complete");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Complete passkey sign-in");
    }

    public override async Task HandleAsync(CompleteAssertionRequest req, CancellationToken ct)
    {
        if (req.Response is null || string.IsNullOrWhiteSpace(req.Handle))
        {
            ThrowError(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest);
            return;
        }

        var db = Resolve<AppDbContext>();
        var user = await Resolve<PasskeyService>().CompleteAssertionAsync(req.Handle, req.Response, ct);

        if (user is null)
        {
            // One error for every failure. The caller is anonymous, and distinguishing "no such
            // credential" from "bad signature" tells them which credential ids exist.
            ThrowError(ErrorCodes.PasskeyVerificationFailed, StatusCodes.Status401Unauthorized);
            return;
        }

        await AuthSignIn.CompleteAsync(HttpContext, db, user, AuthConstants.PasskeyProvider, ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// POST /api/auth/magic-link — email a sign-in link (issue #132).
///
/// Always 204, whether or not the address has an account. Anything else makes this an
/// account-existence oracle for anybody holding a list of addresses.
/// </summary>
public sealed class RequestMagicLinkEndpoint : Endpoint<MagicLinkRequest>
{
    public override void Configure()
    {
        Post("/api/auth/magic-link");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Email a single-use sign-in link";
            s.Description = "Always returns 204 — the response never reveals whether the address has an account.";
        });
    }

    public override async Task HandleAsync(MagicLinkRequest req, CancellationToken ct)
    {
        await Resolve<MagicLinkService>().RequestAsync(req.Email, ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// Shared sign-in completion for the non-OAuth paths (issues #68, #132).
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

        // A passkey is a possession factor with user verification — it already satisfies what the
        // second-factor gate is asking for, so the session starts verified. A magic link is not:
        // it proves control of an inbox, which is the first factor, not a second one.
        if (provider == AuthConstants.PasskeyProvider)
            identity.AddClaim(new Claim(MfaSession.VerifiedClaimType, "1"));

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.Login,
            context.Request.Headers.UserAgent.ToString(),
            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);
    }
}
