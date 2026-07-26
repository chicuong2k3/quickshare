namespace QuickShare.Features.Uploads.Shared;

public record ManifestData(
    string FileName,
    string ContentType,
    long FileSize,
    long TotalBytes,
    int ChunkCount,
    ChunkEntry[] Chunks
);

public record ChunkEntry(int Index, string StorageRef, string Sha256, long Size);
