using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Downloads.EmailOtp;

public record RequestEmailOtpCommand(string Code, string Email) : IRequest<Result<RequestEmailOtpResponse>>;

public record RequestEmailOtpResponse(bool Sent);

/// <summary>
/// Emails a single-use code to unlock a delivery (issue #291). The code is BCrypt-hashed
/// (never stored plaintext), TTL 15 minutes, with a 60s resend cooldown.
/// </summary>
public sealed class RequestEmailOtpHandler(
    AppDbContext db,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<RequestEmailOtpHandler> logger)
    : IRequestHandler<RequestEmailOtpCommand, Result<RequestEmailOtpResponse>>
{
    private const int TtlMinutes = 15;
    private const int CooldownSeconds = 60;
    private const string Subject = "Mã xác nhận tải file QuickShare";

    private readonly EmailOptions _options = emailOptions.Value;

    public async ValueTask<Result<RequestEmailOtpResponse>> Handle(
        RequestEmailOtpCommand cmd, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.Code || d.Alias == cmd.Code, cancellationToken);

        if (delivery is null || delivery.DeletedAt is not null)
            return Result.Failure<RequestEmailOtpResponse>(new LightResults.Error(ErrorCodes.NotFound));

        if (!delivery.EmailOtpEnabled)
            return Result.Failure<RequestEmailOtpResponse>(new LightResults.Error(ErrorCodes.DeliveryGated));

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
            return Result.Failure<RequestEmailOtpResponse>(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<RequestEmailOtpResponse>(new LightResults.Error(ErrorCodes.DeliveryExpired));

        var now = DateTime.UtcNow;
        if (delivery.EmailOtpLastSentAt is { } lastSent && now - lastSent < TimeSpan.FromSeconds(CooldownSeconds))
            return Result.Failure<RequestEmailOtpResponse>(new LightResults.Error(ErrorCodes.EmailOtpCooldown));

        var code = Random.Shared.Next(100000, 1000000).ToString(CultureInfo.InvariantCulture);

        delivery.EmailOtpHash = BCrypt.Net.BCrypt.HashPassword(code);
        delivery.EmailOtpEmail = cmd.Email.Trim().ToLowerInvariant();
        delivery.EmailOtpExpiresAt = now.AddMinutes(TtlMinutes);
        delivery.EmailOtpUsedAt = null;
        delivery.EmailOtpLastSentAt = now;
        await db.SaveChangesAsync(cancellationToken);

        if (emailSender.IsConfigured)
        {
            try
            {
                await emailSender.SendAsync(cmd.Email, "QuickShare", Subject, BuildBody(code), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send email OTP for delivery {DeliveryId}", delivery.Id);
            }
        }

        return Result.Success(new RequestEmailOtpResponse(true));
    }

    private static string BuildBody(string code)
    {
        return $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào bạn,</p>
              <p>Mã xác nhận để tải file của bạn:</p>
              <p style="font-size:28px;font-weight:700;letter-spacing:0.2em">{code}</p>
              <p>Mã có hiệu lực trong 15 phút và chỉ dùng được một lần.</p>
            </div>
            """;
    }
}
