using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Maps the (formerly hand-rolled BackgroundService) maintenance loops to Hangfire
/// recurring jobs so they get a dashboard, per-job history, and manual trigger/retry.
/// Cron is 5-field (minute granularity), matching the poll cadence these jobs always
/// had — none of them need second-level precision.
/// </summary>
public static class HangfireJobs
{
    public const string DeliveryActivation = "delivery-activation";
    public const string DeliveryExpiry = "delivery-expiry";
    public const string SubscriptionExpiry = "subscription-expiry";
    public const string ZombiePartSweep = "zombie-part-sweep";
    public const string DeliveryCleanup = "delivery-cleanup";
    public const string DownloadNotification = "download-notification";
    public const string DeliveryExpiryReminder = "delivery-expiry-reminder";
    public const string OrphanSweep = "orphan-sweep";
    public const string TrialReminder = "trial-reminder";
    public const string TempUploadCleanup = "temp-upload-cleanup";
    public const string WeeklyDigest = "weekly-digest";

    public static void Register(IServiceProvider services, bool hasR2)
    {
        // Hangfire's static API (RecurringJob/BackgroundJob) requires JobStorage.Current,
        // which AddHangfire does not set — use the DI-registered service APIs instead.
        var recurring = services.GetRequiredService<IRecurringJobManager>();
        var background = services.GetRequiredService<IBackgroundJobClient>();

        recurring.AddOrUpdate<DeliveryActivationJob>(DeliveryActivation, j => j.ActivateDeliveriesAsync(), Cron.Minutely());
        recurring.AddOrUpdate<DeliveryExpiryJob>(DeliveryExpiry, j => j.ExpireDeliveriesAsync(), Cron.MinuteInterval(5));
        recurring.AddOrUpdate<SubscriptionExpiryJob>(SubscriptionExpiry, j => j.CheckAndExpireAsync(), Cron.MinuteInterval(15));
        recurring.AddOrUpdate<ZombiePartSweeper>(ZombiePartSweep, j => j.SweepZombiePartsAsync(), Cron.MinuteInterval(30));
        recurring.AddOrUpdate<TempUploadCleanupJob>(TempUploadCleanup, j => j.CleanupAsync(), Cron.MinuteInterval(15));

        var quickShareOptions = services.GetRequiredService<IOptions<QuickShareOptions>>().Value;
        recurring.AddOrUpdate<DeliveryCleanupService>(DeliveryCleanup, j => j.CleanupAsync(),
            Cron.MinuteInterval(Math.Max(1, quickShareOptions.CleanupIntervalMinutes)));

        var emailSender = services.GetRequiredService<EmailSender>();
        if (emailSender.IsConfigured)
        {
            var emailOptions = services.GetRequiredService<IOptions<EmailOptions>>().Value;
            recurring.AddOrUpdate<DownloadNotificationJob>(DownloadNotification, j => j.NotifyAsync(),
                Cron.MinuteInterval(Math.Max(1, emailOptions.DigestIntervalMinutes)));
            recurring.AddOrUpdate<TrialReminderJob>(TrialReminder, j => j.NotifyAsync(), Cron.Daily());
            recurring.AddOrUpdate<DeliveryExpiryReminderJob>(DeliveryExpiryReminder, j => j.RemindAsync(), Cron.Hourly());
            recurring.AddOrUpdate<WeeklyDigestJob>(WeeklyDigest, j => j.NotifyAsync(), Cron.Weekly());
        }

        if (hasR2)
        {
            recurring.AddOrUpdate<OrphanSweepJob>(OrphanSweep, j => j.SweepAsync(),
                Cron.HourInterval(Math.Max(1, quickShareOptions.OrphanSweepIntervalHours)));

            // First sweep soon after boot — an hourly-only schedule would never run on
            // hosts that redeploy daily (same reason the old loop used a 15-min initial delay).
            background.Schedule<OrphanSweepJob>(j => j.SweepAsync(), TimeSpan.FromMinutes(15));
        }
    }
}
