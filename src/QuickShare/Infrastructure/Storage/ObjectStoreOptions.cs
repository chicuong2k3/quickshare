namespace QuickShare.Infrastructure.Storage;

/// <summary>
/// Connection settings for the S3-compatible object store (issue #171).
///
/// R2 was always reached through the AWS S3 SDK — it is S3-compatible, and the only thing that
/// made the provider R2-specific was deriving the endpoint from a Cloudflare account id. This
/// type makes the endpoint explicit, which is the whole feature: the same provider now serves
/// MinIO, Garage, Ceph, Wasabi or AWS S3 itself, and self-hosters stop being limited to local
/// disk.
///
/// The legacy <c>R2</c> configuration section still binds, through <see cref="FromR2"/>. An
/// options rename that silently breaks every existing deployment on upgrade is not a feature.
/// </summary>
public sealed class ObjectStoreOptions
{
    public const string Section = "ObjectStore";

    /// <summary>Region string sent to the SDK. S3-compatible stores generally ignore it; R2 wants "auto".</summary>
    public const string DefaultRegion = "auto";

    /// <summary>
    /// Full https endpoint of the store, e.g. <c>https://minio.internal:9000</c>. When empty,
    /// <see cref="AccountId"/> is used to derive the Cloudflare R2 endpoint.
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Cloudflare account id. Only used to derive an endpoint when none is given.</summary>
    public string AccountId { get; init; } = string.Empty;

    public string Region { get; init; } = DefaultRegion;
    public string BucketName { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string AccessKeySecret { get; init; } = string.Empty;

    /// <summary>
    /// Path-style addressing (<c>host/bucket/key</c>) rather than virtual-host style
    /// (<c>bucket.host/key</c>). True by default because R2, MinIO and Garage all want it, and
    /// virtual-host style additionally needs wildcard DNS that a self-hoster rarely has.
    /// </summary>
    public bool ForcePathStyle { get; init; } = true;

    /// <summary>Public base URL for objects served directly (custom domain / public bucket).</summary>
    public string PublicBaseUrl { get; init; } = string.Empty;

    public int MultipartThreshold { get; init; } = 10 * 1024 * 1024;

    public PresignedUrlExpirySettings PresignedUrlExpiry { get; init; } = new();

    /// <summary>
    /// Storage locations a sender may pin a delivery to (issue #119). Empty means residency is
    /// not offered and every object lives in <see cref="BucketName"/> — which is what every
    /// deployment configured before this feature keeps doing.
    /// </summary>
    public List<StorageRegionOption> Regions { get; init; } = [];

    /// <summary>Configured regions that are actually usable.</summary>
    public IReadOnlyList<StorageRegionOption> UsableRegions =>
        Regions.Where(r => r.IsConfigured).ToList();

    /// <summary>Residency is on offer only once more than nothing is configured.</summary>
    public bool SupportsResidency => UsableRegions.Count > 0;

    public StorageRegionOption? FindRegion(string? code) =>
        code is null ? null : UsableRegions.FirstOrDefault(r => r.Code == code);

    /// <summary>Enough to talk to a store: an endpoint we can derive, credentials, and a bucket.</summary>
    public bool IsConfigured =>
        !string.IsNullOrEmpty(ResolvedEndpoint)
        && !string.IsNullOrEmpty(AccessKeyId)
        && !string.IsNullOrEmpty(AccessKeySecret)
        && !string.IsNullOrEmpty(BucketName);

    /// <summary>Explicit endpoint wins; otherwise fall back to the R2 shape.</summary>
    public string ResolvedEndpoint =>
        !string.IsNullOrEmpty(Endpoint)
            ? Endpoint
            : string.IsNullOrEmpty(AccountId)
                ? string.Empty
                : $"https://{AccountId}.r2.cloudflarestorage.com";

    /// <summary>
    /// Projects the legacy <c>R2</c> section onto the new shape so existing deployments keep
    /// working untouched after an upgrade.
    /// </summary>
    public static ObjectStoreOptions FromR2(R2Options legacy) => new()
    {
        AccountId = legacy.AccountId,
        BucketName = legacy.BucketName,
        AccessKeyId = legacy.AccessKeyId,
        AccessKeySecret = legacy.AccessKeySecret,
        PublicBaseUrl = legacy.PublicBaseUrl,
        MultipartThreshold = legacy.MultipartThreshold,
        PresignedUrlExpiry = legacy.PresignedUrlExpiry,
    };
}
