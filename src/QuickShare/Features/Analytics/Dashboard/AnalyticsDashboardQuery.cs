using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Analytics.Dashboard;

public record AnalyticsDashboardQuery(DateOnly? From = null, DateOnly? To = null);

public sealed record DailyActiveUsersRow(DateOnly Day, int Count);

public sealed record AnalyticsFunnelRow(int Visitor, int UploadStarted, int UploadCompleted, int DownloadCompleted);

public sealed record TopFileRow(string FileName, int DownloadCount);

public sealed record CountryRow(string? CountryCode, int Count);

public sealed record AnalyticsDashboardResponse(
    IReadOnlyList<DailyActiveUsersRow> DailyActiveUsers,
    AnalyticsFunnelRow Funnel,
    IReadOnlyList<TopFileRow> TopFiles,
    IReadOnlyList<CountryRow> GeographicSpread);

/// <summary>
/// Founder dashboard aggregates (issue #24): DAU, funnel, top files, geo spread.
/// Anonymous by design — DAU counts distinct qs_visitor cookie ids, never users.
/// </summary>
public sealed class AnalyticsDashboardHandler
{
    public async Task<AnalyticsDashboardResponse> Handle(AnalyticsDashboardQuery query, AppDbContext db, CacheService cache, CancellationToken ct)
    {
        var to = query.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var from = query.From ?? to.AddDays(-29);
        var toUtc = to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return await cache.GetOrSetAsync(
            $"analytics:dashboard:{from:yyyyMMdd}:{to:yyyyMMdd}",
            async ct2 =>
            {
                // Pull only the columns the dashboard needs; founder-scale volume
                // aggregates fine in memory and stays provider-agnostic (SQLite tests).
                var events = await db.AnalyticsEvents
                    .AsNoTracking()
                    .Where(e => e.CreatedAt >= fromUtc && e.CreatedAt < toUtc)
                    .Select(e => new { e.EventName, e.VisitorId, e.CountryCode, e.FileName, e.CreatedAt })
                    .ToListAsync(ct2);

                var daus = events
                    .Where(e => e.VisitorId.HasValue)
                    .GroupBy(e => DateOnly.FromDateTime(e.CreatedAt.ToUniversalTime()))
                    .Select(g => new DailyActiveUsersRow(g.Key, g.Select(e => e.VisitorId!.Value).Distinct().Count()))
                    .OrderBy(r => r.Day)
                    .ToList();

                int CountOf(string name) => events.Count(e => e.EventName == name);

                var topFiles = events
                    .Where(e => e.EventName == AnalyticsEvents.DownloadCompleted && !string.IsNullOrWhiteSpace(e.FileName))
                    .GroupBy(e => e.FileName!)
                    .Select(g => new TopFileRow(g.Key, g.Count()))
                    .OrderByDescending(r => r.DownloadCount)
                    .Take(10)
                    .ToList();

                var geo = events
                    .Where(e => !string.IsNullOrWhiteSpace(e.CountryCode))
                    .GroupBy(e => e.CountryCode!)
                    .Select(g => new CountryRow(g.Key, g.Count()))
                    .OrderByDescending(r => r.Count)
                    .Take(10)
                    .ToList();

                return new AnalyticsDashboardResponse(
                    daus,
                    new AnalyticsFunnelRow(
                        CountOf(AnalyticsEvents.Visitor),
                        CountOf(AnalyticsEvents.UploadStarted),
                        CountOf(AnalyticsEvents.UploadCompleted),
                        CountOf(AnalyticsEvents.DownloadCompleted)),
                    topFiles,
                    geo);
            },
            duration: TimeSpan.FromSeconds(30),
            token: ct);
    }
}
