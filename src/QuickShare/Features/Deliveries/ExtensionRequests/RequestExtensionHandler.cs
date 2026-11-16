using System.Net;
using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Deliveries.ExtensionRequests;

public record RequestExtensionCommand(string Code) : IRequest<Result<RequestExtensionResponse>>;

public record RequestExtensionResponse(bool Requested);

public sealed class RequestExtensionHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<RequestExtensionHandler> logger)
    : IRequestHandler<RequestExtensionCommand, Result<RequestExtensionResponse>>
{
    private const string RequesterCookie = "qs_ext_req";
    private const string RequesterTokenChars = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const string EmailSubject = "Yêu cầu gia hạn liên kết QuickShare";

    private readonly EmailOptions _options = emailOptions.Value;

    public async ValueTask<Result<RequestExtensionResponse>> Handle(
        RequestExtensionCommand cmd, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.Code || d.Alias == cmd.Code, cancellationToken);

        if (delivery is null)
            return Result.Failure<RequestExtensionResponse>(new LightResults.Error(ErrorCodes.NotFound));

        if (!delivery.AllowExtensionRequests)
            return Result.Failure<RequestExtensionResponse>(new LightResults.Error(ErrorCodes.ExtensionRequestsDisabled));

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
            return Result.Failure<RequestExtensionResponse>(new LightResults.Error(ErrorCodes.DeliveryNotActive));

        // Requests only make sense once the link has actually expired.
        if (delivery.ExpiresAt is { } expiresAt && expiresAt > DateTime.UtcNow)
            return Result.Failure<RequestExtensionResponse>(new LightResults.Error(ErrorCodes.DeliveryNotExpired));

        var token = GetOrSetRequesterToken(http.HttpContext!);

        // One open request per recipient per delivery — idempotent, no spam (issue #52).
        var alreadyRequested = await db.ExtensionRequests
            .AnyAsync(r => r.DeliveryId == delivery.Id && r.RequesterToken == token
                && r.Status == ExtensionRequestStatus.Pending, cancellationToken);
        if (alreadyRequested)
            return Result.Success(new RequestExtensionResponse(true));

        db.ExtensionRequests.Add(new ExtensionRequest
        {
            DeliveryId = delivery.Id,
            RequesterToken = token,
            Status = ExtensionRequestStatus.Pending,
        });
        await db.SaveChangesAsync(cancellationToken);

        await NotifySenderAsync(delivery, cancellationToken);

        return Result.Success(new RequestExtensionResponse(true));
    }

    private static string GetOrSetRequesterToken(HttpContext context)
    {
        var existing = context.Request.Cookies[RequesterCookie];
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var token = RandomToken(32);
        context.Response.Cookies.Append(RequesterCookie, token, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(365),
        });
        return token;
    }

    private static string RandomToken(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = RequesterTokenChars[Random.Shared.Next(RequesterTokenChars.Length)];
        return new string(chars);
    }

    private async Task NotifySenderAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        var link = $"{RoutePaths.AccountDeliveries}/{delivery.Id}/extension-requests";

        if (delivery.SenderId is { } senderId)
        {
            db.Notifications.Add(new Notification
            {
                UserId = senderId,
                Type = NotificationType.ExtensionRequested,
                Title = delivery.FileName,
                LinkUrl = link,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(delivery.SenderEmail) && delivery.SenderEmailConsent && emailSender.IsConfigured)
        {
            try
            {
                await emailSender.SendAsync(delivery.SenderEmail, "QuickShare", EmailSubject,
                    BuildEmailBody(delivery, link), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send extension-request email for delivery {DeliveryId}", delivery.Id);
            }
        }
    }

    private string BuildEmailBody(Delivery delivery, string link)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào bạn,</p>
              <p>Ai đó đã yêu cầu gia hạn liên kết cho file <strong>{WebUtility.HtmlEncode(delivery.FileName)}</strong>.</p>
              <p><a href="{baseUrl}{link}">Xem và duyệt yêu cầu</a></p>
            </div>
            """;
    }
}
