using System.Globalization;
using Microsoft.Extensions.Options;

namespace QuickShare.Features.Deliveries.Otp;

public sealed class VerifyOtpEndpoint : Endpoint<VerifyOtpRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/verify-otp");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Deliveries));
    }

    public override async Task HandleAsync(VerifyOtpRequest req, CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;

        var db = Resolve<AppDbContext>();
        var lockout = Resolve<OtpLockoutService>();

        var delivery = await db.Deliveries
            .AsNoTracking()
            .Select(d => new { d.Id, d.ShareCode })
            .FirstOrDefaultAsync(d => d.ShareCode == shareCode, ct);

        // Unknown deliveries behave exactly like locked ones — no user enumeration.
        var deliveryId = delivery?.Id ?? Guid.Empty;
        var isLocked = delivery is not null && await lockout.IsLockedAsync(db, deliveryId, ct);

        if (isLocked)
        {
            await WriteLockedAsync(ct);
            return;
        }

        var turnstile = Resolve<TurnstileService>();
        var turnstileOptions = Resolve<IOptions<TurnstileOptions>>().Value;

        // Anti-bot (issue #175): after N failed attempts, require a Turnstile
        // token before allowing another try. Off when Turnstile is unconfigured.
        if (delivery is not null
            && turnstile.IsConfigured
            && await RequiresChallengeAsync(db, deliveryId, turnstileOptions.ChallengeAfterFailures, ct)
            && !await turnstile.VerifyAsync(req.CaptchaToken ?? string.Empty, HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
        {
            var body = $"{{\"error\":\"captcha_required\",\"message\":\"Please complete the security check.\",\"siteKey\":\"{turnstileOptions.SiteKey}\"}}";
            await Send.StringAsync(body, StatusCodes.Status428PreconditionRequired, MediaTypes.Json, ct);
            return;
        }
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<VerifyOtpResponse>>(
            new VerifyOtpCommand(shareCode, req.Otp), ct);

        if (result.IsSuccess(out var v))
            await Send.OkAsync(v, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 400);
    }

    private static async Task<bool> RequiresChallengeAsync(AppDbContext db, Guid deliveryId, int threshold, CancellationToken ct)
    {
        var lockout = await db.OtpLockouts
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.DeliveryId == deliveryId, ct);

        return lockout?.FailedAttempts >= threshold;
    }

    private async Task WriteLockedAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var lockout = Resolve<OtpLockoutService>();
        var shareCode = Route<string>("ShareCode")!;
        var deliveryId = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.ShareCode == shareCode)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);

        var retryAfter = deliveryId is null
            ? 0
            : await lockout.RetryAfterSecondsAsync(db, deliveryId.Value, ct);

        HttpContext.Response.Headers[HttpHeaders.RetryAfter] = retryAfter.ToString(CultureInfo.InvariantCulture);

        var body = $"{{\"error\":\"delivery_locked\",\"message\":\"Too many attempts. Try again later.\",\"retryAfter\":{retryAfter}}}";
        await Send.StringAsync(body, StatusCodes.Status423Locked, MediaTypes.Json, ct);
    }
}

public class VerifyOtpRequest
{
    public string Otp { get; set; } = string.Empty;
    public string? CaptchaToken { get; set; }
}
