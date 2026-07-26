using LightResults;

namespace QuickShare.Features.Folders.Delete;

public record DeleteFolderCommand(Guid Id);

public sealed class DeleteFolderHandler
{
    public async Task<Result> Handle(DeleteFolderCommand command, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

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
