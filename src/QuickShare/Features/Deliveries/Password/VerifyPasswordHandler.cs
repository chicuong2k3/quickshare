using LightResults;
using QuickShare.Features.Deliveries.Otp;

namespace QuickShare.Features.Deliveries.Password;

public record VerifyPasswordCommand(string ShareCode, string Password);

public record VerifyPasswordResponse(bool Valid);

/// <summary>
/// Verifies a sender-set share password (issue #44). Shares the per-delivery
/// lockout counter with the OTP gate — a delivery has one verification gate, so
/// password and OTP failures both count toward the same escalating lockout.
/// </summary>
public sealed class VerifyPasswordHandler(OtpLockoutService lockout)
{
    public async Task<Result<VerifyPasswordResponse>> Handle(VerifyPasswordCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.ShareCode, ct);

        if (delivery is null)
            return Result.Failure<VerifyPasswordResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

        // No password set — nothing to verify, the gate is already open.
        if (delivery.PasswordHash is null)
            return Result.Success(new VerifyPasswordResponse(true));

        var valid = BCrypt.Net.BCrypt.Verify(cmd.Password, delivery.PasswordHash);

        if (valid)
        {
            await lockout.ResetAsync(db, delivery.Id, ct);
            return Result.Success(new VerifyPasswordResponse(true));
        }

        await lockout.RecordFailureAsync(db, delivery.Id, ct);
        return Result.Failure<VerifyPasswordResponse>(new LightResults.Error("Wrong password."));
    }
}
