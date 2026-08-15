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
        var db = Resolve<AppDbContext>();

        var report = await db.AbuseReports
            .FirstOrDefaultAsync(r => r.Id == reportId, ct);

        if (report is null)
        {
            ThrowError("Report not found", 404);
            return;
        }

        report.Status = AbuseReportStatus.Resolved;
        report.ResolvedAt = DateTime.UtcNow;
        report.Resolution = req.Action;

        var delivery = await db.Deliveries.FindAsync([report.DeliveryId], ct);

        if (req.Action == ReportAction.Suspend && delivery is not null)
            delivery.Status = DeliveryStatus.Suspended;

        await db.SaveChangesAsync(ct);

        if (delivery is not null)
        {
            // Delivery status changes are user-visible — drop the cached download info.
            await Resolve<QuickShare.Infrastructure.Caching.CacheService>()
                .RemoveAsync($"download:info:{delivery.ShareCode}", ct);
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
