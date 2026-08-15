using System.Net;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Users.Shared;

/// <summary>
/// Sends the "new sign-in from a device we haven't seen" alert (issue #149).
/// Failures are logged and swallowed — an email outage must never break login.
/// </summary>
public sealed class NewDeviceAlertSender(
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<NewDeviceAlertSender> logger)
{
    private readonly EmailOptions _options = emailOptions.Value;

    public async Task SendAsync(User user, UserDevice device, DateTime signInAt, CancellationToken ct)
    {
        if (!emailSender.IsConfigured || string.IsNullOrEmpty(user.Email))
            return;

        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var subject = "Đăng nhập từ thiết bị mới — QuickShare";
        var body = $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào {WebUtility.HtmlEncode(user.Name)},</p>
              <p>Tài khoản của bạn vừa được đăng nhập từ một thiết bị mới:</p>
              <ul>
                <li><strong>Thiết bị:</strong> {WebUtility.HtmlEncode(device.UserAgentLabel)}</li>
                <li><strong>Thời gian:</strong> {signInAt.ToLocalTime():g}</li>
              </ul>
              <p>Nếu đó là bạn, không cần làm gì thêm.</p>
              <p>Nếu KHÔNG phải bạn: <a href="{baseUrl}{RoutePaths.Account}">Bảo mật tài khoản ngay</a> — kiểm tra danh sách thiết bị và đăng xuất các phiên lạ.</p>
              <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0">
              <p style="font-size:13px;color:#6b7280">QuickShare — email an toàn được gửi tự động khi phát hiện thiết bị lạ.</p>
            </div>
            """;

        try
        {
            await emailSender.SendAsync(user.Email, user.Name, subject, body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send new-device alert to user {UserId}", user.Id);
        }
    }
}
