using LightResults;
using System.Security.Cryptography;

namespace QuickShare.Server.Features.Uploads.Chunks;

public record UploadChunkCommand(Guid UploadId, int ChunkIndex, byte[] Data);

public record UploadChunkResponse(int ChunkIndex, bool Stored, bool AlreadyExists);

public sealed class UploadChunkHandler(IStorageProvider storage)
{
    public async Task<Result<UploadChunkResponse>> Handle(UploadChunkCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var session = await db.UploadSessions.FindAsync([cmd.UploadId], ct);
        if (session is null)
            return Result.Failure<UploadChunkResponse>(new NotFoundError("UploadSession", cmd.UploadId));

        if (session.Status != "InProgress")
            return Result.Failure<UploadChunkResponse>(new LightResults.Error("Session expired"));

        if (session.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<UploadChunkResponse>(new LightResults.Error("Session expired"));

        // AD-4: Idempotent — check if chunk already exists
        var existingChunk = await db.Chunks
            .FirstOrDefaultAsync(c => c.DeliveryId == cmd.UploadId && c.Index == cmd.ChunkIndex, ct);

        if (existingChunk is not null)
            return Result.Success(new UploadChunkResponse(cmd.ChunkIndex, false, true));

        var chunkId = Guid.CreateVersion7();
        var storageRef = await storage.StoreChunkAsync(chunkId, cmd.Data, ct);
        var sha256 = Convert.ToHexString(SHA256.HashData(cmd.Data));

        var chunk = new Chunk
        {
            Id = chunkId,
            DeliveryId = cmd.UploadId,
            Index = cmd.ChunkIndex,
            Size = cmd.Data.Length,
            StorageRef = storageRef,
            Sha256 = sha256,
            CreatedAt = DateTime.UtcNow
        };

        db.Chunks.Add(chunk);
        await db.SaveChangesAsync(ct);

        return Result.Success(new UploadChunkResponse(cmd.ChunkIndex, true, false));
    }
}
