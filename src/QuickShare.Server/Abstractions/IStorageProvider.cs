namespace QuickShare.Server.Abstractions;

public interface IStorageProvider
{
    Task<string> StoreChunkAsync(Guid chunkId, byte[] data, CancellationToken ct = default);
    Task<byte[]> GetChunkAsync(string storageRef, CancellationToken ct = default);
    Task DeleteChunksAsync(string[] storageRefs, CancellationToken ct = default);
}
