using System.Text.RegularExpressions;

namespace QuickShare.Infrastructure.Storage;

/// <summary>
/// One storage location a delivery can be pinned to (issue #119).
///
/// A region is a bucket, optionally at its own endpoint. Bucket-only covers R2, where buckets
/// carry a location hint; the endpoint override covers AWS and multi-site MinIO, where the
/// region genuinely is a different host. Without the endpoint case this would not be residency
/// at all for those deployments — it would be one datacentre with several folder names.
/// </summary>
public sealed class StorageRegionOption
{
    public const int CodeMaxLength = 12;

    /// <summary>Short stable identifier — <c>eu</c>, <c>us</c>, <c>apac</c>. Appears in object keys.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>What a sender sees, e.g. "European Union (Frankfurt)".</summary>
    public string Label { get; init; } = string.Empty;

    public string BucketName { get; init; } = string.Empty;

    /// <summary>Optional per-region endpoint. Empty means the default object-store endpoint.</summary>
    public string Endpoint { get; init; } = string.Empty;

    public bool IsConfigured =>
        StorageRegions.IsValidCode(Code) && !string.IsNullOrEmpty(BucketName);
}

/// <summary>
/// Rules for region codes and for finding one inside an object key (issue #119).
///
/// Routing is derived from the key rather than passed as a parameter through
/// <see cref="IStorageProvider"/>. That is a deliberate trade. The alternative — a region
/// argument on ten methods and every call site — is more explicit, but it also means the region
/// has to be threaded correctly through every future path that touches an object, and a path
/// that forgets it reads from the wrong bucket and reports "not found" for a file that exists.
///
/// The key already travels with the delivery everywhere (<c>Delivery.StoragePath</c>), so
/// deriving the bucket from it means an object can always be found from the one thing that is
/// always present. Keys written before this feature have a user GUID where the region segment
/// now sits, and a 32-character hex string can never match a region code, so they resolve to
/// the default bucket exactly as they did before.
/// </summary>
public static partial class StorageRegions
{
    /// <summary>Lowercase letters and digits only, so a code can never be mistaken for a GUID segment.</summary>
    [GeneratedRegex("^[a-z0-9]{2,12}$")]
    private static partial Regex CodePattern();

    public static bool IsValidCode(string? code) =>
        !string.IsNullOrEmpty(code) && CodePattern().IsMatch(code);

    /// <summary>
    /// The region segment of an upload key, or null when there is none. Only the
    /// <c>uploads/</c> namespace is region-partitioned; avatars and brand assets are small,
    /// non-content objects that stay in the default bucket.
    /// </summary>
    public static string? FromKey(string? objectKey)
    {
        if (string.IsNullOrEmpty(objectKey) || !objectKey.StartsWith(R2ObjectKey.UploadsPrefix, StringComparison.Ordinal))
            return null;

        var rest = objectKey.AsSpan(R2ObjectKey.UploadsPrefix.Length);
        var slash = rest.IndexOf('/');
        if (slash <= 0)
            return null;

        var candidate = rest[..slash].ToString();
        return IsValidCode(candidate) ? candidate : null;
    }
}
