using Microsoft.Extensions.Options;

namespace QuickShare.Infrastructure.Storage;

public sealed class LocalDiskStorageProvider : IStorageProvider
{
    private readonly string _rootPath;
    private readonly ILogger<LocalDiskStorageProvider> _logger;
    private readonly object _initLock = new();
    private bool _initialized;

    public LocalDiskStorageProvider(IOptions<StorageOptions> options, ILogger<LocalDiskStorageProvider> logger)
    {
        _rootPath = options.Value.LocalDisk.RootPath;
        _logger = logger;
    }

    private void EnsureRootDirectory()
    {
        if (_initialized) return;
        lock (_initLock)
        {
            if (_initialized) return;
            Directory.CreateDirectory(_rootPath);
            _initialized = true;
            _logger.LogInformation("LocalDiskStorage root: {RootPath}", Path.GetFullPath(_rootPath));
        }
    }

    // ── R2 methods (not supported on local disk) ───────────────────────────

    public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support R2 multipart upload.");

    public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support R2 presigned URLs.");

    public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support R2 multipart upload.");

    public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support R2 multipart upload.");

    public Task<long> GetObjectSizeAsync(string objectKey, CancellationToken ct = default)
        => Task.FromResult(-1L);

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support presigned URLs.");

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support R2 delete.");
}
