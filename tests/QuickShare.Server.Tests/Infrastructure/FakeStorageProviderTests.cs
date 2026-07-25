using QuickShare.Server.Abstractions;
using QuickShare.Server.Services;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class FakeStorageProvider(string rootPath) : IStorageProvider
{
    private readonly Dictionary<string, byte[]> _store = [];
    private int _callCount;

    public int StoreCallCount => _callCount;

    public Task<string> StoreChunkAsync(Guid chunkId, byte[] data, CancellationToken ct = default)
    {
        _callCount++;
        var refId = chunkId.ToString("N");
        _store[refId] = data;
        return Task.FromResult(refId);
    }

    public Task<byte[]> GetChunkAsync(string storageRef, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(storageRef, out var data))
            throw new FileNotFoundException($"Chunk not found: {storageRef}");
        return Task.FromResult(data);
    }

    public Task DeleteChunksAsync(string[] storageRefs, CancellationToken ct = default)
    {
        foreach (var refId in storageRefs)
            _store.Remove(refId);
        return Task.CompletedTask;
    }

    // ── R2 stubs ─────────────────────────────────────────────────────────

    public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, CancellationToken ct = default)
        => Task.FromResult(new R2InitResult("fake-upload-id", $"fake-key/{deliveryId:N}/{fileName}"));

    public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<R2PartUrl>>(partNumbers.Select(p => new R2PartUrl(p, new Uri($"https://fake.r2.dev/{objectKey}?partNumber={p}"))).ToList());

    public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
        => Task.FromResult(new CompletionResult(true, null));

    public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        => Task.FromResult(new Uri($"https://fake.r2.dev/{objectKey}?expires={expiry.TotalSeconds}"));

    public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
        => Task.CompletedTask;
}

public class FakeStorageProviderTests
{
    private readonly FakeStorageProvider _provider = new("/test-storage");

    [Fact]
    public async Task StoreChunkAsync_round_trip_returns_original_data()
    {
        var chunkId = Guid.CreateVersion7();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var storageRef = await _provider.StoreChunkAsync(chunkId, data);
        var retrieved = await _provider.GetChunkAsync(storageRef);
        Assert.Equal(data, retrieved);
    }

    [Fact]
    public async Task StoreChunkAsync_returns_consistent_reference_format()
    {
        var chunkId = Guid.CreateVersion7();
        var storageRef = await _provider.StoreChunkAsync(chunkId, [42]);
        Assert.Equal(chunkId.ToString("N"), storageRef);
        Assert.Equal(32, storageRef.Length); // Guid "N" format = 32 hex chars
    }

    [Fact]
    public async Task GetChunkAsync_throws_on_unknown_reference()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.GetChunkAsync("nonexistent"));
    }

    [Fact]
    public async Task DeleteChunksAsync_removes_by_reference()
    {
        var chunkId = Guid.CreateVersion7();
        var refId = await _provider.StoreChunkAsync(chunkId, [1, 2]);
        await _provider.DeleteChunksAsync([refId]);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _provider.GetChunkAsync(refId));
    }

    [Fact]
    public async Task DeleteChunksAsync_skips_missing_references_without_error()
    {
        await _provider.DeleteChunksAsync(["nonexistent1", "nonexistent2"]);
        // No exception = pass
    }

    [Fact]
    public async Task StoreChunkAsync_empty_data_is_valid()
    {
        var refId = await _provider.StoreChunkAsync(Guid.CreateVersion7(), []);
        var retrieved = await _provider.GetChunkAsync(refId);
        Assert.Empty(retrieved);
    }

    [Fact]
    public async Task StoreChunkAsync_increments_call_count()
    {
        await _provider.StoreChunkAsync(Guid.CreateVersion7(), [1]);
        await _provider.StoreChunkAsync(Guid.CreateVersion7(), [2]);
        Assert.Equal(2, _provider.StoreCallCount);
    }
}
