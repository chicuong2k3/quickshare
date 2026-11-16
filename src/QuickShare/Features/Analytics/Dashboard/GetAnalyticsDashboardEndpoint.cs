using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Analytics.Dashboard;

/// <summary>Admin-only API backing the /admin/analytics founder dashboard (issues #24, #303).</summary>
public sealed class GetAnalyticsDashboardEndpoint : Endpoint<AnalyticsDashboardQuery>
{
    public override void Configure()
    {
        Get(AnalyticsConstants.AdminAnalyticsRoute);
        Description(b => b.WithTags(ApiTags.Admin));
        Summary(s =>
        {
            s.Summary = "Founder analytics dashboard data";
            s.Description = "Admin only. DAU, funnel, cohort retention, top files, and geographic spread over the requested window (default 30 days).";
        });
    }

    public override async Task HandleAsync(AnalyticsDashboardQuery req, CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", 403);
            return;
        }

        var data = await Resolve<IMediator>().Send<AnalyticsDashboardResponse>(req, ct);
        await Send.OkAsync(data, ct);
    }
}

/// <summary>GET /api/admin/analytics/export.csv — CSV export of funnel and cohort retention curves (issue #303).</summary>
public sealed class ExportAnalyticsDashboardCsvEndpoint : Endpoint<AnalyticsDashboardQuery>
{
    public override void Configure()
    {
        Get(RoutePaths.AdminAnalyticsCsvRoute);
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(AnalyticsDashboardQuery req, CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var data = await Resolve<IMediator>().Send<AnalyticsDashboardResponse>(req, ct);
        var csv = AnalyticsDashboardHandler.ToCsv(data);
        HttpContext.Response.Headers.ContentDisposition = "attachment; filename=\"analytics-cohorts.csv\"";
        await Send.StringAsync(csv, StatusCodes.Status200OK, "text/csv; charset=utf-8", ct);
    }
}
