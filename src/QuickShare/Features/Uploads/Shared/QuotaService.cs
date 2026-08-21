using QuickShare.Features.Payments;
using QuickShare.Features.Payments.Billing;

namespace QuickShare.Features.Uploads.Shared;

/// <summary>
/// Enforces and records the monthly upload quota for authenticated users.
/// Anonymous uploads have no monthly cap (only the per-file size limit).
/// The UserUsage row is lazily created on the first upload of the month.
///
/// Workspace members (issue #106) draw from their owner's usage row rather than their own:
/// the pooling lives here, in the one function both the check and the record path go
/// through, because a member who checked against the pool but recorded against a private
/// row would have an unlimited quota in practice.
///
/// Studio meters instead of blocking (issue #141): a hard stop mid-delivery is the wrong
/// failure for a team on a deadline, so bytes past the pool are tallied and priced per
/// GB-month onto the open draft invoice. Free and Pro still block at 100% — an unbounded bill
/// is a worse surprise than a refused upload when nobody agreed to pay for overflow.
/// </summary>
public sealed class QuotaService(AppDbContext db)
{
    /// <summary>
    /// Checks whether <paramref name="size"/> bytes would fit under the caller's
    /// monthly quota. Does not mutate the row.
    /// </summary>
    public async Task<QuotaCheckResult> CheckAsync(Guid userId, string plan, long size, CancellationToken ct)
    {
        var planQuota = PlanTierLimits.GetMonthlyQuota(plan);
        if (planQuota is null)
            return QuotaCheckResult.Unlimited();

        var bucketId = await ResolveBucketAsync(userId, ct);
        var quota = planQuota.Value + await BonusAsync(bucketId, ct);

        var usage = await db.UserUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == bucketId && u.PeriodStart == CurrentMonthStartUtc(), ct);

        var used = usage?.TotalBytesUploaded ?? 0;
        if (size > quota - used)
        {
            return PlanCatalog.AllowsOverage(plan)
                ? QuotaCheckResult.Metered(used, quota)
                : QuotaCheckResult.Exceeded(used, quota);
        }

        return QuotaCheckResult.Allowed(used, quota);
    }

    /// <summary>
    /// Accumulates <paramref name="size"/> bytes into the caller's current-month
    /// usage. Called only after a successful commit; the caller persists via its
    /// own SaveChanges (tracked update, not ExecuteUpdate).
    /// </summary>
    public async Task RecordAsync(Guid userId, string plan, long size, CancellationToken ct)
    {
        var planQuota = PlanTierLimits.GetMonthlyQuota(plan);
        if (planQuota is null)
            return;

        var bucketId = await ResolveBucketAsync(userId, ct);
        var usage = await GetOrCreateAsync(bucketId, ct);
        usage.TotalBytesUploaded += size;
        usage.TransferCount++;

        var quota = planQuota.Value + await BonusAsync(bucketId, ct);
        await MeterOverageAsync(bucketId, plan, usage, quota, ct);
        await AlertThresholdAsync(bucketId, usage, quota, ct);
    }

    /// <summary>
    /// Prices whatever sits past the pool onto the open draft invoice (issue #141). The overage
    /// line is rewritten rather than appended, because a member uploading all afternoon should
    /// produce one charge on the bill, not two hundred.
    /// </summary>
    private async Task MeterOverageAsync(
        Guid bucketId, string plan, UserUsage usage, long quota, CancellationToken ct)
    {
        if (!PlanCatalog.AllowsOverage(plan))
            return;

        var overage = Math.Max(0, usage.TotalBytesUploaded - quota);
        if (overage == usage.OverageBytes)
            return;

        usage.OverageBytes = overage;

        var gb = InvoiceService.OverageGb(overage);
        var rate = PlanCatalog.OverageGbAmount(plan);
        await new InvoiceService(db).AddOrUpdateLineAsync(
            bucketId,
            InvoiceLineKind.Overage,
            $"Overage {gb:0.##} GB @ {rate:N0}/GB",
            gb,
            rate,
            ct);
    }

    /// <summary>
    /// Notifies the payer when usage crosses 80/100/120% (issue #141). The highest threshold
    /// already sent is stored on the usage row so each one fires once — an alert that repeats
    /// on every upload past the line is an alert people mute.
    /// </summary>
    private async Task AlertThresholdAsync(Guid bucketId, UserUsage usage, long quota, CancellationToken ct)
    {
        if (quota <= 0)
            return;

        var percent = (int)(usage.TotalBytesUploaded * 100 / quota);
        var crossed = QuotaAlertThresholds.LastOrDefault(t => percent >= t);
        if (crossed == 0 || crossed <= usage.AlertedThresholdPercent)
            return;

        usage.AlertedThresholdPercent = crossed;

        db.Notifications.Add(new Notification
        {
            UserId = bucketId,
            Type = NotificationType.QuotaThreshold,
            Title = $"{crossed}%",
            LinkUrl = RoutePaths.AccountUsage,
        });

        await Task.CompletedTask;
    }

    /// <summary>Ascending, so the highest crossed threshold is the one reported.</summary>
    private static readonly int[] QuotaAlertThresholds = [80, 100, 120];

    /// <summary>
    /// The account whose monthly usage row this upload counts against (issue #106): the
    /// workspace owner for an active seat, otherwise the uploader themselves.
    /// </summary>
    private async Task<Guid> ResolveBucketAsync(Guid userId, CancellationToken ct)
    {
        var ownerId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.RevokedAt == null && m.JoinedAt != null)
            .Select(m => (Guid?)m.Workspace.OwnerId)
            .FirstOrDefaultAsync(ct);

        return ownerId ?? userId;
    }

    /// <summary>Permanent referral bonus (issue #15), on top of the plan quota.</summary>
    private async Task<long> BonusAsync(Guid userId, CancellationToken ct)
    {
        var bonus = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (long?)u.BonusQuotaBytes)
            .FirstOrDefaultAsync(ct);

        return bonus ?? 0;
    }

    private async Task<UserUsage> GetOrCreateAsync(Guid userId, CancellationToken ct)
    {
        var periodStart = CurrentMonthStartUtc();
        var usage = await db.UserUsages
            .FirstOrDefaultAsync(u => u.UserId == userId && u.PeriodStart == periodStart, ct);
        if (usage is not null)
            return usage;

        usage = new UserUsage
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            PeriodStart = periodStart,
            TotalBytesUploaded = 0,
            DailyBandwidthBonus = 0,
            TransferCount = 0
        };
        db.UserUsages.Add(usage);
        return usage;
    }

    private static DateTime CurrentMonthStartUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}

public sealed class QuotaCheckResult
{
    public bool IsUnlimited { get; private init; }
    public bool IsExceeded { get; private init; }
    /// <summary>Past the pool but allowed to continue, billed per GB (issue #141, Studio only).</summary>
    public bool IsMetered { get; private init; }
    public long Used { get; private init; }
    public long Quota { get; private init; }
    public long Remaining => Math.Max(0, Quota - Used);

    public static QuotaCheckResult Unlimited()
        => new() { IsUnlimited = true };

    public static QuotaCheckResult Allowed(long used, long quota)
        => new() { Used = used, Quota = quota };

    public static QuotaCheckResult Exceeded(long used, long quota)
        => new() { IsExceeded = true, Used = used, Quota = quota };

    public static QuotaCheckResult Metered(long used, long quota)
        => new() { IsMetered = true, Used = used, Quota = quota };
}
