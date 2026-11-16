using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Reports;

/// <summary>Admin-only list of open abuse reports (issue #53).</summary>
public sealed class ListReportsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/admin/reports");
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", 403);
            return;
        }

        var db = Resolve<AppDbContext>();
        var reports = await Resolve<IMediator>().Send<List<ReportListItem>>(new ListReportsQuery(), ct);

        await Send.OkAsync(reports, ct);
    }
}

public record ReportListItem(Guid Id, Guid DeliveryId, string Category, string? Note, DateTime CreatedAt);
