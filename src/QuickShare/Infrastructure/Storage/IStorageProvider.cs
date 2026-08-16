namespace QuickShare.Infrastructure.Storage;

// ── R2 transfer types ──────────────────────────────────────────────────────

/// <summary>Result of initiating an R2 multipart upload.</summary>
public sealed record R2InitResult(string UploadId, string ObjectKey);

/// <summary>A single presigned part URL for direct-client upload to R2.</summary>
public sealed record R2PartUrl(int PartNumber, Uri Url);

/// <summary>ETag returned by R2 after a part upload, used during commit.</summary>
public sealed record R2PartEtag(int PartNumber, string ETag);

/// <summary>Outcome of completing an R2 multipart upload.</summary>
public sealed record CompletionResult(bool Success, string? ErrorMessage);

/// <summary>An in-flight multipart upload, as listed from the bucket.</summary>
public sealed record R2MultipartInfo(string UploadId, string ObjectKey, DateTime Initiated);

// ── Storage provider contract ──────────────────────────────────────────────

public interface IStorageProvider
{
    // ── R2 multipart upload ────────────────────────────────────────────────
    Task<R2InitResult> InitR2MultipartUploadAsync(
        Guid deliveryId, string fileName, CancellationToken ct = default);

    Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(
        string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default);

    Task<CompletionResult> CompleteR2MultipartUploadAsync(
        string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default);

    Task AbortR2MultipartUploadAsync(
        string uploadId, string objectKey, CancellationToken ct = default);

    // ── R2 small-object upload (avatars, etc.) ─────────────────────────────

    /// <summary>Uploads a single object (stream) to the bucket — used for small files like avatars.</summary>
    Task UploadObjectAsync(
        string objectKey, Stream content, string contentType, CancellationToken ct = default);

    // ── R2 download + cleanup ──────────────────────────────────────────────

    /// <summary>Actual stored size of an object in bytes, or -1 if it cannot be determined.</summary>
    Task<long> GetObjectSizeAsync(
        string objectKey, CancellationToken ct = default);

    Task<Uri> GetPresignedDownloadUrlAsync(
        string objectKey, TimeSpan expiry, CancellationToken ct = default);

    Task DeleteObjectsAsync(
        string prefix, CancellationToken ct = default);

    /// <summary>Deletes exactly one object by its full key (no prefix expansion).</summary>
    Task DeleteObjectAsync(
        string objectKey, CancellationToken ct = default);

    /// <summary>Server-side copy of a single object to a new key (issue #218 clone).</summary>
    Task CopyObjectAsync(
        string sourceKey, string destinationKey, CancellationToken ct = default);

    /// <summary>
    /// Opens a seekable read stream for an object, or null if it does not exist.
    /// Used for server-side inspection like ZIP content listing (issue #215).
    /// </summary>
    Task<Stream?> OpenReadAsync(
        string objectKey, CancellationToken ct = default);

    // ── R2 orphan sweep (issue #107) ───────────────────────────────────────

    /// <summary>Keys under <paramref name="prefix"/> last modified before <paramref name="olderThan"/>.</summary>
    Task<IReadOnlyList<string>> ListObjectsAsync(
        string prefix, DateTime olderThan, CancellationToken ct = default);

    /// <summary>In-flight multipart uploads under <paramref name="prefix"/> initiated before <paramref name="olderThan"/>.</summary>
    Task<IReadOnlyList<R2MultipartInfo>> ListMultipartUploadsAsync(
        string prefix, DateTime olderThan, CancellationToken ct = default);
}
