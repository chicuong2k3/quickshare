using LightResults;

namespace QuickShare.Features.Deliveries.Otp;

public record VerifyOtpCommand(string ShareCode, string Otp) : IRequest<Result<VerifyOtpResponse>>;

public record VerifyOtpResponse(bool Valid);

public sealed class VerifyOtpHandler(AnalyticsService analytics, OtpLockoutService lockout, AppDbContext db)
    : IRequestHandler<VerifyOtpCommand, Result<VerifyOtpResponse>>
{
    public async ValueTask<Result<VerifyOtpResponse>> Handle(VerifyOtpCommand cmd, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(cmd.ShareCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null)
        {
            await analytics.RecordAsync(AnalyticsEvents.OtpFail, ct: cancellationToken);
            return Result.Failure<VerifyOtpResponse>(new LightResults.Error("Delivery not found."));
        }

        if (delivery.OtpHash is null)
        {
            await lockout.ResetAsync(db, delivery.Id, cancellationToken);
            await analytics.RecordAsync(AnalyticsEvents.OtpPass, ct: cancellationToken);
            return Result.Success(new VerifyOtpResponse(true));
        }

        var valid = BCrypt.Net.BCrypt.Verify(cmd.Otp, delivery.OtpHash);

        if (valid)
        {
            await lockout.ResetAsync(db, delivery.Id, cancellationToken);
            await analytics.RecordAsync(AnalyticsEvents.OtpPass, ct: cancellationToken);
            return Result.Success(new VerifyOtpResponse(true));
        }

        await lockout.RecordFailureAsync(db, delivery.Id, cancellationToken);
        await analytics.RecordAsync(AnalyticsEvents.OtpFail, ct: cancellationToken);
        return Result.Failure<VerifyOtpResponse>(new LightResults.Error("Wrong code."));
    }
}
