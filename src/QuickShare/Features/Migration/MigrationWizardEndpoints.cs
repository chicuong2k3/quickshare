using System.Text.Json;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Folders;

namespace QuickShare.Features.Migration;

public sealed class MigrationSourceItem
{
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long EstimatedBytes { get; set; }
}

public sealed class RunMigrationWizardRequest
{
    public string SourceProvider { get; set; } = MigrationImportJob.SourceWeTransfer;
    public string ConflictPolicy { get; set; } = MigrationImportJob.ConflictRename;
    public Guid? TargetFolderId { get; set; }
    public bool DryRun { get; set; }
    public List<MigrationSourceItem> Items { get; set; } = [];
}

public sealed record MigrationItemResultDto(
    string OriginalFileName,
    string ResolvedFileName,
    string Action,
    string? ShareCode,
    string? Error);

public sealed record MigrationWizardResponse(
    Guid JobId,
    string SourceProvider,
    string ConflictPolicy,
    bool DryRun,
    int TotalItems,
    int ImportedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<MigrationItemResultDto> Results);

/// <summary>
/// POST /api/migration/import — guided migration wizard from WeTransfer, Google Drive, S3, or URLs
/// with folder mapping, conflict resolution (skip/rename/replace), and dry-run preview (issue #272).
/// </summary>
public sealed class RunMigrationWizardEndpoint(
    AppDbContext db,
    IRemoteUrlFetcher fetcher,
    IStorageProvider storage) : Endpoint<RunMigrationWizardRequest, MigrationWizardResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.MigrationImportApi);
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(RunMigrationWizardRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            ThrowError(ErrorCodes.RequiresPaidPlan, StatusCodes.Status403Forbidden);
            return;
        }

        if (req.Items is null || req.Items.Count == 0 || req.Items.Count > MigrationImportJob.MaxItemsPerJob)
        {
            ThrowError(ErrorCodes.MigrationItemsInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var source = NormalizeSource(req.SourceProvider);
        var policy = NormalizePolicy(req.ConflictPolicy);

        var workspaceId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.RevokedAt == null && m.JoinedAt != null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        Guid? folderId = null;
        if (req.TargetFolderId is { } requestedFolderId && workspaceId is { } wsId)
        {
            var resolved = await FolderDefaults.ResolveAsync(db, wsId, requestedFolderId, ct);
            if (resolved is null)
            {
                ThrowError(ErrorCodes.FolderNotFound, StatusCodes.Status404NotFound);
                return;
            }
            folderId = requestedFolderId;
        }

        var existingDeliveries = await db.Deliveries
            .Where(d => d.SenderId == user.Id && d.DeletedAt == null && d.FolderId == folderId)
            .ToListAsync(ct);

        var results = new List<MigrationItemResultDto>(req.Items.Count);
        var imported = 0;
        var skipped = 0;
        var failed = 0;
        var totalImportedBytes = 0L;
        var maxFileSize = PlanTierLimits.GetMaxFileSize(user.Plan) ?? (10L * 1024 * 1024 * 1024);
        var quota = new QuotaService(db);

        foreach (var item in req.Items)
        {
            var rawName = string.IsNullOrWhiteSpace(item.FileName) ? "imported-file.bin" : item.FileName.Trim();
            var existing = existingDeliveries.FirstOrDefault(
                d => string.Equals(d.FileName, rawName, StringComparison.OrdinalIgnoreCase));

            var resolvedName = rawName;
            var action = "import";

            if (existing is not null)
            {
                if (policy == MigrationImportJob.ConflictSkip)
                {
                    skipped++;
                    results.Add(new MigrationItemResultDto(rawName, rawName, "skipped", existing.ShareCode, null));
                    continue;
                }

                if (policy == MigrationImportJob.ConflictRename)
                {
                    var ext = Path.GetExtension(rawName);
                    var stem = Path.GetFileNameWithoutExtension(rawName);
                    resolvedName = $"{stem} (imported){ext}";
                    action = "renamed";
                }
                else if (policy == MigrationImportJob.ConflictReplace)
                {
                    if (existing.LegalHoldAt is not null)
                    {
                        skipped++;
                        results.Add(new MigrationItemResultDto(rawName, rawName, "skipped_legal_hold", existing.ShareCode, ErrorCodes.LegalHoldActive));
                        continue;
                    }
                    action = "replaced";
                }
            }

            if (req.DryRun)
            {
                imported++;
                results.Add(new MigrationItemResultDto(rawName, resolvedName, $"dry_run_{action}", existing?.ShareCode, null));
                continue;
            }

            var fetchRes = await fetcher.FetchAsync(item.Url, maxFileSize, ct);
            if (fetchRes.Status != RemoteUrlStatus.Ok || fetchRes.Content is null)
            {
                failed++;
                results.Add(new MigrationItemResultDto(rawName, resolvedName, "failed", null, ErrorCodes.UrlFetchFailed));
                continue;
            }

            long sizeBytes;
            var deliveryId = existing is not null && action == "replaced" ? existing.Id : Guid.CreateVersion7();
            var objectKey = R2ObjectKey.For(user.Id, deliveryId, resolvedName);

            try
            {
                await storage.UploadObjectAsync(objectKey, fetchRes.Content, fetchRes.ContentType ?? "application/octet-stream", ct);
                sizeBytes = await storage.GetObjectSizeAsync(objectKey, ct);
                if (sizeBytes < 0) sizeBytes = item.EstimatedBytes > 0 ? item.EstimatedBytes : 4;
            }
            finally
            {
                if (fetchRes.Content is IDisposable disp) disp.Dispose();
            }

            if (existing is not null && action == "replaced")
            {
                existing.FileName = resolvedName;
                existing.TotalFileBytes = sizeBytes;
                imported++;
                results.Add(new MigrationItemResultDto(rawName, resolvedName, "replaced", existing.ShareCode, null));
            }
            else
            {
                var shareCode = ShareCodes.Generate();
                var delivery = new Delivery
                {
                    Id = deliveryId,
                    SenderId = user.Id,
                    WorkspaceId = workspaceId,
                    FolderId = folderId,
                    FileName = resolvedName,
                    ContentType = fetchRes.ContentType ?? "application/octet-stream",
                    FileSize = sizeBytes,
                    TotalBytes = sizeBytes,
                    TotalFileBytes = sizeBytes,
                    FileCount = 1,
                    ChunkCount = 1,
                    ShareCode = shareCode,
                    MaxDownloads = PlanTierLimits.GetMaxDownloads(user.Plan),
                    StoragePath = objectKey,
                    ExpiresAt = RetentionPolicy.Clamp(DateTime.UtcNow.Add(PlanTierLimits.GetMaxExpiry(user.Plan)), user.RetentionDays),
                    Status = DeliveryStatus.Active,
                    Source = DeliverySources.Url,
                };
                db.Deliveries.Add(delivery);
                existingDeliveries.Add(delivery);
                totalImportedBytes += sizeBytes;
                imported++;
                results.Add(new MigrationItemResultDto(rawName, resolvedName, action, shareCode, null));
            }
        }

        if (!req.DryRun && totalImportedBytes > 0)
        {
            await quota.RecordAsync(user.Id, user.Plan, totalImportedBytes, ct);
        }

        var job = new MigrationImportJob
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            WorkspaceId = workspaceId,
            TargetFolderId = folderId,
            SourceProvider = source,
            ConflictPolicy = policy,
            TotalItems = req.Items.Count,
            ImportedCount = imported,
            SkippedCount = skipped,
            FailedCount = failed,
            Status = req.DryRun
                ? MigrationImportJob.StatusDryRun
                : failed > 0 ? MigrationImportJob.StatusCompletedWithErrors : MigrationImportJob.StatusCompleted,
            SummaryJson = JsonSerializer.Serialize(results),
        };
        db.MigrationImportJobs.Add(job);

        if (!req.DryRun)
        {
            db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                WorkspaceId = workspaceId,
                EventType = SecurityEventType.MigrationImportCompleted,
                IpHash = string.Empty,
                UserAgentLabel = nameof(RunMigrationWizardEndpoint),
                MetadataJson = JsonSerializer.Serialize(new { job.Id, source, policy, imported, skipped, failed }),
            });
        }

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new MigrationWizardResponse(
            job.Id,
            source,
            policy,
            req.DryRun,
            req.Items.Count,
            imported,
            skipped,
            failed,
            results), ct);
    }

    private static string NormalizeSource(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        MigrationImportJob.SourceGoogleDrive => MigrationImportJob.SourceGoogleDrive,
        MigrationImportJob.SourceS3 => MigrationImportJob.SourceS3,
        MigrationImportJob.SourceUrls => MigrationImportJob.SourceUrls,
        _ => MigrationImportJob.SourceWeTransfer,
    };

    private static string NormalizePolicy(string? raw) => (raw ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        MigrationImportJob.ConflictSkip => MigrationImportJob.ConflictSkip,
        MigrationImportJob.ConflictReplace => MigrationImportJob.ConflictReplace,
        _ => MigrationImportJob.ConflictRename,
    };
}
