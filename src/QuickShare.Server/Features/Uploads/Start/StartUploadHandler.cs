using LightResults;

namespace QuickShare.Server.Features.Uploads.Start;

public record StartUploadCommand(string FileName, long FileSize, string MimeType);

public record StartUploadResponse(Guid UploadId, int ChunkSize);

public sealed class StartUploadHandler
{
    public async Task<Result<StartUploadResponse>> Handle(StartUploadCommand cmd, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

        if (cmd.FileSize <= 0)
            return Result.Failure<StartUploadResponse>(new LightResults.Error("File size must be positive"));

        var maxFileSize = PlanTierLimits.GetMaxFileSize(user.Plan);
        if (maxFileSize.HasValue && cmd.FileSize > maxFileSize.Value)
            return Result.Failure<StartUploadResponse>(new LightResults.Error(
                $"File size {cmd.FileSize} exceeds {user.Plan} plan limit of {maxFileSize} bytes"));

        var maxExpiryDays = PlanTierLimits.GetMaxExpiryDays(user.Plan);
        var session = new UploadSession
        {
            Id = Guid.CreateVersion7(),
            SenderId = userId,
            FileName = cmd.FileName,
            ContentType = cmd.MimeType,
            FileSize = cmd.FileSize,
            Status = "InProgress",
            ExpiresAt = DateTime.UtcNow.AddDays(maxExpiryDays)
        };

        db.UploadSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return Result.Success(new StartUploadResponse(session.Id, UploadConstants.DefaultChunkSize));
    }
}
