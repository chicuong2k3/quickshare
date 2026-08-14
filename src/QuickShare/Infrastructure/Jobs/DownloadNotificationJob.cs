using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Emails senders when their files get downloaded. Digest, not per-download:
/// one email per sender per cycle, so a file with 50 downloads sends 1 email, not 50.
/// </summary>
public sealed class DownloadNotificationJob(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    UnsubscribeTokens unsubscribeTokens,
    IOptions<EmailOptions> emailOptions,
    ILogger<DownloadNotificationJob> logger) : BackgroundService
{
    private const int BatchSize = 500;
    private const string Subject = "File của bạn vừa được tải";

    private readonly EmailOptions _options = emailOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!emailSender.IsConfigured)
        {
            logger.LogInformation("DownloadNotificationJob disabled — Email section not configured");
            return;
        }

        var interval = TimeSpan.FromMinutes(_options.DigestIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await NotifyAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "DownloadNotificationJob cycle failed"); }
        }
    }

    private async Task NotifyAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.DownloadTrackings
            .Where(t => !t.Notified)
            .OrderBy(t => t.DownloadedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var deliveryIds = pending
            .Select(t => t.DeliveryId)
            .Distinct()
            .ToList();

        var deliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => deliveryIds.Contains(d.Id) && d.SenderId != null)
            .Select(d => new { d.Id, SenderId = d.SenderId!.Value, d.FileName })
            .ToListAsync(ct);

        var deliveryById = deliveries.ToDictionary(d => d.Id);

        var groups = pending
            .Where(t => deliveryById.ContainsKey(t.DeliveryId))
            .GroupBy(t => deliveryById[t.DeliveryId].SenderId)
            .ToList();

        var senderIds = groups
            .Select(g => g.Key)
            .ToList();

        var recipients = await db.Users
            .AsNoTracking()
            .Where(u => senderIds.Contains(u.Id) && u.EmailOnDownload && u.Email != string.Empty)
            .ToDictionaryAsync(u => u.Id, ct);

        var sent = 0;

        foreach (var group in groups)
        {
            if (!recipients.TryGetValue(group.Key, out var user)) continue;

            var counts = group
                .GroupBy(t => deliveryById[t.DeliveryId].FileName)
                .ToDictionary(g => g.Key, g => g.Count());

            try
            {
                await emailSender.SendAsync(user.Email, user.Name, Subject, BuildBody(user, counts), ct);
                sent++;
            }
            catch (Exception ex)
            {
                // ponytail: log and drop — a failed digest isn't worth a retry queue.
                // Add a durable outbox if bounce/delivery guarantees ever matter.
                logger.LogWarning(ex, "Failed to send download digest to user {UserId}", user.Id);
            }
        }

        // Marked regardless of send outcome, so a broken mailbox can't replay the same digest forever.
        foreach (var tracking in pending)
            tracking.Notified = true;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("DownloadNotificationJob sent {Sent} digests for {Count} downloads", sent, pending.Count);
    }

    private string BuildBody(User user, Dictionary<string, int> downloadsByFile)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var unsubscribeUrl = $"{baseUrl}{RoutePaths.Unsubscribe}/{unsubscribeTokens.Create(user.Id)}";

        var lines = new StringBuilder();
        foreach (var (fileName, count) in downloadsByFile)
            lines.Append(CultureInfo.InvariantCulture, $"<li><strong>{WebUtility.HtmlEncode(fileName)}</strong> — {count} lượt tải</li>");

        return $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào {WebUtility.HtmlEncode(user.Name)},</p>
              <p>File bạn gửi qua QuickShare vừa được tải:</p>
              <ul>{lines}</ul>
              <p><a href="{baseUrl}{RoutePaths.AccountDeliveries}">Xem chi tiết trong tài khoản</a></p>
              <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0">
              <p style="font-size:13px;color:#6b7280">
                Không muốn nhận email này nữa? <a href="{unsubscribeUrl}">Huỷ đăng ký</a>.
              </p>
            </div>
            """;
    }
}
