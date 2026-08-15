namespace QuickShare.Features.Uploads.Shared;

/// <summary>
/// Enforces and records the monthly upload quota for authenticated users.
/// Anonymous uploads have no monthly cap (only the per-file size limit).
/// The UserUsage row is lazily created on the first upload of the month.
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

        var quota = planQuota.Value + await BonusAsync(userId, ct);

        var usage = await db.UserUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.PeriodStart == CurrentMonthStartUtc(), ct);

        var used = usage?.TotalBytesUploaded ?? 0;
        if (size > quota - used)
            return QuotaCheckResult.Exceeded(used, quota);

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

        var usage = await GetOrCreateAsync(userId, ct);
        usage.TotalBytesUploaded += size;
        usage.TransferCount++;
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
    public long Used { get; private init; }
    public long Quota { get; private init; }
    public long Remaining => Math.Max(0, Quota - Used);

    public static QuotaCheckResult Unlimited()
        => new() { IsUnlimited = true };

    public static QuotaCheckResult Allowed(long used, long quota)
        => new() { Used = used, Quota = quota };

    public static QuotaCheckResult Exceeded(long used, long quota)
        => new() { IsExceeded = true, Used = used, Quota = quota };
}
