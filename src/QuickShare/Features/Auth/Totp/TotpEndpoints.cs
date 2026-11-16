using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using QuickShare.Extensions;
using QuickShare.Features.Auth.Mfa;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Middleware;

namespace QuickShare.Features.Auth.Totp;

/// <summary>The enrolment secret. Returned exactly once, at start — never readable afterwards.</summary>
public sealed record TotpEnrolmentDto(string Secret, string OtpAuthUri);

/// <summary>Enrolment state for the account screen. Carries no secret.</summary>
public sealed record TotpStatusDto(bool Enrolled, bool Confirmed, DateTime? ConfirmedAt, int RecoveryCodesLeft);

/// <summary>Recovery codes. Shown once at confirmation, then only ever as a count.</summary>
public sealed record TotpRecoveryDto(IReadOnlyList<string> Codes);

public sealed class TotpCodeRequest
{
    public string? Code { get; set; }
}

/// <summary>GET /api/account/totp — whether this account has an authenticator (issue #128).</summary>
public sealed class GetTotpStatusEndpoint : EndpointWithoutRequest<TotpStatusDto>
{
    public override void Configure()
    {
        Get("/api/account/totp");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Authenticator enrolment status for the signed-in account");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var totp = await db.UserTotps.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == user.Id, ct);

        await Send.OkAsync(totp is null
            ? new TotpStatusDto(false, false, null, 0)
            : new TotpStatusDto(true, totp.IsConfirmed, totp.ConfirmedAt,
                TotpService.CountRecoveryCodes(totp.RecoveryCodeHashes)),
            ct);
    }
}

/// <summary>
/// POST /api/account/totp/start — mint a secret and hand back the otpauth URI (issue #128).
///
/// Starting again before confirming replaces the pending secret, because the usual reason to
/// press it twice is that the first QR code never made it into the app. Starting again after
/// confirming is refused: that path is disable-then-enrol, so a stolen session cannot quietly
/// swap the second factor for one it controls.
/// </summary>
public sealed class StartTotpEndpoint : EndpointWithoutRequest<TotpEnrolmentDto>
{
    private const string Issuer = "QuickShareCloud";

    public override void Configure()
    {
        Post("/api/account/totp/start");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Begin authenticator enrolment";
            s.Description = "Returns a fresh secret and its otpauth URI. The secret is never readable again.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        using var lease = await TotpAttemptLimiter.EnterAsync(user.Id, ct);
        var existing = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == user.Id, ct);
        if (existing is not null && existing.IsConfirmed)
        {
            ThrowError(ErrorCodes.TotpAlreadyEnrolled, StatusCodes.Status409Conflict);
            return;
        }

        var secret = TotpService.GenerateSecret();

        if (existing is null)
        {
            db.UserTotps.Add(new UserTotp { UserId = user.Id, Secret = secret });
        }
        else
        {
            existing.Secret = secret;
            existing.LastUsedStep = 0;
        }

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new TotpEnrolmentDto(secret, TotpService.BuildUri(secret, user.Email, Issuer)), ct);
    }
}

/// <summary>
/// POST /api/account/totp/confirm — prove the app holds the secret, then enable it (issue #128).
///
/// Enrolment is not complete until a code verifies. Enabling on the strength of "we showed them
/// a QR code" is how an account ends up with a second factor nobody can produce.
/// </summary>
public sealed class ConfirmTotpEndpoint : Endpoint<TotpCodeRequest, TotpRecoveryDto>
{
    public override void Configure()
    {
        Post("/api/account/totp/confirm");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Confirm authenticator enrolment with a code";
            s.Description = "Returns the recovery codes. They are shown once and stored only as hashes.";
        });
    }

    public override async Task HandleAsync(TotpCodeRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        if (!TotpAttemptLimiter.TryAcquire(user.Id, out var retryAfterSeconds))
        {
            HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            ThrowError(ErrorCodes.MfaRateLimited, StatusCodes.Status429TooManyRequests);
            return;
        }

        using var lease = await TotpAttemptLimiter.EnterAsync(user.Id, ct);
        var totp = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == user.Id, ct);
        if (totp is null)
        {
            ThrowError(ErrorCodes.TotpNotEnrolled, StatusCodes.Status404NotFound);
            return;
        }

        if (totp.IsConfirmed)
        {
            ThrowError(ErrorCodes.TotpAlreadyEnrolled, StatusCodes.Status409Conflict);
            return;
        }

        if (!TotpService.TryVerify(totp.Secret, req.Code ?? string.Empty, DateTime.UtcNow, totp.LastUsedStep, out var step))
        {
            ThrowError(ErrorCodes.TotpCodeInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var (codes, hashes) = TotpService.GenerateRecoveryCodes();
        totp.ConfirmedAt = DateTime.UtcNow;
        totp.LastUsedStep = step;
        totp.RecoveryCodeHashes = hashes;

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.TotpEnrolled,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await db.SaveChangesAsync(ct);
        await StaticCache.Instance.RemoveAsync(MfaEnforcementMiddleware.EnrolmentCacheKey(user.Id), token: ct);

        // The session that just enrolled is verified — otherwise ticking the box would bounce
        // the enroller straight into the challenge they have already answered.
        await MfaSession.MarkVerifiedAsync(HttpContext);

        await Send.OkAsync(new TotpRecoveryDto(codes), ct);
    }
}

