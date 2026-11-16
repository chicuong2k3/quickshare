using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Reports;

public static class ReportResolution
{
    public static async Task<bool> ResolveAsync(Guid reportId, string action, AppDbContext db, CacheService cache, CancellationToken ct)
    {
        var report = await db.AbuseReports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null)
        {
            return false;
        }

        report.Status = AbuseReportStatus.Resolved;
        report.ResolvedAt = DateTime.UtcNow;
        report.Resolution = action;

        var delivery = await db.Deliveries.FindAsync([report.DeliveryId], ct);
        if (action == ReportAction.Suspend && delivery is not null)
        {
            delivery.Status = DeliveryStatus.Suspended;
        }

        await db.SaveChangesAsync(ct);
        if (delivery is not null)
        {
            await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);
        }

        return true;
    }
}
