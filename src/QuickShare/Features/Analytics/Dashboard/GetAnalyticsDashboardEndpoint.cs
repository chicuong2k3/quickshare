using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Analytics.Dashboard;

/// <summary>Admin-only API backing the /admin/analytics founder dashboard (issue #24).</summary>
public sealed class GetAnalyticsDashboardEndpoint : Endpoint<AnalyticsDashboardQuery>
{
    public override void Configure()
    {
        Get(AnalyticsConstants.AdminAnalyticsRoute);
        Description(b => b.WithTags(ApiTags.Admin));
        Summary(s =>
        {
            s.Summary = "Founder analytics dashboard data";
            s.Description = "Admin only. DAU, funnel, top files, and geographic spread over the requested window (default 30 days).";
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
