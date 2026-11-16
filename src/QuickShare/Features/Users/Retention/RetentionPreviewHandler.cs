using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Users.Retention;

public record RetentionPreviewQuery(int? Days) : IRequest<Result<RetentionPreviewResponse>>;

/// <summary>One of the caller's own shares that a proposed window would cut short.</summary>
public sealed record RetentionPreviewItem(string ShareCode, string FileName, DateTime? ExpiresAt);

/// <summary>
/// Dry-run outcome of a retention window (issue #161). <paramref name="EarliestExpiry"/> and
/// <paramref name="LatestExpiry"/> cover only the affected rows that actually carry an expiry —
/// a never-expiring share is counted in <paramref name="Affected"/> but has no date to bound.
/// </summary>
public sealed record RetentionPreviewResponse(
    int? Days,
    int Affected,
    DateTime? EarliestExpiry,
    DateTime? LatestExpiry,
    IReadOnlyList<RetentionPreviewItem> Samples);

/// <summary>
/// Answers "what would this retention window shorten?" before the owner commits to it
/// (issue #161). Read-only — nothing is clamped here; the sweep and the upload path do that.
/// Scoped to the caller's own deliveries, and legally held rows are excluded because the
/// sweep will never clamp them either.
/// </summary>
public sealed class RetentionPreviewHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<RetentionPreviewQuery, Result<RetentionPreviewResponse>>
{
    /// <summary>Sample rows returned so the owner can recognize what is affected. Not a report.</summary>
    public const int MaxSamples = 20;

    private const string CacheKeyPrefix = "user:retention:preview:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>Preview cache key. Public so the settings mutation can evict what it dirties.</summary>
    public static string CacheKey(Guid userId, int days) =>
        $"{CacheKeyPrefix}{userId}:{days.ToString(CultureInfo.InvariantCulture)}";

    public async ValueTask<Result<RetentionPreviewResponse>> Handle(
        RetentionPreviewQuery query,
        CancellationToken cancellationToken)
    {
        if (!RetentionPolicy.IsValid(query.Days))
            return Result.Failure<RetentionPreviewResponse>(new LightResults.Error(ErrorCodes.RetentionDaysInvalid));

        // "No policy" is a valid selection and shortens nothing — no query needed.
        if (query.Days is not { } days)
            return Result.Success(new RetentionPreviewResponse(null, 0, null, null, []));

        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var response = await cache.GetOrSetAsync(
            CacheKey(user.Id, days),
            ct => BuildAsync(user.Id, days, ct),
            CacheDuration,
            cancellationToken);

        return Result.Success(response);
    }

    private async Task<RetentionPreviewResponse> BuildAsync(Guid userId, int days, CancellationToken ct)
    {
        var ceiling = RetentionPolicy.Ceiling(days);

        var affected = db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == userId
                && d.DeletedAt == null
                && d.LegalHoldAt == null
                && (d.Status == DeliveryStatus.Active || d.Status == DeliveryStatus.Scheduled)
                && (d.ExpiresAt == null || d.ExpiresAt > ceiling));

        // One aggregate round trip for count + bounds; SQL MIN/MAX skip the null expiries.
        var stats = await affected
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Earliest = g.Min(d => d.ExpiresAt),
                Latest = g.Max(d => d.ExpiresAt),
            })
            .FirstOrDefaultAsync(ct);

        if (stats is null)
            return new RetentionPreviewResponse(days, 0, null, null, []);

        var samples = await affected
            .OrderByDescending(d => d.ExpiresAt)
            .Take(MaxSamples)
            .Select(d => new RetentionPreviewItem(d.ShareCode, d.FileName, d.ExpiresAt))
            .ToListAsync(ct);

        return new RetentionPreviewResponse(days, stats.Count, stats.Earliest, stats.Latest, samples);
    }
}
