using System.Text;
using Hangfire;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Weekly digest (issue #62): one email per sender summarizing their files this
/// week — how many were sent, downloaded, and are still live. Retention loop,
/// not a per-download ping (that's DownloadNotificationJob).
/// </summary>
public sealed class WeeklyDigestJob(
    IServiceScopeFactory scopeFactory,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<WeeklyDigestJob> logger)
{
    private const string Subject = "Your files this week on QuickShare";

    private readonly EmailOptions _options = emailOptions.Value;

    private sealed record DigestRecipient(Guid Id, string Name, string Email, int Sent, int Downloads, int Active);

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task NotifyAsync()
    {
        if (!emailSender.IsConfigured)
            return;

        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var senders = await db.Users
            .AsNoTracking()
            .Where(u => u.EmailOnDownload && u.Email != string.Empty)
            .Select(u => new DigestRecipient(
                u.Id,
                u.Name,
                u.Email,
                db.Deliveries.Count(d => d.SenderId == u.Id),
                db.Deliveries.Where(d => d.SenderId == u.Id).Sum(d => d.DownloadCount),
                db.Deliveries.Count(d => d.SenderId == u.Id && d.Status == DeliveryStatus.Active)))
            .Where(x => x.Sent > 0)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var sender in senders)
        {
            try
            {
                await emailSender.SendAsync(sender.Email, sender.Name, Subject, BuildBody(sender), ct);
                sent++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Weekly digest failed for user {UserId}", sender.Id);
            }
        }

        logger.LogInformation("WeeklyDigestJob sent {Sent} digests", sent);
    }

    private string BuildBody(DigestRecipient sender)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var sb = new StringBuilder();
        sb.Append("<p>Here's your QuickShare week:</p>");
        sb.Append("<ul>");
        sb.Append("<li><strong>").Append(sender.Sent).Append("</strong> files sent</li>");
        sb.Append("<li><strong>").Append(sender.Downloads).Append("</strong> downloads</li>");
        sb.Append("<li><strong>").Append(sender.Active).Append("</strong> links still live</li>");
        sb.Append("</ul>");
        sb.Append("<p><a href=\"").Append(baseUrl).Append("/account/deliveries\">View your deliveries</a> · <a href=\"");
        sb.Append(baseUrl).Append("/send\">Send another file</a></p>");
        return sb.ToString();
    }
}