/// <summary>
/// POST /api/account/totp/verify — answer the sign-in challenge (issue #128).
///
/// Accepts a live code or one recovery code. Success re-issues the auth cookie carrying the
/// verified claim; nothing else in the pipeline treats the session as second-factored until it
/// does. Recovery codes are spent on use.
/// </summary>
public sealed class VerifyTotpEndpoint : Endpoint<TotpCodeRequest>
{
    public override void Configure()
    {
        Post("/api/account/totp/verify");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Answer the second-factor challenge";
            s.Description = "Accepts a six-digit code or a recovery code. Recovery codes are single-use.";
        });
    }

    public override async Task HandleAsync(TotpCodeRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        var result = await TotpChallenge.VerifyAsync(db, user, req.Code, HttpContext, ct);
        if (result == TotpChallengeResult.RateLimited)
        {
            ThrowError(ErrorCodes.MfaRateLimited, StatusCodes.Status429TooManyRequests);
            return;
        }

        if (result == TotpChallengeResult.NotEnrolled)
        {
            ThrowError(ErrorCodes.TotpNotEnrolled, StatusCodes.Status404NotFound);
            return;
        }

        if (result == TotpChallengeResult.InvalidCode)
        {
            ThrowError(ErrorCodes.TotpCodeInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// DELETE /api/account/totp — remove the authenticator (issue #128).
///
/// Requires a current code, for the same reason the bank asks before changing your address: the
/// point of a second factor is that a stolen session cannot remove it. Refused outright while a
/// workspace policy requires one (issue #219) — otherwise the policy is a suggestion.
/// </summary>
public sealed class DisableTotpEndpoint : Endpoint<TotpCodeRequest>
{
    public override void Configure()
    {
        Delete("/api/account/totp");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s =>
        {
            s.Summary = "Remove the authenticator";
            s.Description = "Requires a current code or recovery code. Refused while a workspace MFA policy applies.";
        });
    }

    public override async Task HandleAsync(TotpCodeRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        if (!TotpAttemptLimiter.TryAcquire(user.Id, out var retryAfterSeconds))
        {
            HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            ThrowError(ErrorCodes.MfaRateLimited, StatusCodes.Status429TooManyRequests);
            return;
        }

        using var lease = await TotpAttemptLimiter.EnterAsync(user.Id, ct);
        var totp = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == user.Id, ct);
        if (totp is null)
        {
            ThrowError(ErrorCodes.TotpNotEnrolled, StatusCodes.Status404NotFound);
            return;
        }

        var policy = await Resolve<MfaPolicyService>().EvaluateAsync(user.Id, ct);
        if (policy.Required && !policy.SsoExempt)
        {
            ThrowError(ErrorCodes.MfaPolicyLocked, StatusCodes.Status409Conflict);
            return;
        }

        var code = (req.Code ?? string.Empty).Trim();
        var verified = TotpService.TryVerify(totp.Secret, code, DateTime.UtcNow, totp.LastUsedStep, out _)
            || TotpService.TrySpendRecoveryCode(totp.RecoveryCodeHashes, code, out _);

        if (!verified)
        {
            ThrowError(ErrorCodes.TotpCodeInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        db.UserTotps.Remove(totp);
        await StaticCache.Instance.RemoveAsync(MfaEnforcementMiddleware.EnrolmentCacheKey(user.Id), token: ct);

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.TotpRemoved,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// Marks the current auth cookie as second-factored (issue #128).
///
/// The claim rides in the cookie rather than in server state because the cookie is already the
/// session: a separate store would be a second thing to expire, and signing out would have to
/// remember to clear it.
/// </summary>
public static class MfaSession
{
    public const string VerifiedClaimType = "mfa_verified";

    public static bool IsVerified(ClaimsPrincipal principal) =>
        principal.HasClaim(c => c.Type == VerifiedClaimType);

    /// <summary>Re-issues the cookie with the verified claim added.</summary>
    public static async Task MarkVerifiedAsync(HttpContext context)
    {
        if (context.User.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            return;

        if (IsVerified(context.User))
            return;

        identity.AddClaim(new Claim(VerifiedClaimType, "1"));
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}
