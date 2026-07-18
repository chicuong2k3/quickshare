using LightResults;

namespace TeleCloud.Server.Features.Folders.Delete;

public record DeleteFolderCommand(Guid Id);

public sealed class DeleteFolderHandler
{
    public async Task<Result> Handle(DeleteFolderCommand command, TeleCloudDbContext db, CurrentUserService currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);

        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (folder is null)
            return Result.Failure(new NotFoundError("Folder", command.Id));

        if (folder.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own this folder"));

        var hasFiles = await db.Files.AnyAsync(f => f.FolderId == command.Id, ct);
        if (hasFiles)
            return Result.Failure(new ConflictError("Cannot delete folder that contains files"));

        db.Folders.Remove(folder);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
