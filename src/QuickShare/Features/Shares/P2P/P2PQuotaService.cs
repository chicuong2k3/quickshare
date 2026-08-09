namespace QuickShare.Features.Shares.P2P;

/// <summary>
/// Enforces the daily P2P share-link creation limit for free/anonymous users.
/// Authenticated users are keyed by UserId; anonymous users by IP address.
/// Paid plans (Pro/Studio) are unlimited and skip tracking entirely.
/// Rewarded ads add unlock slots on top of the base limit for the day.
/// </summary>
public sealed class P2PQuotaService(AppDbContext db)
{
    /// <summary>
    /// Checks the caller's daily P2P upload allowance and, if allowed, increments
    /// today's usage count.
    /// </summary>
    /// <param name="userId">Authenticated user id, or null for anonymous.</param>
    /// <param name="ipAddress">Client IP — used as the identity key for anonymous callers.</param>
    /// <param name="plan">The caller's plan tier (use <see cref="PlanTierLimits.Anonymous"/> when not authenticated).</param>
    /// <returns>The quota result describing whether the share creation is allowed.</returns>
    public async Task<P2PQuotaResult> CheckAndIncrementAsync(Guid? userId, string ipAddress, string plan, CancellationToken ct)
    {
        var maxDaily = PlanTierLimits.GetMaxDailyP2PUploads(plan);
        if (maxDaily is null)
        {
            // Paid plans — unlimited, nothing to track.
            return P2PQuotaResult.Unlimited();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var usage = await GetOrCreateAsync(userId, ipAddress, today, ct);

        var allowance = maxDaily.Value + usage.UnlockedUploads;
        if (usage.UploadCount >= allowance)
        {
            return P2PQuotaResult.Rejected(
                maxDaily.Value,
                usage.UploadCount,
                usage.UnlockedUploads,
                allowance);
        }

        usage.UploadCount++;
        await db.SaveChangesAsync(ct);

        return P2PQuotaResult.Allowed(
            maxDaily.Value,
            usage.UploadCount,
            usage.UnlockedUploads,
            allowance);
    }

    /// <summary>
    /// Returns today's usage state without mutating it (for the send-page quota banner).
    /// </summary>
    public async Task<P2PQuotaResult> GetTodayAsync(Guid? userId, string ipAddress, string plan, CancellationToken ct)
    {
        var maxDaily = PlanTierLimits.GetMaxDailyP2PUploads(plan);
        if (maxDaily is null)
        {
            return P2PQuotaResult.Unlimited();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var usage = await GetOrCreateAsync(userId, ipAddress, today, ct);

        return P2PQuotaResult.Allowed(
            maxDaily.Value,
            usage.UploadCount,
            usage.UnlockedUploads,
            maxDaily.Value + usage.UnlockedUploads);
    }

    private async Task<DailyP2PUsage> GetOrCreateAsync(Guid? userId, string ipAddress, DateOnly today, CancellationToken ct)
    {
        if (userId is { } id)
        {
            var row = await db.DailyP2PUsages
                .FirstOrDefaultAsync(u => u.UserId == id && u.Date == today, ct);
            if (row is not null)
            {
                return row;
            }

            row = new DailyP2PUsage
            {
                Id = Guid.CreateVersion7(),
                UserId = id,
                IpAddress = string.Empty,
                Date = today,
                UploadCount = 0,
                UnlockedUploads = 0
            };
            db.DailyP2PUsages.Add(row);
            return row;
        }

        var ip = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress;
        var anonRow = await db.DailyP2PUsages
            .FirstOrDefaultAsync(u => u.UserId == null && u.IpAddress == ip && u.Date == today, ct);
        if (anonRow is not null)
        {
            return anonRow;
        }

        anonRow = new DailyP2PUsage
        {
            Id = Guid.CreateVersion7(),
            UserId = null,
            IpAddress = ip,
            Date = today,
            UploadCount = 0,
            UnlockedUploads = 0
        };
        db.DailyP2PUsages.Add(anonRow);
        return anonRow;
    }
}

public sealed class P2PQuotaResult
{
    public bool IsUnlimited { get; private init; }
    public bool IsAllowed { get; private init; }
    public int Limit { get; private init; }
    public int Used { get; private init; }
    public int Unlocked { get; private init; }
    public int Allowance { get; private init; }
    public int Remaining => Math.Max(0, Allowance - Used);

    public static P2PQuotaResult Unlimited()
        => new() { IsUnlimited = true, IsAllowed = true };

    public static P2PQuotaResult Allowed(int limit, int used, int unlocked, int allowance)
        => new() { IsUnlimited = false, IsAllowed = true, Limit = limit, Used = used, Unlocked = unlocked, Allowance = allowance };

    public static P2PQuotaResult Rejected(int limit, int used, int unlocked, int allowance)
        => new() { IsUnlimited = false, IsAllowed = false, Limit = limit, Used = used, Unlocked = unlocked, Allowance = allowance };
}
