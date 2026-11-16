namespace QuickShare.Features.Reports;

public record ListReportsQuery : IRequest<List<ReportListItem>>;

public sealed class ListReportsHandler(AppDbContext db) : IRequestHandler<ListReportsQuery, List<ReportListItem>>
{
    public async ValueTask<List<ReportListItem>> Handle(ListReportsQuery query, CancellationToken cancellationToken)
    {
        return await db.AbuseReports
            .AsNoTracking()
            .Where(r => r.Status == AbuseReportStatus.Open)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReportListItem(
                r.Id,
                r.DeliveryId,
                r.Category,
                r.Note,
                r.CreatedAt
            ))
            .ToListAsync(cancellationToken);
    }
}
