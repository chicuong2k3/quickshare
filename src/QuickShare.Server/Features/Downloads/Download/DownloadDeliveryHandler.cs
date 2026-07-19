using LightResults;
using System.Security.Cryptography;

namespace QuickShare.Server.Features.Downloads.Download;

public record DownloadDeliveryQuery(string ShareCode);

public sealed class DownloadDeliveryHandler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    private const int MaxRetries = 3;

    public async Task HandleAsync(DownloadDeliveryQuery query, AppDbContext db, CancellationToken ct)
    {
        var httpContext = httpContextAccessor.HttpContext!;
        var response = httpContext.Response;

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == query.ShareCode, ct);

        if (delivery is null)
        {
            response.StatusCode = 404;
            return;
        }

        if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
        {
            response.StatusCode = 410;
            return;
        }

        if (delivery.DownloadCount >= delivery.MaxDownloads)
        {
            response.StatusCode = 410;
            return;
        }

        var chunks = await db.Chunks
            .Where(c => c.DeliveryId == delivery.Id)
            .OrderBy(c => c.Index)
            .Select(c => new { c.StorageRef, c.Sha256 })
            .ToListAsync(ct);

        response.ContentType = delivery.ContentType;
        response.Headers.ContentDisposition = $"attachment; filename=\"{delivery.FileName}\"";

        long totalBytes = 0;

        foreach (var chunk in chunks)
        {
            byte[]? data = null;
            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    data = await storage.GetChunkAsync(chunk.StorageRef, ct);
                    var computedHash = Convert.ToHexString(SHA256.HashData(data));
                    if (computedHash == chunk.Sha256)
                        break;
                    if (attempt == MaxRetries - 1)
                    {
                        response.StatusCode = 500;
                        return;
                    }
                }
                catch (FileNotFoundException)
                {
                    if (attempt == MaxRetries - 1)
                    {
                        response.StatusCode = 500;
                        return;
                    }
                }
            }

            await response.Body.WriteAsync(data!, ct);
            totalBytes += data!.Length;
        }

        // Update counter + tracking in a fresh scope (avoid blocking the response)
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = httpContext.RequestServices.CreateAsyncScope();
                var updateDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var updateCtx = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

                var entity = await updateDb.Deliveries.FindAsync([delivery.Id]);
                if (entity is not null)
                {
                    entity.DownloadCount++;
                    entity.EgressBytes += totalBytes;

                    updateDb.DownloadTrackings.Add(new DownloadTracking
                    {
                        Id = Guid.CreateVersion7(),
                        DeliveryId = delivery.Id,
                        IpAddress = updateCtx.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "",
                        DownloadedAt = DateTime.UtcNow
                    });

                    await updateDb.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch { /* counter update failure shouldn't break the download */ }
        }, CancellationToken.None);
    }
}
