namespace QuickShare.Infrastructure.Storage;

public static class R2ObjectKey
{
    /// <summary>All upload keys live under this prefix — the orphan sweep never leaves it.</summary>
    public const string UploadsPrefix = "uploads/";

    /// <summary>All avatar keys live under this prefix.</summary>
    public const string AvatarsPrefix = "avatars/";

    /// <summary>
    /// Upload key, optionally pinned to a storage region (issue #119).
    ///
    /// The region segment sits directly under <see cref="UploadsPrefix"/> so the orphan sweep
    /// still scans one namespace, and so the storage provider can find the right bucket from
    /// the key alone. A key without a region — everything written before residency existed —
    /// keeps its original shape, and the user id in that slot is 32 hex characters, which can
    /// never be mistaken for a region code.
    /// </summary>
    public static string For(Guid userId, Guid deliveryId, string fileName, string? region = null)
    {
        var safeName = Uri.EscapeDataString(fileName);
        var prefix = StorageRegions.IsValidCode(region)
            ? $"{UploadsPrefix}{region}/"
            : UploadsPrefix;

        return $"{prefix}{userId:N}/{deliveryId:N}/{safeName}";
    }

    public static string ForAvatar(Guid userId, string extension)
    {
        return $"{AvatarsPrefix}{userId:N}{extension}";
    }
}
