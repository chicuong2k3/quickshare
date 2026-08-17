using System.Net;
using System.Text;
using Hangfire;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Emails file-request owners when new files land in one of their requests (issue #16).
/// Digest, not per-file: one email per owner per cycle listing every pending delivery,
/// so 40 submissions to a request send 1 email, not 40.
/// </summary>
public sealed class FileRequestDigestJob(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    UnsubscribeTokens unsubscribeTokens,
    IOptions<EmailOptions> emailOptions,
    ILogger<FileRequestDigestJob> logger)
{
    private const int BatchSize = 500;
    private const string Subject = "Có file mới gửi đến yêu cầu của bạn";

    private sealed record PendingDelivery(Guid Id, Guid FileRequestId, string FileName, long TotalBytes);
    private sealed record PendingRequest(Guid Id, Guid OwnerId, string Title, string Code);

    private readonly EmailOptions _options = emailOptions.Value;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task NotifyAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var pending = await db.Deliveries
            .Where(d => d.FileRequestId != null && d.DigestNotifiedAt == null && d.Status == DeliveryStatus.Active)
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .Select(d => new PendingDelivery(d.Id, d.FileRequestId!.Value, d.FileName, d.TotalBytes))
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var requestIds = pending
            .Select(d => d.FileRequestId)
            .Distinct()
            .ToList();

        var requests = await db.FileRequests
            .AsNoTracking()
            .Where(r => requestIds.Contains(r.Id))
            .Select(r => new PendingRequest(r.Id, r.OwnerId, r.Title, r.Code))
            .ToListAsync(ct);

        var requestById = requests.ToDictionary(r => r.Id);

        var ownerIds = requests
            .Select(r => r.OwnerId)
            .Distinct()
            .ToList();

        var recipients = await db.Users
            .AsNoTracking()
            .Where(u => ownerIds.Contains(u.Id) && u.EmailOnDownload && u.Email != string.Empty)
            .ToDictionaryAsync(u => u.Id, ct);

        var groups = pending
            .Where(d => requestById.ContainsKey(d.FileRequestId))
            .GroupBy(d => requestById[d.FileRequestId].OwnerId)
            .ToList();

        var sent = 0;

        foreach (var group in groups)
        {
            if (!recipients.TryGetValue(group.Key, out var user)) continue;

            var requestsForUser = group
                .GroupBy(d => d.FileRequestId)
                .ToDictionary(g => g.Key, g => g.ToList());

            try
            {
                await emailSender.SendAsync(user.Email, user.Name, Subject, BuildBody(user, requestsForUser, requestById), ct);
                sent++;
            }
            catch (Exception ex)
            {
                // ponytail: log and drop — a failed digest isn't worth a retry queue.
                // Add a durable outbox if bounce/delivery guarantees ever matter.
                logger.LogWarning(ex, "Failed to send file-request digest to user {UserId}", user.Id);
            }
        }

        // Marked regardless of send outcome, so a broken mailbox can't replay the same digest forever.
        var pendingIds = pending.Select(d => d.Id).ToList();
        var toMark = await db.Deliveries
            .Where(d => pendingIds.Contains(d.Id))
            .ToListAsync(ct);

        foreach (var delivery in toMark)
            delivery.DigestNotifiedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation("FileRequestDigestJob sent {Sent} digests for {Count} submissions", sent, pending.Count);
    }

    private string BuildBody(
        User user,
        Dictionary<Guid, List<PendingDelivery>> submissionsByRequest,
        Dictionary<Guid, PendingRequest> requestById)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var unsubscribeUrl = $"{baseUrl}{RoutePaths.Unsubscribe}/{unsubscribeTokens.Create(user.Id)}";

        var lines = new StringBuilder();
        foreach (var (requestId, deliveries) in submissionsByRequest)
        {
            var request = requestById[requestId];
            lines.Append(CultureInfo.InvariantCulture, $"<li><strong>{WebUtility.HtmlEncode(request.Title)}</strong>");
            if (deliveries.Count == 1)
            {
                var d = deliveries[0];
                lines.Append(CultureInfo.InvariantCulture,
                    $" — {WebUtility.HtmlEncode(d.FileName)} ({FormatBytes(d.TotalBytes)})");
            }
            else
            {
                lines.Append(CultureInfo.InvariantCulture,
                    $" — {deliveries.Count} file mới");
            }
            lines.Append(CultureInfo.InvariantCulture,
                $" (<a href=\"{baseUrl}{RoutePaths.FileRequestPage}{request.Code}\">mở yêu cầu</a>)</li>");
        }

        return $"""
            <div style="font-family:system-ui,sans-serif;font-size:15px;color:#1f2937">
              <p>Chào {WebUtility.HtmlEncode(user.Name)},</p>
              <p>Có file mới được gửi đến yêu cầu của bạn:</p>
              <ul>{lines}</ul>
              <p><a href="{baseUrl}{RoutePaths.AccountFileRequests}">Xem tất cả trong tài khoản</a></p>
              <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0">
              <p style="font-size:13px;color:#6b7280">
                Không muốn nhận email này nữa? <a href="{unsubscribeUrl}">Huỷ đăng ký</a>.
              </p>
            </div>
            """;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var magnitude = (int)Math.Floor(Math.Log(bytes, 1024));
        if (magnitude >= units.Length) magnitude = units.Length - 1;
        var size = bytes / Math.Pow(1024, magnitude);
        return magnitude == 0 ? $"{bytes} B" : $"{size:F1} {units[magnitude]}";
    }
}
