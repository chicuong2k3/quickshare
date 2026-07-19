using LightResults;

namespace QuickShare.Server.Features.Reports;

public record ReportDeliveryCommand(string ShareCode, string Reason, string? Message);

public sealed class ReportDeliveryHandler(IStorageProvider storage)
{
    public async Task<Result<string>> Handle(ReportDeliveryCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.ShareCode, ct);

        if (delivery is null)
            return Result.Failure<string>(new LightResults.Error("not_found"));

        // Takedown: delete storage chunks, then cascade-delete delivery
        var chunkRefs = await db.Chunks.Where(c => c.DeliveryId == delivery.Id).Select(c => c.StorageRef).ToArrayAsync(ct);
        await storage.DeleteChunksAsync(chunkRefs, ct);
        db.Deliveries.Remove(delivery);
        await db.SaveChangesAsync(ct);

        return Result.Success("Thank you. This delivery has been reported and will be reviewed.");
    }
}
