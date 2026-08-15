using System.Net;
using Hangfire;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Day-5 conversion email for the 7-day Pro trial (issue #65): reminds users
/// whose trial ends in 1-3 days that the benefits lapse unless they convert.
/// </summary>
public sealed class TrialReminderJob(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<TrialReminderJob> logger)
{
    private const string Subject = "Pro trial sắp kết thúc — giữ quyền lợi của bạn";

    private readonly EmailOptions _options = emailOptions.Value;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task NotifyAsync()
    {
        var ct = CancellationToken.None;
        var now = DateTime.UtcNow;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidates = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && s.TrialEndsAt.HasValue)
            .Where(s => s.TrialEndsAt!.Value > now.AddDays(1) && s.TrialEndsAt!.Value <= now.AddDays(3))
            .Include(s => s.User)
            .ToListAsync(ct);

        var sent = 0;

        foreach (var sub in candidates)
        {
            var user = sub.User;
            if (string.IsNullOrEmpty(user.Email) || !sub.TrialEndsAt.HasValue)
                continue;

            var daysLeft = Math.Max(1, (int)Math.Ceiling((sub.TrialEndsAt.Value - now).TotalDays));

            try
            {
                await emailSender.SendAsync(user.Email, user.Name, Subject, BuildBody(user, daysLeft), ct);
                sent++;
            }
            catch (Exception ex)
            {
                // ponytail: log and drop — a failed reminder isn't worth a retry queue.
                logger.LogWarning(ex, "Failed to send trial reminder to user {UserId}", user.Id);
            }
        }

        if (sent > 0)
            logger.LogInformation("TrialReminderJob sent {Sent} reminders", sent);
    }

    private string BuildBody(User user, int daysLeft)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var pricingUrl = $"{baseUrl}/pricing";

        return $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào {WebUtility.HtmlEncode(user.Name)},</p>
              <p>Bản dùng thử Pro của bạn còn <strong>{daysLeft} ngày</strong>. Sau đó tài khoản quay về gói miễn phí — ưu tiên hàng đợi, thời hạn 30 ngày và không quảng cáo sẽ kết thúc.</p>
              <p style="margin-top:16px"><a href="{pricingUrl}" style="display:inline-block;background:#2563eb;color:#fff;padding:10px 20px;border-radius:6px;text-decoration:none">Giữ quyền lợi Pro</a></p>
              <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0">
              <p style="font-size:13px;color:#6b7280">Bạn nhận email này vì đang trong giai đoạn dùng thử Pro.</p>
            </div>
            """;
    }
}
