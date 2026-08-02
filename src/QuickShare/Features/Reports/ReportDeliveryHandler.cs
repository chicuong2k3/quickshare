using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Reports;

public record ReportDeliveryCommand(string ShareCode, string Reason, string? Message);
public sealed class ReportDeliveryHandler(IStorageProvider storage)
{
    public async Task<Result<string>> Handle(ReportDeliveryCommand cmd, AppDbContext db, CacheService cache, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.ShareCode, ct);

        if (delivery is null)
            return Result.Failure<string>(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.StoragePath is not null)
        {
            await storage.DeleteObjectsAsync(delivery.StoragePath, ct);
        }

        delivery.Status = "Takedown";
        await db.SaveChangesAsync(ct);

        await cache.RemoveAsync($"download:info:{cmd.ShareCode}", ct);

        return Result.Success("Thank you. This delivery has been reported and will be reviewed.");
    }
}
