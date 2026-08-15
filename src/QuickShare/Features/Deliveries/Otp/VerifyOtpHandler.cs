using LightResults;

namespace QuickShare.Features.Deliveries.Otp;

public record VerifyOtpCommand(string ShareCode, string Otp);

public record VerifyOtpResponse(bool Valid);

public sealed class VerifyOtpHandler(AnalyticsService analytics, OtpLockoutService lockout)
{
    public async Task<Result<VerifyOtpResponse>> Handle(VerifyOtpCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.ShareCode, ct);

        if (delivery is null)
        {
            await analytics.RecordAsync(AnalyticsEvents.OtpFail, ct: ct);
            return Result.Failure<VerifyOtpResponse>(new LightResults.Error("Delivery not found."));
        }

        if (delivery.OtpHash is null)
        {
            await lockout.ResetAsync(db, delivery.Id, ct);
            await analytics.RecordAsync(AnalyticsEvents.OtpPass, ct: ct);
            return Result.Success(new VerifyOtpResponse(true));
        }

        var valid = BCrypt.Net.BCrypt.Verify(cmd.Otp, delivery.OtpHash);

        if (valid)
        {
            await lockout.ResetAsync(db, delivery.Id, ct);
            await analytics.RecordAsync(AnalyticsEvents.OtpPass, ct: ct);
            return Result.Success(new VerifyOtpResponse(true));
        }

        await lockout.RecordFailureAsync(db, delivery.Id, ct);
        await analytics.RecordAsync(AnalyticsEvents.OtpFail, ct: ct);
        return Result.Failure<VerifyOtpResponse>(new LightResults.Error("Wrong code."));
    }
}
