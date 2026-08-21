using QuickShare.Features.Uploads.Shared;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Uploads.CommitR2;

/// <summary>One extra file, as the client claims it at commit time. Every field is untrusted.</summary>
public sealed record CommitFile(
    string ObjectKey,
    string UploadId,
    string FileName,
    string ContentType,
    long FileSize,
    IReadOnlyList<R2PartEtag> Parts,
    string? Sha256);

/// <summary>What the extra files added up to, once storage confirmed them.</summary>
public sealed record BundleResult(IReadOnlyList<DeliveryFile> Files, long TotalBytes, string? Error);

/// <summary>
/// Completes the extra files of a multi-file delivery and turns them into rows (issue #79).
///
/// Lives apart from <c>CommitR2Handler</c> deliberately. That handler is already long and
/// carries the single-file contract that everything else in the product depends on; bundling is
/// an additive step that either succeeds entirely or leaves the delivery single-file, and
/// keeping it separate is what makes that all-or-nothing property readable.
/// </summary>
public static class BundleCommitter
{
    /// <summary>
    /// Completes each claimed upload and builds the rows for them.
    ///
    /// **The object key is checked against the delivery's own prefix.** A commit request is
    /// client-supplied, and without that check a caller could name any key in the bucket —
    /// including one belonging to somebody else's delivery — and have it served under a share
    /// code they control. That is the security property this whole function exists to hold.
    /// </summary>
    public static async Task<BundleResult> CompleteAsync(
        Guid deliveryId,
        IReadOnlyList<CommitFile> files,
        long? maxFileSize,
        IStorageProvider storage,
        ILogger logger,
        CancellationToken ct)
    {
        if (files.Count == 0)
            return new BundleResult([], 0, null);

        if (files.Count + 1 > DeliveryFile.MaxFilesPerDelivery)
            return new BundleResult([], 0, ErrorCodes.DeliveryTooManyFiles);

        var rows = new List<DeliveryFile>(files.Count);
        var total = 0L;

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];

            if (!R2ObjectKey.BelongsTo(file.ObjectKey, deliveryId))
            {
                logger.LogWarning("Commit claimed object key {Key} for delivery {DeliveryId}", file.ObjectKey, deliveryId);
                return new BundleResult([], 0, ErrorCodes.DeliveryFileKeyMismatch);
            }

            CompletionResult completion;
            try
            {
                completion = await storage.CompleteR2MultipartUploadAsync(file.UploadId, file.ObjectKey, file.Parts, ct);
            }
            catch (R2StorageException ex)
            {
                logger.LogError(ex, "R2 commit failed for bundle file {Key}", file.ObjectKey);
                return new BundleResult([], 0, UploadErrors.R2Upstream + "commit failed");
            }

            if (!completion.Success)
                return new BundleResult([], 0, completion.ErrorMessage ?? "R2 commit returned failure");

            // Trust the bucket over the request: the declared size is client-controlled and
            // part uploads are not size-constrained. Same rule the single-file path uses.
            var stored = await storage.GetObjectSizeAsync(file.ObjectKey, ct);
            var size = stored >= 0 ? stored : file.FileSize;

            if (maxFileSize.HasValue && size > maxFileSize.Value)
                return new BundleResult([], 0, ErrorCodes.FileTooLarge);

            total += size;

            rows.Add(new DeliveryFile
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = deliveryId,
                // Sanitised again here, not only at add-file time: this is the value that ends
                // up as a zip entry name, and the commit request is its own trust boundary.
                FileName = DeliveryFile.SanitizeName(file.FileName, index + 1),
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                FileSize = size,
                ObjectKey = file.ObjectKey,
                Sha256 = file.Sha256,
                // File 0 is the one the Delivery row itself mirrors, so extras start at 1.
                SortOrder = index + 1,
            });
        }

        return new BundleResult(rows, total, null);
    }

    /// <summary>
    /// The row for file #0 — the one <see cref="Delivery"/> already describes.
    ///
    /// Written even for a single-file delivery, so "every delivery has at least one
    /// <see cref="DeliveryFile"/>" is true without exception and no consumer has to branch on
    /// which shape it is looking at.
    /// </summary>
    public static DeliveryFile PrimaryRow(Delivery delivery, long actualSize) => new()
    {
        Id = Guid.CreateVersion7(),
        DeliveryId = delivery.Id,
        FileName = DeliveryFile.SanitizeName(delivery.FileName, 0),
        ContentType = string.IsNullOrWhiteSpace(delivery.ContentType) ? "application/octet-stream" : delivery.ContentType,
        FileSize = actualSize > 0 ? actualSize : delivery.FileSize,
        ObjectKey = delivery.StoragePath ?? string.Empty,
        Sha256 = delivery.Sha256,
        SortOrder = 0,
    };
}
