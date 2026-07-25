using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuickShare.Server.Infrastructure;

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

    [Obsolete("Use R2 multipart upload instead.")]
    public async Task<string> StoreChunkAsync(Guid chunkId, byte[] data, CancellationToken ct = default)
    {
        EnsureRootDirectory();
        var filePath = GetChunkPath(chunkId);
        await File.WriteAllBytesAsync(filePath, data, ct);
        _logger.LogDebug("Stored chunk {ChunkId} ({Size} bytes)", chunkId, data.Length);
        return chunkId.ToString("N");
    }

    [Obsolete("Use GetPresignedDownloadUrlAsync instead.")]
    public async Task<byte[]> GetChunkAsync(string storageRef, CancellationToken ct = default)
    {
        EnsureRootDirectory();
        var filePath = GetChunkPath(Guid.Parse(storageRef));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Chunk not found: {storageRef}", filePath);
        return await File.ReadAllBytesAsync(filePath, ct);
    }

    [Obsolete("Use DeleteObjectsAsync instead.")]
    public Task DeleteChunksAsync(string[] storageRefs, CancellationToken ct = default)
    {
        EnsureRootDirectory();
        foreach (var refId in storageRefs)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = GetChunkPath(Guid.Parse(refId));
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Deleted chunk {RefId}", refId);
            }
        }
        return Task.CompletedTask;
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

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support presigned URLs.");

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
        => throw new NotSupportedException("LocalDiskStorageProvider does not support R2 delete.");

    private string GetChunkPath(Guid chunkId) =>
        Path.Combine(_rootPath, $"{chunkId:N}.chunk");
}
