using LightResults;

namespace QuickShare.Features.Deliveries.Otp;

public record VerifyOtpCommand(string ShareCode, string Otp);

public record VerifyOtpResponse(bool Valid);

public sealed class VerifyOtpHandler
{
    public async Task<Result<VerifyOtpResponse>> Handle(VerifyOtpCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.ShareCode, ct);

        if (delivery is null || delivery.OtpHash is null)
            return Result.Success(new VerifyOtpResponse(true));

        return BCrypt.Net.BCrypt.Verify(cmd.Otp, delivery.OtpHash)
            ? Result.Success(new VerifyOtpResponse(true))
            : Result.Failure<VerifyOtpResponse>(new LightResults.Error("Wrong code."));
    }
}
