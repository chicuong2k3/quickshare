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

    /// <summary>
    /// Whether a key sits under one delivery's own folder (issue #79).
    ///
    /// Multi-file commits name their object keys in the request, so this is the check that stops
    /// a caller stapling somebody else's object — or an avatar — onto a share code they control.
    /// The delivery id segment is what makes a key theirs, so that is what gets compared, rather
    /// than a prefix string that a crafted name could imitate.
    /// </summary>
    public static bool BelongsTo(string? objectKey, Guid deliveryId)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || !objectKey.StartsWith(UploadsPrefix, StringComparison.Ordinal))
            return false;

        var segments = objectKey.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // uploads/[region/]{userId}/{deliveryId}/{name} — the delivery id is the segment before
        // the file name, whether or not a region is present.
        return segments.Length >= 4
            && string.Equals(segments[^2], deliveryId.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }

    public static string ForAvatar(Guid userId, string extension)
    {
        return $"{AvatarsPrefix}{userId:N}{extension}";
    }
}
