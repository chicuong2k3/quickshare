using LightResults;

namespace QuickShare.Server.Features.Files.Move;

public record MoveFileCommand(Guid FileId, Guid TargetFolderId);

public sealed class MoveFileHandler
{
    public async Task<Result<FileInfoResponse>> Handle(MoveFileCommand command, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetOrCreateUserAsync(db, ct);
        var userId = user.Id;

        // Find the file
        var file = await db.Files
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == command.FileId, ct);

        if (file is null)
            return Result.Failure(new NotFoundError("File", command.FileId)).AsFailure<FileInfoResponse>();

        if (file.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own this file")).AsFailure<FileInfoResponse>();

        // Verify target folder exists and belongs to user
        var targetFolder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.TargetFolderId, ct);
        if (targetFolder is null)
            return Result.Failure(new NotFoundError("Folder", command.TargetFolderId)).AsFailure<FileInfoResponse>();

        if (targetFolder.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own the target folder")).AsFailure<FileInfoResponse>();

        // Already in this folder
        if (file.FolderId == command.TargetFolderId)
            return Result.Success(file.ToFileInfoResponse());

        // Move the file
        await db.Files
            .Where(f => f.Id == command.FileId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.FolderId, command.TargetFolderId), ct);

        return Result.Success(file.ToFileInfoResponse() with { FolderId = command.TargetFolderId, FolderName = targetFolder.Name });
    }
}
