using System.Globalization;
using QuickShare.Features.Deliveries.Otp;

namespace QuickShare.Features.Deliveries.Password;

public sealed class VerifyPasswordEndpoint : Endpoint<VerifyPasswordRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/verify-password");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Deliveries));
    }

    public override async Task HandleAsync(VerifyPasswordRequest req, CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;

        var db = Resolve<AppDbContext>();
        var lockout = Resolve<OtpLockoutService>();

        var deliveryId = await db.Deliveries
            .AsNoTracking()
            .ByCode(shareCode)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);

        // Unknown deliveries behave exactly like locked ones — no enumeration.
        if (deliveryId is { } id && await lockout.IsLockedAsync(db, id, ct))
        {
            await WriteLockedAsync(ct);
            return;
        }

        var result = await Resolve<IMediator>().Send<LightResults.Result<VerifyPasswordResponse>>(
            new VerifyPasswordCommand(shareCode, req.Password), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 400);
    }

    private async Task WriteLockedAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var lockout = Resolve<OtpLockoutService>();
        var shareCode = Route<string>("ShareCode")!;

        var deliveryId = await db.Deliveries
            .AsNoTracking()
            .ByCode(shareCode)
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

public class VerifyPasswordRequest
{
    public string Password { get; set; } = string.Empty;
}
