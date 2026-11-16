using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Reports;

/// <summary>
/// Admin resolution of an abuse report (issue #53). <c>resolve</c> closes the
/// report without action; <c>suspend</c> additionally marks the delivery
/// Suspended so it becomes undownloadable.
/// </summary>
public sealed class ResolveReportEndpoint : Endpoint<ResolveReportRequest>
{
    private const string ReportIdRouteKey = "id";

    public override void Configure()
    {
        Post("/api/admin/reports/{id}/resolve");
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(ResolveReportRequest req, CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", 403);
            return;
        }

        var reportId = Route<Guid>(ReportIdRouteKey);
        var resolved = await ReportResolution.ResolveAsync(
            reportId, req.Action, Resolve<AppDbContext>(),
            Resolve<QuickShare.Infrastructure.Caching.CacheService>(), ct);
        if (!resolved)
        {
            ThrowError("Report not found", 404);
            return;
        }

        await Send.OkAsync(new { resolved = true }, ct);
    }
}

public class ResolveReportRequest
{
    public string Action { get; set; } = string.Empty;
}

public static class ReportAction
{
    public const string Resolve = "resolve";
    public const string Suspend = "suspend";
}
