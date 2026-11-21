namespace QuickShare.Infrastructure.Storage;

public static class R2ObjectKey
{
    /// <summary>All upload keys live under this prefix — the orphan sweep never leaves it.</summary>
    public const string UploadsPrefix = "uploads/";

    /// <summary>All avatar keys live under this prefix.</summary>
    public const string AvatarsPrefix = "avatars/";

    public static string For(Guid userId, Guid deliveryId, string fileName)
    {
        var safeName = Uri.EscapeDataString(fileName);
        return $"{UploadsPrefix}{userId:N}/{deliveryId:N}/{safeName}";
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
