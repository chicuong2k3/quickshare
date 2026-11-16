using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Auth.Totp;

internal enum TotpChallengeResult
{
    Success,
    NotEnrolled,
    InvalidCode,
    RateLimited,
}

internal static class TotpChallenge
{
    public static async Task<TotpChallengeResult> VerifyAsync(
        AppDbContext db, User user, string? suppliedCode, HttpContext context, CancellationToken ct)
    {
        if (!TotpAttemptLimiter.TryAcquire(user.Id, out var retryAfterSeconds))
        {
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            return TotpChallengeResult.RateLimited;
        }

        using var lease = await TotpAttemptLimiter.EnterAsync(user.Id, ct);
        var code = (suppliedCode ?? string.Empty).Trim();
        var totp = await db.UserTotps.FirstOrDefaultAsync(t => t.UserId == user.Id, ct);
        if (totp is null || !totp.IsConfirmed)
        {
            return TotpChallengeResult.NotEnrolled;
        }

        var userAgent = context.Request.Headers.UserAgent.ToString();
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        if (TotpService.TryVerify(totp.Secret, code, DateTime.UtcNow, totp.LastUsedStep, out var step))
        {
            totp.LastUsedStep = step;
        }
        else if (TotpService.TrySpendRecoveryCode(totp.RecoveryCodeHashes, code, out var remaining))
        {
            totp.RecoveryCodeHashes = remaining;
            await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.RecoveryUsed, userAgent, ip, ct);
        }
        else
        {
            await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.LoginFailed, userAgent, ip, ct);
            await db.SaveChangesAsync(ct);
            return TotpChallengeResult.InvalidCode;
        }

        await db.SaveChangesAsync(ct);
        await MfaSession.MarkVerifiedAsync(context);
        return TotpChallengeResult.Success;
    }
}
