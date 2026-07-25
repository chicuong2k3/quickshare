using QuickShare.Server.Data;

namespace QuickShare.Server.Infrastructure.Quota;

/// <summary>
/// Throws <see cref="QuotaExceededException"/> when the user exceeds
/// their plan-tier monthly limits.
/// </summary>
public sealed class QuotaEnforcer(AppDbContext db)
{
    /// <returns>The user's current usage record (creates one if missing).</returns>
    public async Task<UserUsage> EnsureUsageRecordAsync(User user, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usage = await db.UserUsages
            .FirstOrDefaultAsync(u => u.UserId == user.Id && u.PeriodStart == periodStart, ct);

        if (usage is not null)
            return usage;

        usage = new UserUsage
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            PeriodStart = periodStart,
            TotalBytesUploaded = 0,
            TransferCount = 0
        };
        db.UserUsages.Add(usage);
        return usage;
    }

    /// <summary>
    /// Throws if uploading <paramref name="fileSize"/> bytes would exceed
    /// the user's plan-tier monthly byte limit.
    /// </summary>
    public async Task CheckUploadQuotaAsync(User user, long fileSize, CancellationToken ct = default)
    {
        var (maxBytesPerMonth, _) = PlanTierLimits.GetMonthlyLimits(user.Plan);
        if (maxBytesPerMonth is null)
            return; // unlimited

        var usage = await EnsureUsageRecordAsync(user, ct);
        var projectedTotal = usage.TotalBytesUploaded + fileSize;
        if (projectedTotal > maxBytesPerMonth.Value)
        {
            throw new QuotaExceededException(
                $"Upload would exceed monthly limit of {maxBytesPerMonth.Value:N0} bytes " +
                $"(used: {usage.TotalBytesUploaded:N0}, requested: {fileSize:N0}). " +
                $"Upgrade your plan at /settings/billing.");
        }
    }

    /// <summary>
    /// Throws if performing a transfer (send) would exceed the user's
    /// plan-tier monthly transfer count limit.
    /// </summary>
    public async Task CheckTransferQuotaAsync(User user, CancellationToken ct = default)
    {
        var (_, maxTransfersPerMonth) = PlanTierLimits.GetMonthlyLimits(user.Plan);
        if (maxTransfersPerMonth is null)
            return; // unlimited

        var usage = await EnsureUsageRecordAsync(user, ct);
        if (usage.TransferCount >= maxTransfersPerMonth.Value)
        {
            throw new QuotaExceededException(
                $"Monthly transfer limit of {maxTransfersPerMonth.Value:N0} reached. " +
                $"Upgrade your plan at /settings/billing.");
        }
    }

    /// <summary>
    /// Checks whether <paramref name="fileSize"/> exceeds the plan-tier
    /// per-file size cap.  This is a synchronous check because the caps
    /// are purely a function of the plan constant.
    /// </summary>
    public void CheckFileSizeCap(User user, long fileSize)
    {
        var maxFileSize = PlanTierLimits.GetMaxFileSize(user.Plan);
        if (fileSize > maxFileSize)
        {
            throw new FileSizeLimitExceededException(
                $"File size {fileSize:N0} bytes exceeds plan limit of {maxFileSize:N0} bytes. " +
                $"Upgrade your plan at /settings/billing.");
        }
    }
}

public sealed class QuotaExceededException(string message) : InvalidOperationException(message);

public sealed class FileSizeLimitExceededException(string message) : InvalidOperationException(message);
