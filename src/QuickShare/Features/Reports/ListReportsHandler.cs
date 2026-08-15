namespace QuickShare.Features.Reports;

public record ListReportsQuery;

public sealed class ListReportsHandler
{
    public async Task<List<ReportListItem>> Handle(ListReportsQuery query, AppDbContext db, CancellationToken ct)
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
            .ToListAsync(ct);
    }
}
