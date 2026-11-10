namespace QuickShare.Features.Uploads.Shared;

/// <summary>
/// Content-addressed storage deduplication (issue #197).
/// Scoped strictly within the same tenant (workspace or personal account) so deduplication
/// never acts as a cross-tenant file-existence oracle (#190), and bypassed in privacy mode (#187).
/// </summary>
public static class DeduplicationService
{
    public static string? BuildScopeKey(Guid? workspaceId, Guid? userId, bool privacyMode)
    {
        if (privacyMode)
        {
            return null;
        }

        if (workspaceId is { } wsId)
        {
            return $"ws:{wsId:N}";
        }

        if (userId is { } uId)
        {
            return $"user:{uId:N}";
        }

        return null;
    }

    public sealed record DedupOutcome(bool ReusedExisting, string CanonicalStoragePath);

    /// <summary>
    /// Checks if an identical blob (same tenant ScopeKey, Sha256, FileSize, StorageRegion) is already stored.
    /// If found, increments RefCount, deletes the duplicate newly-uploaded object, and returns the canonical path.
    /// Otherwise registers the new object as a canonical blob with RefCount = 1.
    /// </summary>
    public static async Task<DedupOutcome> RegisterOrReuseAsync(
        AppDbContext db,
        IStorageProvider storage,
        string? scopeKey,
        string? sha256,
        long fileSize,
        string? storageRegion,
        string uploadedObjectKey,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(scopeKey) || string.IsNullOrEmpty(sha256) || fileSize <= 0)
        {
            return new DedupOutcome(false, uploadedObjectKey);
        }

        var existing = await db.DeduplicatedBlobs
            .FirstOrDefaultAsync(b => b.ScopeKey == scopeKey
                && b.Sha256 == sha256
                && b.FileSize == fileSize
                && b.StorageRegion == storageRegion, ct);

        if (existing is not null && existing.RefCount > 0 && !string.Equals(existing.StoragePath, uploadedObjectKey, StringComparison.Ordinal))
        {
            existing.RefCount++;
            try
            {
                await storage.DeleteObjectsAsync(uploadedObjectKey, ct);
            }
            catch
            {
                // Best-effort duplicate cleanup; orphan sweep handles any stragglers.
            }

            return new DedupOutcome(true, existing.StoragePath);
        }

        if (existing is not null)
        {
            existing.RefCount = Math.Max(1, existing.RefCount + 1);
            return new DedupOutcome(false, existing.StoragePath);
        }

        db.DeduplicatedBlobs.Add(new DeduplicatedBlob
        {
            Id = Guid.CreateVersion7(),
            ScopeKey = scopeKey,
            Sha256 = sha256,
            StoragePath = uploadedObjectKey,
            StorageRegion = storageRegion,
            FileSize = fileSize,
            RefCount = 1,
        });

        return new DedupOutcome(false, uploadedObjectKey);
    }

    /// <summary>
    /// Decrements reference count for a storage path. Only deletes the physical object from R2/S3
    /// when no remaining live references exist (issue #197).
    /// </summary>
    public static async Task ReleaseOrDeleteAsync(
        AppDbContext db,
        IStorageProvider storage,
        string? storagePath,
        bool isDeduplicated,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(storagePath))
        {
            return;
        }

        var blob = await db.DeduplicatedBlobs
            .FirstOrDefaultAsync(b => b.StoragePath == storagePath, ct);

        if (blob is not null)
        {
            blob.RefCount--;
            if (blob.RefCount > 0)
            {
                return;
            }

            db.DeduplicatedBlobs.Remove(blob);
        }

        await storage.DeleteObjectsAsync(storagePath, ct);
    }
}
