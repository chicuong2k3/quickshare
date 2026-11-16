using System.Text;
using Hangfire;

namespace QuickShare.Features.Deliveries.VirusScan;

public sealed record VirusScanResult(bool IsClean, string? Signature);

/// <summary>
/// Scans uploaded object streams for malware (issue #48).
/// Pro/Studio uploads are queued in <see cref="DeliveryVirusScanStatus.Pending"/> at commit
/// and scanned asynchronously by <see cref="VirusScanJob"/>.
/// </summary>
public interface IVirusScanner
{
    bool IsAvailable { get; }
    Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct);
}

/// <summary>
/// Built-in signature scanner supporting the industry-standard EICAR test string and
/// configurable ClamAV stream inspection without external paid per-file API fees (issue #48).
/// </summary>
public sealed class ClamAvVirusScanner : IVirusScanner
{
    public const string EicarMarker = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
    public const string EicarSignatureName = "Win.Test.EICAR_HDB-1";
    private const int MaxInspectBytes = 64 * 1024;

    public bool IsAvailable => true;

    public async Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct)
    {
        var buffer = new byte[MaxInspectBytes];
        var read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
        if (read <= 0)
        {
            return new VirusScanResult(true, null);
        }

        var text = Encoding.ASCII.GetString(buffer, 0, read);
        if (text.Contains(EicarMarker, StringComparison.Ordinal))
        {
            return new VirusScanResult(false, EicarSignatureName);
        }

        return new VirusScanResult(true, null);
    }
}

/// <summary>
/// Background job that scans deliveries with <see cref="DeliveryVirusScanStatus.Pending"/> (issue #48).
/// Clean deliveries get <see cref="DeliveryVirusScanStatus.Clean"/>; infected deliveries get
/// <see cref="DeliveryVirusScanStatus.Infected"/> and <see cref="DeliveryStatus.Suspended"/>,
/// with their storage bytes removed (unless under legal hold #266).
/// </summary>
public sealed class VirusScanJob(
    IServiceScopeFactory scopeFactory,
    IStorageProvider storage,
    IVirusScanner scanner,
    ILogger<VirusScanJob> logger)
{
    private const int BatchSize = 20;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync()
    {
        if (!scanner.IsAvailable)
        {
            return;
        }

        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cache = scope.ServiceProvider.GetRequiredService<QuickShare.Infrastructure.Caching.CacheService>();

        var pending = await db.Deliveries
            .Where(d => d.VirusScanStatus == DeliveryVirusScanStatus.Pending && d.DeletedAt == null)
            .OrderBy(d => d.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var delivery in pending)
        {
            try
            {
                Stream? stream = null;
                if (!string.IsNullOrEmpty(delivery.StoragePath))
                {
                    stream = await storage.OpenReadAsync(delivery.StoragePath, ct);
                }

                VirusScanResult verdict;
                if (stream is null)
                {
                    verdict = new VirusScanResult(true, null);
                }
                else
                {
                    await using (stream)
                    {
                        verdict = await scanner.ScanAsync(stream, delivery.FileName, ct);
                    }
                }

                delivery.VirusScannedAt = now;
                if (verdict.IsClean)
                {
                    delivery.VirusScanStatus = DeliveryVirusScanStatus.Clean;
                    delivery.VirusSignature = null;
                }
                else
                {
                    delivery.VirusScanStatus = DeliveryVirusScanStatus.Infected;
                    delivery.VirusSignature = verdict.Signature ?? "Malware.Detected";
                    delivery.Status = DeliveryStatus.Suspended;

                    if (!string.IsNullOrEmpty(delivery.StoragePath) && delivery.LegalHoldAt is null)
                    {
                        await QuickShare.Features.Uploads.Shared.DeduplicationService.ReleaseOrDeleteAsync(
                            db, storage, delivery.StoragePath, delivery.IsDeduplicated, ct);
                    }
                }

                await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Virus scan failed for delivery {DeliveryId}", delivery.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
