using Hangfire;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Fires due recurring schedules (issue #176).
///
/// Each cycle copies the source delivery's bytes to a **new object with a new share code**,
/// rather than re-mailing the original link. A link that never rotates is a link that leaks,
/// and last week's recipients should not be able to open this week's numbers by revisiting an
/// old mail. The copy stays in the source delivery's storage region (issue #119).
///
/// Catch-up is deliberately skipped, not batched: the next run is computed forward from *now*,
/// so a service that was down for a week sends one report on restart rather than seven. A
/// recurring feature that storms on recovery becomes an incident of its own.
/// </summary>
public sealed class RecurringDeliveryJob(
    IServiceScopeFactory scopeFactory,
    IStorageProvider storage,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<RecurringDeliveryJob> logger)
{
    private const string Subject = "A file has been shared with you";

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var due = await db.RecurringDeliveries
            .Where(r => r.Enabled && r.NextRunAt <= now)
            .ToListAsync(ct);

        foreach (var schedule in due)
        {
            await RunOneAsync(db, schedule, now, ct);
        }

        if (due.Count > 0)
            await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Runs one schedule. Shared with the run-now endpoint so a manual fire exercises exactly
    /// the path the hourly tick does.
    /// </summary>
    public async Task RunOneAsync(AppDbContext db, RecurringDelivery schedule, DateTime now, CancellationToken ct)
    {
        schedule.LastRunAt = now;
        schedule.UpdatedAt = now;
        // Advanced before the attempt, not after: a schedule that throws must not retry on the
        // next tick forever, hammering storage once an hour with the same failure.
        schedule.NextRunAt = schedule.ComputeNextRun(now);

        try
        {
            var source = await db.Deliveries
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == schedule.SourceDeliveryId && d.DeletedAt == null, ct);

            if (source is null || string.IsNullOrEmpty(source.StoragePath))
            {
                // The content is gone. Stop rather than firing empty cycles forever, and say so
                // where the owner will see it.
                schedule.Enabled = false;
                schedule.LastError = "Source delivery is no longer available.";
                return;
            }

            var owner = await db.Users.AsNoTracking().FirstAsync(u => u.Id == schedule.OwnerId, ct);

            var newId = Guid.CreateVersion7();
            var newKey = R2ObjectKey.For(Guid.Empty, newId, source.FileName, source.StorageRegion);
            await storage.CopyObjectAsync(source.StoragePath, newKey, ct);

            var window = PlanTierLimits.GetMaxExpiry(owner.Plan);
            var copy = source with
            {
                Id = newId,
                ShareCode = ShareCodes.Generate(),
                StoragePath = newKey,
                Status = DeliveryStatus.Active,
                DownloadCount = 0,
                FirstDownloadedAt = null,
                DeliverAt = null,
                ExpiresAt = RetentionPolicy.Clamp(now.Add(window), owner.RetentionDays),
                Alias = null,
                CreatedAt = now,
                UpdatedAt = null,
                Sender = null,
                // A generated cycle is never itself under review: the schedule was approved
                // when it was created, and re-gating every cycle would make recurrence useless
                // in exactly the workspaces that turned approval on.
                ApprovalRequestedAt = null,
                ApprovedById = null,
                ApprovedAt = null,
                DenyReason = null,
                PendingRecipients = null,
                LegalHoldAt = null,
                LegalHoldReason = null,
                LegalHoldById = null,
                DeletedAt = null,
            };

            db.Deliveries.Add(copy);

            schedule.LastDeliveryId = copy.Id;
            schedule.RunCount++;
            schedule.LastError = null;

            db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.CreateVersion7(),
                UserId = schedule.OwnerId,
                WorkspaceId = schedule.WorkspaceId,
                EventType = SecurityEventType.RecurringDeliveryRan,
                IpHash = string.Empty,
                UserAgentLabel = nameof(RecurringDeliveryJob),
                MetadataJson = System.Text.Json.JsonSerializer.Serialize(
                    new { schedule.Name, copy.ShareCode, schedule.RunCount }),
                CreatedAt = now,
            });

            await NotifyAsync(schedule, copy, ct);
        }
        catch (Exception ex)
        {
            // One broken schedule must not stop the others; the error is the report.
            logger.LogWarning(ex, "Recurring delivery {ScheduleId} failed", schedule.Id);
            schedule.LastError = ex.Message.Length > RecurringDelivery.ErrorMaxLength
                ? ex.Message[..RecurringDelivery.ErrorMaxLength]
                : ex.Message;
        }
    }

    private async Task NotifyAsync(RecurringDelivery schedule, Delivery copy, CancellationToken ct)
    {
        if (!emailSender.IsConfigured)
            return;

        var recipients = schedule.Recipients
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (recipients.Length == 0)
            return;

        var link = $"{emailOptions.Value.BaseUrl.TrimEnd('/')}{RoutePaths.DeliveryPage}{copy.ShareCode}";
        var body = $"""
            <p>{System.Net.WebUtility.HtmlEncode(copy.FileName)} is ready for you.</p>
            <p><a href="{link}">Open the delivery</a></p>
            """;

        foreach (var recipient in recipients)
        {
            try
            {
                await emailSender.SendAsync(recipient, "QuickShare", Subject, body, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Recurring delivery mail failed for {ScheduleId}", schedule.Id);
            }
        }
    }
}
