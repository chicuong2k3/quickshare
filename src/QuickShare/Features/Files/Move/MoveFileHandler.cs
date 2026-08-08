using LightResults;

namespace QuickShare.Features.Files.Move;

public record MoveFileCommand(Guid FileId, Guid TargetFolderId);

public sealed class MoveFileHandler
{
    public async Task<Result<FileItemResponse>> Handle(MoveFileCommand command, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

        // Find the file
        var file = await db.Files
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == command.FileId, ct);

        if (file is null)
            return Result.Failure(new NotFoundError("File", command.FileId)).AsFailure<FileItemResponse>();

        if (file.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own this file")).AsFailure<FileItemResponse>();

        // Verify target folder exists and belongs to user
        var targetFolder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.TargetFolderId, ct);
        if (targetFolder is null)
            return Result.Failure(new NotFoundError("Folder", command.TargetFolderId)).AsFailure<FileItemResponse>();

        if (targetFolder.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own the target folder")).AsFailure<FileItemResponse>();

        // Already in this folder
        if (file.FolderId == command.TargetFolderId)
            return Result.Success(file.ToFileItemResponse());

        // Move the file
        file.FolderId = command.TargetFolderId;
        await db.SaveChangesAsync(ct);

        return Result.Success(file.ToFileItemResponse() with { FolderId = command.TargetFolderId, FolderName = targetFolder.Name });
    }
}
