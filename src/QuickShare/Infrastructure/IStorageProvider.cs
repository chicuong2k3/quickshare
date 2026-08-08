namespace QuickShare.Infrastructure;

// ── R2 transfer types ──────────────────────────────────────────────────────

/// <summary>Result of initiating an R2 multipart upload.</summary>
public sealed record R2InitResult(string UploadId, string ObjectKey);

/// <summary>A single presigned part URL for direct-client upload to R2.</summary>
public sealed record R2PartUrl(int PartNumber, Uri Url);

/// <summary>ETag returned by R2 after a part upload, used during commit.</summary>
public sealed record R2PartEtag(int PartNumber, string ETag);

/// <summary>Outcome of completing an R2 multipart upload.</summary>
public sealed record CompletionResult(bool Success, string? ErrorMessage);

// ── Storage provider contract ──────────────────────────────────────────────

public interface IStorageProvider
{
    // ── Legacy chunk cleanup (DeliveryCleanupService still sweeps old chunks) ──
    Task DeleteChunksAsync(string[] storageRefs, CancellationToken ct = default);

    // ── R2 multipart upload ────────────────────────────────────────────────
    Task<R2InitResult> InitR2MultipartUploadAsync(
        Guid deliveryId, string fileName, CancellationToken ct = default);

    Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(
        string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default);

    Task<CompletionResult> CompleteR2MultipartUploadAsync(
        string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default);

    Task AbortR2MultipartUploadAsync(
        string uploadId, string objectKey, CancellationToken ct = default);

    // ── R2 download + cleanup ──────────────────────────────────────────────
    Task<Uri> GetPresignedDownloadUrlAsync(
        string objectKey, TimeSpan expiry, CancellationToken ct = default);

    Task DeleteObjectsAsync(
        string prefix, CancellationToken ct = default);
}
