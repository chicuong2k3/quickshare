using System.Globalization;
using System.Text;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Analytics.Dashboard;

public record AnalyticsDashboardQuery(DateOnly? From = null, DateOnly? To = null, string? Plan = null) : IRequest<AnalyticsDashboardResponse>;

public sealed record DailyActiveUsersRow(DateOnly Day, int Count);

public sealed record AnalyticsFunnelRow(
    int Visitor,
    int UploadStarted,
    int UploadCompleted,
    int DownloadCompleted,
    int Signup = 0,
    int TrialStarted = 0);

public sealed record TopFileRow(string FileName, int DownloadCount);

public sealed record CountryRow(string? CountryCode, int Count);

public sealed record CohortRetentionRow(
    DateOnly CohortWeekStart,
    int CohortSize,
    int RetainedWeek1,
    int RetainedWeek4,
    int RetainedWeek8,
    int Week1Percent,
    int Week4Percent,
    int Week8Percent);

public sealed record AnalyticsDashboardResponse(
    IReadOnlyList<DailyActiveUsersRow> DailyActiveUsers,
    AnalyticsFunnelRow Funnel,
    IReadOnlyList<TopFileRow> TopFiles,
    IReadOnlyList<CountryRow> GeographicSpread,
    IReadOnlyList<CohortRetentionRow>? Cohorts = null);

/// <summary>
/// Founder dashboard aggregates (issues #24, #303): DAU, funnel, cohort retention, top files, geo spread.
/// Anonymous by design — DAU and cohort curves count distinct qs_visitor cookie ids, never users.
/// </summary>
public sealed class AnalyticsDashboardHandler(AppDbContext db, CacheService cache)
    : IRequestHandler<AnalyticsDashboardQuery, AnalyticsDashboardResponse>
{
    public async ValueTask<AnalyticsDashboardResponse> Handle(AnalyticsDashboardQuery query, CancellationToken cancellationToken)
    {
        var to = query.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var from = query.From ?? to.AddDays(-29);
        var toUtc = to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(1);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var planFilter = string.IsNullOrWhiteSpace(query.Plan) ? null : query.Plan.Trim().ToLowerInvariant();

        return await cache.GetOrSetAsync(
            $"analytics:dashboard:{from:yyyyMMdd}:{to:yyyyMMdd}:{planFilter ?? "all"}",
            async ct2 =>
            {
                // Pull only the columns the dashboard needs; founder-scale volume
                // aggregates fine in memory and stays provider-agnostic (SQLite tests).
                var baseQuery = db.AnalyticsEvents
                    .AsNoTracking()
                    .Where(e => e.CreatedAt >= fromUtc && e.CreatedAt < toUtc);

                if (planFilter is not null)
                {
                    baseQuery = baseQuery.Where(e => e.Plan == planFilter);
                }

                var events = await baseQuery
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

                // Cohort retention (issue #303): group visitors by the Monday of their first-seen week,
                // then measure how many returned in week +1 (days 7..13), week +4 (days 28..34), and week +8 (days 56..62).
                var visitorEvents = events
                    .Where(e => e.VisitorId.HasValue)
                    .Select(e => new { VisitorId = e.VisitorId!.Value, Day = DateOnly.FromDateTime(e.CreatedAt.ToUniversalTime()) })
                    .ToList();

                var cohorts = visitorEvents
                    .GroupBy(x => x.VisitorId)
                    .Select(g =>
                    {
                        var days = g.Select(x => x.Day).Distinct().OrderBy(d => d).ToList();
                        var firstDay = days[0];
                        var offset = ((int)firstDay.DayOfWeek + 6) % 7; // Monday = 0
                        var cohortMonday = firstDay.AddDays(-offset);
                        return new { VisitorId = g.Key, CohortWeek = cohortMonday, FirstDay = firstDay, Days = days };
                    })
                    .GroupBy(v => v.CohortWeek)
                    .OrderBy(g => g.Key)
                    .Select(g =>
                    {
                        var size = g.Count();
                        var w1 = g.Count(v => v.Days.Any(d => d.DayNumber - v.FirstDay.DayNumber is >= 7 and < 14));
                        var w4 = g.Count(v => v.Days.Any(d => d.DayNumber - v.FirstDay.DayNumber is >= 28 and < 35));
                        var w8 = g.Count(v => v.Days.Any(d => d.DayNumber - v.FirstDay.DayNumber is >= 56 and < 63));
                        int Pct(int n) => size == 0 ? 0 : (int)Math.Round(n * 100.0 / size, MidpointRounding.AwayFromZero);
                        return new CohortRetentionRow(g.Key, size, w1, w4, w8, Pct(w1), Pct(w4), Pct(w8));
                    })
                    .ToList();

                return new AnalyticsDashboardResponse(
                    daus,
                    new AnalyticsFunnelRow(
                        CountOf(AnalyticsEvents.Visitor),
                        CountOf(AnalyticsEvents.UploadStarted),
                        CountOf(AnalyticsEvents.UploadCompleted),
                        CountOf(AnalyticsEvents.DownloadCompleted),
                        CountOf(AnalyticsEvents.Signup),
                        CountOf(AnalyticsEvents.TrialStarted)),
                    topFiles,
                    geo,
                    cohorts);
            },
            duration: TimeSpan.FromSeconds(30),
            token: cancellationToken);
    }

    public static string ToCsv(AnalyticsDashboardResponse data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("section,key,metric1,metric2,metric3,metric4");
        sb.AppendLine(CultureInfo.InvariantCulture, $"funnel,steps,{data.Funnel.Visitor},{data.Funnel.Signup},{data.Funnel.UploadStarted},{data.Funnel.UploadCompleted}");
        if (data.Cohorts is not null)
        {
            foreach (var c in data.Cohorts)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"cohort,{c.CohortWeekStart:yyyy-MM-dd},{c.CohortSize},{c.Week1Percent}%,{c.Week4Percent}%,{c.Week8Percent}%");
            }
        }
        return sb.ToString();
    }
}
