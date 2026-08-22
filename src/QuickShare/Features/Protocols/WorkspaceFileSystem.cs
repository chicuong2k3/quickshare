using LightResults;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Protocols;

/// <summary>One row in a mounted collection: a folder, or a delivery presented as a file.</summary>
public sealed record MountEntry(
    bool IsCollection,
    string Name,
    long Size,
    DateTime ModifiedAt,
    string ContentType,
    Guid? FolderId,
    Guid? DeliveryId,
    string? ShareCode);

/// <summary>What a mounted path points at. Exactly one of the three is true.</summary>
public sealed record MountNode(bool IsRoot, WorkspaceFolder? Folder, Delivery? File)
{
    public bool IsCollection => IsRoot || Folder is not null;
    public Guid? FolderId => Folder?.Id;
}

/// <summary>
/// The workspace's deliveries and folders, presented as a filesystem (issues #195, #265).
///
/// One mapping, shared by every protocol surface, because the interesting decisions are about
/// *what a path means* rather than about WebDAV's XML or S3's signatures — and two protocols
/// that disagreed about which files exist would be two products.
///
/// The namespace is the **workspace**, never a personal pile. A mount is a team surface: shared
/// folders and the deliveries filed in them. Personal shares have no workspace and stay out of
/// it, or a colleague's key would list somebody's private history as a drive.
///
/// Reading here is *owner-side* access. A GET through a mount is the team looking at its own
/// files, so it never touches <c>DownloadCount</c>, never runs the recipient gates, and never
/// appears in the download stats — counting it would corrupt the number the sender actually
/// asked for, which is "how many times did the recipient fetch this".
/// </summary>
public sealed class WorkspaceFileSystem(
    AppDbContext db,
    IStorageProvider storage,
    CacheService cache,
    StorageRegionResolver regions,
    ILogger<WorkspaceFileSystem> logger)
{
    public const string DefaultContentType = "application/octet-stream";

    /// <summary>Splits a mount path into segments, dropping empties so "/a//b/" == "a/b".</summary>
    public static string[] Segments(string? path) =>
        (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // ── Reading ──

    /// <summary>
    /// Resolves a path to a folder or a file. Returns null when nothing is there, which every
    /// caller turns into a not-found — a mount must not distinguish "no such folder" from "no
    /// permission", because the workspace boundary is the permission.
    /// </summary>
    public async Task<MountNode?> ResolveAsync(Guid workspaceId, string[] segments, CancellationToken ct)
    {
        if (segments.Length == 0)
            return new MountNode(IsRoot: true, null, null);

        WorkspaceFolder? folder = null;
        Guid? parentId = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var name = segments[i];
            var candidate = await db.WorkspaceFolders
                .FirstOrDefaultAsync(f => f.WorkspaceId == workspaceId && f.ParentId == parentId && f.Name == name, ct);

            if (candidate is null)
            {
                // Only the last segment may be a file; a file in the middle of a path is a
                // client mistake, not a lookup to keep trying.
                if (i != segments.Length - 1)
                    return null;

                var file = await FindFileAsync(workspaceId, parentId, name, ct);
                return file is null ? null : new MountNode(false, null, file);
            }

            folder = candidate;
            parentId = candidate.Id;
        }

        return new MountNode(false, folder, null);
    }

    /// <summary>The contents of a collection, folders first, each name unique within it.</summary>
    public async Task<IReadOnlyList<MountEntry>> ListAsync(Guid workspaceId, Guid? folderId, CancellationToken ct)
    {
        var folders = await db.WorkspaceFolders
            .AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId && f.ParentId == folderId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

        var files = await FilesQuery(workspaceId, folderId)
            .AsNoTracking()
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

        var entries = folders
            .Select(f => new MountEntry(true, f.Name, 0, f.UpdatedAt ?? f.CreatedAt, DefaultContentType, f.Id, null, null))
            .ToList();

        var taken = new HashSet<string>(folders.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            entries.Add(new MountEntry(
                false,
                UniqueName(file, taken),
                file.TotalFileBytes > 0 ? file.TotalFileBytes : file.FileSize,
                file.UpdatedAt ?? file.CreatedAt,
                string.IsNullOrEmpty(file.ContentType) ? DefaultContentType : file.ContentType,
                null,
                file.Id,
                file.ShareCode));
        }

        return entries;
    }

    /// <summary>
    /// Every file in the workspace as a flat <c>folder/name</c> key list (issue #265).
    ///
    /// Object storage has no directories, so an S3 client asking for a bucket without a
    /// delimiter expects the whole tree at once. It is bounded by the folder depth cap (two) and
    /// the per-workspace folder limit, so "walk it" is a handful of queries rather than an
    /// unbounded crawl.
    /// </summary>
    public async Task<IReadOnlyList<(string Key, MountEntry Entry)>> ListAllAsync(
        Guid workspaceId, CancellationToken ct)
    {
        var flat = new List<(string, MountEntry)>();
        await WalkAsync(workspaceId, null, string.Empty, flat, ct);

        return flat;
    }

    private async Task WalkAsync(
        Guid workspaceId, Guid? folderId, string prefix, List<(string, MountEntry)> flat, CancellationToken ct)
    {
        foreach (var entry in await ListAsync(workspaceId, folderId, ct))
        {
            var key = prefix + entry.Name;

            if (entry.IsCollection)
                await WalkAsync(workspaceId, entry.FolderId, key + "/", flat, ct);
            else
                flat.Add((key, entry));
        }
    }

    /// <summary>
    /// Resolves a path, creating the folders along the way (issue #265).
    ///
    /// S3 has no <c>MKCOL</c>: a client writes <c>campaign/final/brief.pdf</c> and expects the
    /// path to exist afterwards. The depth cap still applies — a key nested deeper than the
    /// folder model allows is refused rather than flattened, because flattening would silently
    /// file somebody's object somewhere they did not ask for.
    /// </summary>
    public async Task<Result<Guid?>> EnsureFolderAsync(
        Guid workspaceId, string[] segments, Guid actorId, CancellationToken ct)
    {
        Guid? parentId = null;

        foreach (var name in segments)
        {
            var existing = await db.WorkspaceFolders
                .FirstOrDefaultAsync(f => f.WorkspaceId == workspaceId && f.ParentId == parentId && f.Name == name, ct);

            if (existing is not null)
            {
                parentId = existing.Id;
                continue;
            }

            var created = await CreateFolderAsync(workspaceId, parentId, name, actorId, ct);
            if (!created.IsSuccess(out var folder))
            {
                created.IsFailure(out var error);
                return Result.Failure<Guid?>(new LightResults.Error(error?.Message ?? ErrorCodes.FolderNotFound));
            }

            parentId = folder.Id;
        }

        return Result.Success(parentId);
    }

    /// <summary>The bytes of a delivery, or null when the object is gone.</summary>
    public Task<Stream?> OpenAsync(Delivery file, CancellationToken ct) =>
        string.IsNullOrEmpty(file.StoragePath)
            ? Task.FromResult<Stream?>(null)
            : storage.OpenReadAsync(file.StoragePath, ct);

    // ── Writing ──

    /// <summary>
    /// Stores a stream as a delivery in this collection, or replaces the delivery already
    /// carrying that name.
    ///
    /// Replacing rather than duplicating is both what a filesystem client means by PUT and what
    /// the product already calls "replace file, keep link" (#72): the share code, password,
    /// expiry and download cap survive, only the bytes change. Two files with one name in one
    /// folder would break every sync client that has ever existed.
    /// </summary>
    public async Task<Result<Delivery>> PutAsync(
        User owner, Guid workspaceId, Guid? folderId, string name, string? contentType, Stream content, long? declaredSize, CancellationToken ct)
    {
        var plan = owner.Plan;
        var cap = PlanTierLimits.GetMaxFileSize(plan);

        if (declaredSize is { } declared && cap is { } limit && declared > limit)
            return Result.Failure<Delivery>(new LightResults.Error(ErrorCodes.FileTooLarge));

        var defaults = folderId is { } id
            ? await FolderDefaults.ResolveAsync(db, workspaceId, id, ct)
            : null;

        if (folderId is not null && defaults is null)
            return Result.Failure<Delivery>(new LightResults.Error(ErrorCodes.FolderNotFound));

        // A folder whose policy is "everything here carries a password" cannot accept a mount
        // write: the protocol has no field for one, and inventing a password leaves nobody to
        // tell it to. Refused rather than filed without the protection the folder demands.
        if (defaults is { Locked: true, PasswordRequired: true })
            return Result.Failure<Delivery>(new LightResults.Error(ErrorCodes.FolderPasswordRequired));

        var existing = await FindFileAsync(workspaceId, folderId, name, ct);

        if (existing is not null && await IsLockedByOtherAsync(existing.Id, owner.Id, ct))
            return Result.Failure<Delivery>(new LightResults.Error(ErrorCodes.DeliveryLocked));

        var deliveryId = existing?.Id ?? Guid.CreateVersion7();
        // Replacing keeps the delivery's own region: swapping a file must not move the bytes to
        // another jurisdiction (issue #119).
        var region = existing?.StorageRegion ?? regions.Resolve(owner, plan, null);
        var objectKey = R2ObjectKey.For(owner.Id, existing is null ? deliveryId : Guid.CreateVersion7(), name, region);

        try
        {
            await storage.UploadObjectAsync(objectKey, content, contentType ?? DefaultContentType, ct);
        }
        catch (MountPayloadException)
        {
            // The bytes failed their own integrity check partway through. Whatever landed is
            // unauthenticated, so it does not stay — and the protocol layer, not this one, owns
            // saying so to the client.
            await TryDeleteAsync(objectKey, ct);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mount write failed for {Name} in workspace {WorkspaceId}", name, workspaceId);
            await TryDeleteAsync(objectKey, ct);
            return Result.Failure<Delivery>(new LightResults.Error(UploadErrors.R2Upstream + "write failed"));
        }

        var size = await storage.GetObjectSizeAsync(objectKey, ct);
        if (size < 0)
            size = declaredSize ?? 0;

        // The cap is enforced on what actually landed, not on what the client declared — a
        // chunked PUT has no Content-Length to check up front.
        if (cap is { } max && size > max)
        {
            await TryDeleteAsync(objectKey, ct);
            return Result.Failure<Delivery>(new LightResults.Error(ErrorCodes.FileTooLarge));
        }

        var quota = new QuotaService(db);
        var quotaCheck = await quota.CheckAsync(owner.Id, plan, size, ct);
        if (quotaCheck.IsExceeded)
        {
            await TryDeleteAsync(objectKey, ct);
            return Result.Failure<Delivery>(new LightResults.Error(ErrorCodes.MonthlyQuotaExceeded));
        }

        var delivery = existing is null
            ? await CreateAsync(owner, workspaceId, folderId, deliveryId, name, contentType, objectKey, size, region, defaults, ct)
            : await ReplaceAsync(existing, name, contentType, objectKey, size, ct);

        await quota.RecordAsync(owner.Id, plan, size, ct);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

        return Result.Success(delivery);
    }

    /// <summary>Moves a delivery to the trash (issue #99), honouring hold and checkout.</summary>
    public async Task<Result> DeleteAsync(Delivery file, Guid actorId, CancellationToken ct)
    {
        if (file.LegalHoldAt is not null)
            return Result.Failure(new LightResults.Error(ErrorCodes.LegalHoldActive));

        if (await IsLockedByOtherAsync(file.Id, actorId, ct))
            return Result.Failure(new LightResults.Error(ErrorCodes.DeliveryLocked));

        // Soft, like every other delete path: a mistaken `rm` on a mounted drive is exactly the
        // accident the 24-hour trash window exists for.
        file.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"download:info:{file.ShareCode}", ct);

        return Result.Success();
    }

    /// <summary>Creates a shared folder (issue #151). Depth and per-workspace limits still apply.</summary>
    public async Task<Result<WorkspaceFolder>> CreateFolderAsync(
        Guid workspaceId, Guid? parentId, string name, Guid actorId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > WorkspaceFolder.NameMaxLength)
            return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderNameInvalid));

        if (parentId is { } id)
        {
            var parent = await db.WorkspaceFolders.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == workspaceId, ct);

            if (parent is null)
                return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderNotFound));

            // Two levels, as everywhere else — a mount is not a way around the depth cap.
            if (parent.ParentId is not null)
                return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderTooDeep));
        }

        var taken = await db.WorkspaceFolders
            .AnyAsync(f => f.WorkspaceId == workspaceId && f.ParentId == parentId && f.Name == name, ct);

        if (taken)
            return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderNameTaken));

        var count = await db.WorkspaceFolders.CountAsync(f => f.WorkspaceId == workspaceId, ct);
        if (count >= WorkspaceFolder.MaxPerWorkspace)
            return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderLimitReached));

        var folder = new WorkspaceFolder
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            ParentId = parentId,
            Name = name.Trim(),
            CreatedById = actorId,
            CreatedAt = DateTime.UtcNow,
        };

        db.WorkspaceFolders.Add(folder);
        await db.SaveChangesAsync(ct);

        return Result.Success(folder);
    }

    // ── Internals ──

    private IQueryable<Delivery> FilesQuery(Guid workspaceId, Guid? folderId) =>
        db.Deliveries.Where(d =>
            d.WorkspaceId == workspaceId
            && d.FolderId == folderId
            && d.DeletedAt == null
            // A share still under review has not been published to anyone, including a drive:
            // the mount is a view of the workspace's files, not a way around the gate (#162).
            && d.Status != DeliveryStatus.PendingApproval);

    private async Task<Delivery?> FindFileAsync(Guid workspaceId, Guid? folderId, string name, CancellationToken ct)
    {
        var candidates = await FilesQuery(workspaceId, folderId).OrderBy(d => d.CreatedAt).ToListAsync(ct);
        var taken = await db.WorkspaceFolders
            .Where(f => f.WorkspaceId == workspaceId && f.ParentId == folderId)
            .Select(f => f.Name)
            .ToListAsync(ct);

        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);

        // Resolution runs the same naming the listing does, so a name a client just read back
        // is always a name it can fetch.
        return candidates.FirstOrDefault(file =>
            string.Equals(UniqueName(file, used), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The name a delivery is shown under. Duplicates get their share code before the
    /// extension: a folder is allowed to hold two files called <c>report.pdf</c>, a directory
    /// listing is not.
    /// </summary>
    private static string UniqueName(Delivery file, HashSet<string> used)
    {
        var name = string.IsNullOrWhiteSpace(file.FileName) ? file.ShareCode : file.FileName;

        if (used.Add(name))
            return name;

        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var disambiguated = $"{stem}-{file.ShareCode}{extension}";
        used.Add(disambiguated);

        return disambiguated;
    }

    private Task<bool> IsLockedByOtherAsync(Guid deliveryId, Guid actorId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.FileLocks.AnyAsync(l =>
            l.DeliveryId == deliveryId
            && l.ReleasedAt == null
            && l.ExpiresAt > now
            && l.LockedById != actorId, ct);
    }

    private async Task<Delivery> CreateAsync(
        User owner, Guid workspaceId, Guid? folderId, Guid deliveryId, string name, string? contentType,
        string objectKey, long size, string? region, WorkspaceFolder.EffectiveDefaults? defaults, CancellationToken ct)
    {
        var plan = owner.Plan;
        var window = defaults is { Locked: true, ExpiryHours: { } hours }
            ? TimeSpan.FromHours(hours)
            : PlanTierLimits.GetMaxExpiry(plan);

        var delivery = new Delivery
        {
            Id = deliveryId,
            SenderId = owner.Id,
            WorkspaceId = workspaceId,
            FolderId = folderId,
            StorageRegion = region,
            FileName = name,
            ContentType = string.IsNullOrEmpty(contentType) ? DefaultContentType : contentType,
            FileSize = size,
            TotalBytes = size,
            TotalFileBytes = size,
            ChunkCount = 1,
            ShareCode = ShareCodes.Generate(),
            MaxDownloads = PlanTierLimits.GetMaxDownloads(plan),
            StoragePath = objectKey,
            ExpiresAt = RetentionPolicy.Clamp(DateTime.UtcNow.Add(window), owner.RetentionDays),
            // Locked folder defaults are policy, and a protocol client has no form to override
            // them from (issue #245).
            PrivacyMode = defaults is { Locked: true, PrivacyMode: true },
            Status = defaults is { RequireApproval: true } ? DeliveryStatus.PendingApproval : DeliveryStatus.Active,
            ApprovalRequestedAt = defaults is { RequireApproval: true } ? DateTime.UtcNow : null,
            Source = DeliverySources.Mount,
        };

        db.Deliveries.Add(delivery);
        await Task.CompletedTask;

        return delivery;
    }

    private async Task<Delivery> ReplaceAsync(
        Delivery existing, string name, string? contentType, string objectKey, long size, CancellationToken ct)
    {
        var oldKey = existing.StoragePath;

        // FileSize/ContentType/TotalBytes are init-only on the entity; EF sets them through the
        // property, which is the same door the replace endpoint (#72) uses.
        await db.Deliveries
            .Where(d => d.Id == existing.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.StoragePath, objectKey)
                .SetProperty(d => d.FileName, name)
                .SetProperty(d => d.ContentType, string.IsNullOrEmpty(contentType) ? DefaultContentType : contentType)
                .SetProperty(d => d.FileSize, size)
                .SetProperty(d => d.TotalBytes, size)
                .SetProperty(d => d.TotalFileBytes, size)
                .SetProperty(d => d.ChunkCount, 1)
                .SetProperty(d => d.Sha256, (string?)null)
                .SetProperty(d => d.DownloadCount, 0)
                .SetProperty(d => d.FirstDownloadedAt, (DateTime?)null)
                .SetProperty(d => d.UpdatedAt, DateTime.UtcNow), ct);

        // Only once the new bytes are durably stored, never before.
        if (!string.IsNullOrEmpty(oldKey) && oldKey != objectKey)
            await TryDeleteAsync(oldKey, ct);

        return existing;
    }

    private async Task TryDeleteAsync(string objectKey, CancellationToken ct)
    {
        try
        {
            await storage.DeleteObjectAsync(objectKey, ct);
        }
        catch (Exception ex)
        {
            // Best-effort: the orphan sweeper (issue #107) owns whatever is left behind.
            logger.LogWarning(ex, "Mount cleanup failed for {ObjectKey}", objectKey);
        }
    }
}
