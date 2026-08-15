using LightResults;

namespace QuickShare.Features.Deliveries.Otp;

public record VerifyOtpCommand(string ShareCode, string Otp);

public record VerifyOtpResponse(bool Valid);

public sealed class VerifyOtpHandler(AnalyticsService analytics)
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
            await analytics.RecordAsync(AnalyticsEvents.OtpPass, ct: ct);
            return Result.Success(new VerifyOtpResponse(true));
        }

        var valid = BCrypt.Net.BCrypt.Verify(cmd.Otp, delivery.OtpHash);
        await analytics.RecordAsync(valid ? AnalyticsEvents.OtpPass : AnalyticsEvents.OtpFail, ct: ct);
        return valid
            ? Result.Success(new VerifyOtpResponse(true))
            : Result.Failure<VerifyOtpResponse>(new LightResults.Error("Wrong code."));
    }
}
