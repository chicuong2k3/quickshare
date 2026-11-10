using Hangfire;
using Microsoft.Extensions.Options;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.ColdStorage;

public sealed record ColdStoragePolicyResponse(
    int? ColdStorageDays,
    int EffectiveDays,
    long ColdFilesCount,
    long ColdBytesTotal,
    long DedupSavedBytes);

public sealed class UpdateColdStoragePolicyRequest
{
    public int? ColdStorageDays { get; set; }
}

/// <summary>
/// GET /api/account/cold-storage — reads the account's cold-storage lifecycle threshold
/// and current archive + deduplication savings metrics (issues #314, #197).
/// </summary>
public sealed class GetColdStoragePolicyEndpoint : EndpointWithoutRequest<ColdStoragePolicyResponse>
{
    public override void Configure()
    {
        Get(RoutePaths.AccountColdStorageApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Get cold storage lifecycle policy and storage tier savings");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var opts = Resolve<IOptions<QuickShareOptions>>().Value;
        var user = await User.GetUserAsync(db, ct);

        var coldDeliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == user.Id && d.DeletedAt == null && d.StorageTier == DeliveryStorageTier.Cold)
            .Select(d => d.TotalFileBytes > 0 ? d.TotalFileBytes : d.FileSize)
            .ToListAsync(ct);

        var dedupDeliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == user.Id && d.DeletedAt == null && d.IsDeduplicated)
            .Select(d => d.TotalFileBytes > 0 ? d.TotalFileBytes : d.FileSize)
            .ToListAsync(ct);

        var effectiveDays = user.ColdStorageDays ?? opts.DefaultColdStorageDays;
        await Send.OkAsync(new ColdStoragePolicyResponse(
            user.ColdStorageDays,
            effectiveDays,
            coldDeliveries.Count,
            coldDeliveries.Sum(),
            dedupDeliveries.Sum()), ct);
    }
}

/// <summary>
/// PUT /api/account/cold-storage — sets or clears the account's cold-storage transition threshold in days (issue #314).
/// </summary>
public sealed class UpdateColdStoragePolicyEndpoint : Endpoint<UpdateColdStoragePolicyRequest, ColdStoragePolicyResponse>
{
    public const int MinDays = 1;
    public const int MaxDays = 365;

    public override void Configure()
    {
        Put(RoutePaths.AccountColdStorageApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Update cold storage lifecycle policy");
    }

    public override async Task HandleAsync(UpdateColdStoragePolicyRequest req, CancellationToken ct)
    {
        if (req.ColdStorageDays is { } days && (days < MinDays || days > MaxDays))
        {
            ThrowError(ErrorCodes.ColdStorageDaysInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var db = Resolve<AppDbContext>();
        var opts = Resolve<IOptions<QuickShareOptions>>().Value;
        var cachedUser = await User.GetUserAsync(db, ct);
        var entity = await db.Users.FirstAsync(u => u.Id == cachedUser.Id, ct);

        entity.ColdStorageDays = req.ColdStorageDays;
        await db.SaveChangesAsync(ct);

        var effectiveDays = entity.ColdStorageDays ?? opts.DefaultColdStorageDays;
        await Send.OkAsync(new ColdStoragePolicyResponse(
            entity.ColdStorageDays,
            effectiveDays,
            0,
            0,
            0), ct);
    }
}

public sealed record RestoreColdDeliveryResponse(string ShareCode, string StorageTier, DateTime? RestoredUntil);

/// <summary>
/// POST /api/downloads/{ShareCode}/restore — restores a Cold delivery back to Hot on demand
/// so the recipient can download it (issue #314).
/// </summary>
public sealed class RestoreColdDeliveryEndpoint : EndpointWithoutRequest<RestoreColdDeliveryResponse>
{
    public static readonly TimeSpan RestoreWindow = TimeSpan.FromHours(24);

    public override void Configure()
    {
        Post(RoutePaths.DeliveryRestoreApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s => s.Summary = "Restore a cold-archived delivery on demand for download");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var cache = Resolve<CacheService>();
        var shareCode = Route<string>("ShareCode") ?? string.Empty;

        var delivery = await db.Deliveries
            .ByCode(shareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null || delivery.DeletedAt is not null || delivery.Status == DeliveryStatus.PendingApproval)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (delivery.StorageTier != DeliveryStorageTier.Cold && delivery.StorageTier != DeliveryStorageTier.Restoring)
        {
            ThrowError(ErrorCodes.DeliveryNotCold, StatusCodes.Status400BadRequest);
            return;
        }

        var until = DateTime.UtcNow.Add(RestoreWindow);
        delivery.StorageTier = DeliveryStorageTier.Hot;
        delivery.RestoredUntil = until;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

        await Send.OkAsync(new RestoreColdDeliveryResponse(delivery.ShareCode, delivery.StorageTier, delivery.RestoredUntil), ct);
    }
}

/// <summary>
/// Transitions old active deliveries from <see cref="DeliveryStorageTier.Hot"/> to
/// <see cref="DeliveryStorageTier.Cold"/> after the sender's (or deployment's) N-day threshold,
/// and relieves their bytes from the current billing period's live quota meter (issue #314).
/// </summary>
public sealed class ColdStorageSweepJob(
    IServiceScopeFactory scopeFactory,
    IOptions<QuickShareOptions> options,
    ILogger<ColdStorageSweepJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task SweepAsync()
    {
        var ct = CancellationToken.None;
        var defaultDays = Math.Max(1, options.Value.DefaultColdStorageDays);
        var now = DateTime.UtcNow;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<CacheService>();

        var candidates = await db.Deliveries
            .Include(d => d.Sender)
            .Where(d => d.DeletedAt == null
                && d.Status == DeliveryStatus.Active
                && d.StorageTier == DeliveryStorageTier.Hot
                && (d.RestoredUntil == null || d.RestoredUntil < now))
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return;
        }

        var quota = new QuotaService(db);
        var transitioned = 0;

        foreach (var delivery in candidates)
        {
            var thresholdDays = delivery.Sender?.ColdStorageDays ?? defaultDays;
            if (thresholdDays <= 0)
            {
                continue;
            }

            var cutoff = now.AddDays(-thresholdDays);
            if (delivery.CreatedAt > cutoff)
            {
                continue;
            }

            delivery.StorageTier = DeliveryStorageTier.Cold;
            delivery.ArchivedAt = now;
            transitioned++;

            if (delivery.SenderId is { } senderId)
            {
                var bytes = delivery.TotalFileBytes > 0 ? delivery.TotalFileBytes : delivery.FileSize;
                await quota.RelieveColdBytesAsync(senderId, bytes, ct);
            }

            await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);
        }

        if (transitioned > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("ColdStorageSweepJob transitioned {Count} deliveries to Cold tier", transitioned);
        }
    }
}
