using LightResults;
using QuickShare.Features.Deliveries.Otp;

namespace QuickShare.Features.Deliveries.Password;

public record VerifyPasswordCommand(string ShareCode, string Password) : IRequest<Result<VerifyPasswordResponse>>;

public record VerifyPasswordResponse(bool Valid);

/// <summary>
/// Verifies a sender-set share password (issue #44). Shares the per-delivery
/// lockout counter with the OTP gate — a delivery has one verification gate, so
/// password and OTP failures both count toward the same escalating lockout.
/// </summary>
public sealed class VerifyPasswordHandler(OtpLockoutService lockout, AppDbContext db)
    : IRequestHandler<VerifyPasswordCommand, Result<VerifyPasswordResponse>>
{
    public async ValueTask<Result<VerifyPasswordResponse>> Handle(VerifyPasswordCommand cmd, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(cmd.ShareCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null)
            return Result.Failure<VerifyPasswordResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

        // No password set — nothing to verify, the gate is already open.
        if (delivery.PasswordHash is null)
            return Result.Success(new VerifyPasswordResponse(true));

        var valid = BCrypt.Net.BCrypt.Verify(cmd.Password, delivery.PasswordHash);

        if (valid)
        {
            if (string.IsNullOrEmpty(delivery.OtpHash) && !delivery.EmailOtpEnabled)
            {
                await lockout.ResetAsync(db, delivery.Id, cancellationToken);
            }
            return Result.Success(new VerifyPasswordResponse(true));
        }

        await lockout.RecordFailureAsync(db, delivery.Id, cancellationToken);
        return Result.Failure<VerifyPasswordResponse>(new LightResults.Error("Wrong password."));
    }
}
