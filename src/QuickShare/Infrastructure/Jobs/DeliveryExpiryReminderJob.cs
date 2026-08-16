using System.Net;
using Hangfire;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Sender-expiry reminder (issue #29): emails senders whose file expires within
/// 24h so they can notify the recipient or extend. Only senders who opted in
/// with a confirmed-use email get reminded; the flag dedups to one email per file.
/// </summary>
public sealed class DeliveryExpiryReminderJob(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<DeliveryExpiryReminderJob> logger)
{
    private const string Subject = "File của bạn sắp hết hạn — tải về trước khi mất";

    private readonly EmailOptions _options = emailOptions.Value;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RemindAsync()
    {
        var ct = CancellationToken.None;
        var now = DateTime.UtcNow;
        var windowEnd = now.AddHours(24);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidates = await db.Deliveries
            .Where(d => d.Status == DeliveryStatus.Active)
            .Where(d => d.ExpiresAt.HasValue)
            .Where(d => d.ExpiresAt!.Value > now && d.ExpiresAt!.Value <= windowEnd)
            .Where(d => d.SenderEmail != null && d.SenderEmail != string.Empty && d.SenderEmailConsent)
            .Where(d => d.ExpiryReminderSentAt == null)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        var sent = 0;

        foreach (var delivery in candidates)
        {
            try
            {
                await emailSender.SendAsync(
                    delivery.SenderEmail!, string.Empty, Subject, BuildBody(delivery), ct);
                sent++;
            }
            catch (Exception ex)
            {
                // ponytail: log and drop — a failed reminder isn't worth a retry queue.
                logger.LogWarning(ex, "Failed to send expiry reminder for delivery {DeliveryId}", delivery.Id);
            }

            // Marked regardless of send outcome, so a broken mailbox can't replay the reminder.
            delivery.ExpiryReminderSentAt = now;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation("DeliveryExpiryReminderJob reminded {Sent} senders of {Count} expiring files", sent, candidates.Count);
    }

    private string BuildBody(Delivery delivery)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var shareUrl = $"{baseUrl}/d/{delivery.ShareCode}";
        var expiresLabel = delivery.ExpiresAt!.Value.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);

        return $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào bạn,</p>
              <p>File <strong>{WebUtility.HtmlEncode(delivery.FileName)}</strong> của bạn sẽ hết hạn lúc <strong>{expiresLabel}</strong>.</p>
              <p>Nếu người nhận chưa tải, hãy gửi link này cho họ trước khi file bị xoá:</p>
              <p><a href="{shareUrl}" style="color:#2563eb">{shareUrl}</a></p>
              <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0">
              <p style="font-size:13px;color:#6b7280">Bạn nhận email này vì đã cung cấp email khi gửi file qua QuickShare.</p>
            </div>
            """;
    }
}