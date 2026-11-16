using QuickShare.Extensions;
using QuickShare.Features.Auth.Totp;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Email;
using QuickShare.Infrastructure.Push;

namespace QuickShare.Features.Auth.Passkeys;

/// <summary>GET /api/account/passkeys/recovery-codes — check remaining passkey recovery codes (issue #118).</summary>
public sealed class GetPasskeyRecoveryStatusEndpoint : EndpointWithoutRequest<PasskeyRecoveryStatusDto>
{
    public override void Configure()
    {
        Get(RoutePaths.AccountPasskeyRecoveryCodesApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Get passkey recovery codes count and re-add status");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var row = await db.UserPasskeyRecoveries
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == user.Id, ct);

        if (row is null)
        {
            await Send.OkAsync(new PasskeyRecoveryStatusDto(0, false, null), ct);
            return;
        }

        var count = TotpService.CountRecoveryCodes(row.RecoveryCodeHashes);
        await Send.OkAsync(new PasskeyRecoveryStatusDto(count, row.RequirePasskeyReAdd, row.LastUsedAt), ct);
    }
}

/// <summary>POST /api/account/passkeys/recovery-codes — generate or rotate 10 one-time passkey recovery codes (issue #118).</summary>
public sealed class GeneratePasskeyRecoveryCodesEndpoint : EndpointWithoutRequest<PasskeyRecoveryGenerateDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AccountPasskeyRecoveryCodesApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Generate 10 one-time passkey recovery codes (rotates any existing set)");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var (codes, hashesJson) = TotpService.GenerateRecoveryCodes();
        var existing = await db.UserPasskeyRecoveries.FirstOrDefaultAsync(r => r.UserId == user.Id, ct);
        if (existing is null)
        {
            db.UserPasskeyRecoveries.Add(new UserPasskeyRecovery
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                RecoveryCodeHashes = hashesJson,
            });
        }
        else
        {
            existing.RecoveryCodeHashes = hashesJson;
        }

        await db.SaveChangesAsync(ct);

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.PasskeyRecoveryGenerated,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await Send.OkAsync(new PasskeyRecoveryGenerateDto(codes, codes.Count), ct);
    }
}

/// <summary>POST /api/auth/passkey/recovery — sign in using a one-time passkey recovery code (issue #118).</summary>
public sealed class PasskeyRecoveryLoginEndpoint : Endpoint<PasskeyRecoveryLoginRequest, PasskeyRecoveryLoginDto>
{
    private const string AlertSubject = "Passkey recovery code used on QuickShareCloud";

    public override void Configure()
    {
        Post(RoutePaths.PasskeyRecoveryLoginApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Sign in with a one-time passkey recovery code and require passkey re-enrollment");
    }

    public override async Task HandleAsync(PasskeyRecoveryLoginRequest req, CancellationToken ct)
    {
        var email = req.Email?.Trim().ToLowerInvariant();
        var code = req.Code?.Trim();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
        {
            ThrowError(ErrorCodes.RecoveryCodeInvalid, StatusCodes.Status401Unauthorized);
            return;
        }

        var db = Resolve<AppDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            ThrowError(ErrorCodes.RecoveryCodeInvalid, StatusCodes.Status401Unauthorized);
            return;
        }

        using var lease = await TotpAttemptLimiter.EnterAsync(user.Id, ct);
        if (!TotpAttemptLimiter.TryAcquire(user.Id, out var retryAfter))
        {
            HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ThrowError(ErrorCodes.RecoveryRateLimited, StatusCodes.Status429TooManyRequests);
            return;
        }

        var recovery = await db.UserPasskeyRecoveries.FirstOrDefaultAsync(r => r.UserId == user.Id, ct);
        if (recovery is null || !TotpService.TrySpendRecoveryCode(recovery.RecoveryCodeHashes, code, out var remainingJson))
        {
            ThrowError(ErrorCodes.RecoveryCodeInvalid, StatusCodes.Status401Unauthorized);
            return;
        }

        recovery.RecoveryCodeHashes = remainingJson;
        recovery.RequirePasskeyReAdd = true;
        recovery.LastUsedAt = DateTime.UtcNow;

        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Type = NotificationType.Info,
            Title = AlertSubject,
            LinkUrl = $"{RoutePaths.AccountSecurity}?readd=1",
        });

        await db.SaveChangesAsync(ct);

        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.PasskeyRecoveryUsed,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty, ct);

        await AuthSignIn.CompleteAsync(HttpContext, db, user, AuthConstants.PasskeyRecoveryProvider, ct);

        var emailSender = Resolve<EmailSender>();
        if (emailSender.IsConfigured)
        {
            try
            {
                await emailSender.SendAsync(
                    user.Email,
                    user.Name ?? user.Email,
                    AlertSubject,
                    "<p>A passkey recovery code was used to sign in to your QuickShareCloud account. Please add a new passkey on your security settings page.</p>",
                    ct);
            }
            catch
            {
                // Best-effort alert
            }
        }

        var push = Resolve<IPushService>();
        if (push.IsConfigured)
        {
            var subs = await db.PushSubscriptions
                .AsNoTracking()
                .Where(s => s.UserId == user.Id)
                .ToListAsync(ct);
            await push.SendAsync(subs, AlertSubject, "Passkey recovery code used — please add a new passkey.", RoutePaths.AccountSecurity, ct);
        }

        await Send.OkAsync(new PasskeyRecoveryLoginDto(true, true, $"{RoutePaths.AccountSecurity}?readd=1"), ct);
    }
}
