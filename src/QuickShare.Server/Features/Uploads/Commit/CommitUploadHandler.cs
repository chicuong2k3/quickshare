using LightResults;
using System.Security.Cryptography;
using QuickShare.Server.Features.Uploads.Shared;

namespace QuickShare.Server.Features.Uploads.Commit;

public record CommitUploadCommand(Guid UploadId);

public record CommitUploadResponse(string ShareId, string ShareLink);

public sealed class CommitUploadHandler(IHttpContextAccessor httpContextAccessor)
{
    public async Task<Result<CommitUploadResponse>> Handle(CommitUploadCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var session = await db.UploadSessions.FindAsync([cmd.UploadId], ct);
        if (session is null)
            return Result.Failure<CommitUploadResponse>(new NotFoundError("UploadSession", cmd.UploadId));

        if (session.Status != "InProgress")
            return Result.Failure<CommitUploadResponse>(new LightResults.Error(
                session.Status == "Committed" ? "Upload already committed" : "Session expired"));

        if (session.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<CommitUploadResponse>(new LightResults.Error("Session expired"));

        var chunks = await db.Chunks
            .Where(c => c.DeliveryId == cmd.UploadId)
            .OrderBy(c => c.Index)
            .ToListAsync(ct);

        var totalChunks = (int)Math.Ceiling((double)session.FileSize / UploadConstants.DefaultChunkSize);

        if (chunks.Count != totalChunks)
        {
            if (chunks.Count < totalChunks)
            {
                var uploadedSet = new HashSet<int>(chunks.Select(c => c.Index));
                var missing = Enumerable.Range(0, totalChunks).Where(i => !uploadedSet.Contains(i)).ToArray();
                return Result.Failure<CommitUploadResponse>(new LightResults.Error(
                    $"Incomplete upload: missing chunks [{string.Join(", ", missing)}]"));
            }
            return Result.Failure<CommitUploadResponse>(new LightResults.Error(
                $"Too many chunks: expected {totalChunks}, got {chunks.Count}"));
        }

        // Generate URL-safe base64 share code (8 random bytes -> ~11 chars)
        var randomBytes = new byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        var shareCode = Convert.ToBase64String(randomBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var deliveryId = Guid.CreateVersion7();

        var userPlan = (await db.Users.FindAsync([session.SenderId], ct))?.Plan ?? PlanTierLimits.Free;

        var delivery = new Delivery
        {
            Id = deliveryId,
            SenderId = session.SenderId,
            FileName = session.FileName,
            ContentType = session.ContentType,
            FileSize = session.FileSize,
            TotalBytes = chunks.Sum(c => c.Size),
            ChunkCount = chunks.Count,
            ShareCode = shareCode,
            MaxDownloads = PlanTierLimits.GetMaxDownloads(userPlan)
        };

        var manifestData = new ManifestData(
            session.FileName,
            session.ContentType,
            session.FileSize,
            chunks.Sum(c => c.Size),
            chunks.Count,
            chunks.Select(c => new ChunkEntry(c.Index, c.StorageRef, c.Sha256, c.Size)).ToArray()
        );

        var manifest = new Manifest
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = deliveryId,
            EncryptedData = ManifestEncryption.Encrypt(manifestData, shareCode)
        };

        await db.Chunks
            .Where(c => c.DeliveryId == cmd.UploadId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.DeliveryId, deliveryId), ct);

        session.Status = "Committed";

        db.Deliveries.Add(delivery);
        db.Manifests.Add(manifest);
        await db.SaveChangesAsync(ct);

        var baseUrl = $"{httpContextAccessor.HttpContext!.Request.Scheme}://{httpContextAccessor.HttpContext.Request.Host}";
        return Result.Success(new CommitUploadResponse(deliveryId.ToString(), $"{baseUrl}/d/{shareCode}"));
    }
}
