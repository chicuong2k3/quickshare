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

    public async Task<string> StoreChunkAsync(Guid chunkId, byte[] data, CancellationToken ct = default)
    {
        EnsureRootDirectory();
        var filePath = GetChunkPath(chunkId);
        await File.WriteAllBytesAsync(filePath, data, ct);
        _logger.LogDebug("Stored chunk {ChunkId} ({Size} bytes)", chunkId, data.Length);
        return chunkId.ToString("N");
    }

    public async Task<byte[]> GetChunkAsync(string storageRef, CancellationToken ct = default)
    {
        EnsureRootDirectory();
        var filePath = GetChunkPath(Guid.Parse(storageRef));
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Chunk not found: {storageRef}", filePath);
        return await File.ReadAllBytesAsync(filePath, ct);
    }

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

    private string GetChunkPath(Guid chunkId) =>
        Path.Combine(_rootPath, $"{chunkId:N}.chunk");
}
