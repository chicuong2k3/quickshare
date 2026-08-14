using Microsoft.Extensions.Options;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Reclaims R2 storage that no DB row references (issue #107): objects from uploads
/// that died mid-flight or rows deleted without cleanup, and multipart uploads whose
/// session is gone (ZombiePartSweeper only aborts uploads that still HAVE a session).
/// Dry-run by default — reports would-delete candidates until OrphanSweepDelete=true.
/// </summary>
public sealed class OrphanSweepJob(
    IServiceScopeFactory scopeFactory,
    IStorageProvider storage,
    IOptions<QuickShareOptions> options,
    ILogger<OrphanSweepJob> logger) : BackgroundService
{
    // First sweep soon after boot — a 24h delay-first loop would never run on
    // hosts that redeploy daily.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(15);
    private const int MaxLoggedKeysPerCycle = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Clamp: 0/negative config would spin a tight loop or make Task.Delay throw.
        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.OrphanSweepIntervalHours));
        var delay = InitialDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "OrphanSweepJob cycle failed"); }

            delay = interval;
        }
    }

    // internal: invoked directly by E2E tests (the timer loop is untestable).
    internal async Task SweepAsync(CancellationToken ct)
    {
        var opts = options.Value;
        var now = DateTime.UtcNow;
        var deleteMode = opts.OrphanSweepDelete;

        // List R2 FIRST, then snapshot DB references: an upload committed between the
        // two steps is in the DB snapshot and therefore excluded — no false positives.
        // Age guards clamped to at least 1 day / 1 hour: a negative value would put the
        // cutoff in the future and classify everything as stale.
        var staleObjects = await storage.ListObjectsAsync(
            R2ObjectKey.UploadsPrefix, now.AddDays(-Math.Max(1, opts.OrphanObjectMinAgeDays)), ct);
        var staleUploads = await storage.ListMultipartUploadsAsync(
            R2ObjectKey.UploadsPrefix, now.AddHours(-Math.Max(1, opts.OrphanMultipartMinAgeHours)), ct);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var referencedPaths = new HashSet<string>(
            await db.Deliveries.Where(d => d.StoragePath != null).Select(d => d.StoragePath!).ToListAsync(ct),
            StringComparer.Ordinal);
        referencedPaths.UnionWith(
            await db.UploadSessions.Where(s => s.StoragePath != null).Select(s => s.StoragePath!).ToListAsync(ct));

        var trackedUploadIds = new HashSet<string>(
            await db.UploadSessions.Where(s => s.R2UploadId != null).Select(s => s.R2UploadId!).ToListAsync(ct),
            StringComparer.Ordinal);

        // ponytail: full key + reference sets in memory — fine at this app's scale,
        // switch to per-page streaming if the bucket ever reaches millions of objects.
        var orphanObjects = staleObjects.Where(key => !referencedPaths.Contains(key)).ToList();
        var orphanUploads = staleUploads.Where(u => !trackedUploadIds.Contains(u.UploadId)).ToList();

        // Circuit breaker: an unexpectedly huge candidate list smells like a listing or
        // reference-query bug — never mass-delete on a spike, report instead (#89).
        if (deleteMode && orphanObjects.Count > opts.OrphanSweepMaxDeletesPerCycle)
        {
            logger.LogWarning(
                "OrphanSweep: {Count} candidates exceed MaxDeletesPerCycle={Max} — downgrading this cycle to dry-run",
                orphanObjects.Count, opts.OrphanSweepMaxDeletesPerCycle);
            deleteMode = false;
        }

        var deletedCount = 0;
        var loggedKeys = 0;

        foreach (var key in orphanObjects)
        {
            if (!deleteMode)
            {
                if (loggedKeys++ < MaxLoggedKeysPerCycle)
                    logger.LogInformation("OrphanSweep dry-run: would delete R2 object {Key}", key);
                continue;
            }

            try
            {
                await storage.DeleteObjectAsync(key, ct);
                deletedCount++;
                logger.LogInformation("OrphanSweep deleted R2 object {Key}", key);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OrphanSweep failed to delete R2 object {Key}", key);
            }
        }

        if (!deleteMode && orphanObjects.Count > MaxLoggedKeysPerCycle)
            logger.LogInformation("OrphanSweep dry-run: … and {Count} more candidates not listed",
                orphanObjects.Count - MaxLoggedKeysPerCycle);

        // Session-less multipart uploads can never complete — aborting is safe in both
        // modes (per approved spec). A NoSuchUpload failure here is usually the benign
        // race where the upload completed between listing and abort — warning, not error.
        var abortedCount = 0;
        foreach (var upload in orphanUploads)
        {
            try
            {
                await storage.AbortR2MultipartUploadAsync(upload.UploadId, upload.ObjectKey, ct);
                abortedCount++;
                logger.LogInformation("OrphanSweep aborted multipart upload {UploadId} ({Key}, initiated {Initiated})",
                    upload.UploadId, upload.ObjectKey, upload.Initiated);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OrphanSweep could not abort multipart upload {UploadId} ({Key})",
                    upload.UploadId, upload.ObjectKey);
            }
        }

        logger.LogInformation(
            "OrphanSweep cycle done: {OrphanCount} orphan objects found, {DeletedCount} deleted ({Mode}), {UploadCount} multipart candidates, {AbortedCount} aborted",
            orphanObjects.Count, deletedCount, deleteMode ? "delete" : "dry-run", orphanUploads.Count, abortedCount);
    }
}
