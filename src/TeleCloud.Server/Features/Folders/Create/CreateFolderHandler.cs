using LightResults;

namespace TeleCloud.Server.Features.Folders.Create;

public record CreateFolderCommand(string Path);

public sealed class CreateFolderHandler
{
    public async Task<Result<FolderInfoResponse>> Handle(CreateFolderCommand command, TeleCloudDbContext db, CurrentUserService currentUser, CancellationToken ct)
    {
        var userId = await currentUser.GetUserIdAsync(ct);

        var exists = await db.Folders.AsNoTracking().AnyAsync(f => f.Path == command.Path, ct);
        if (exists)
            return Result.Failure<FolderInfoResponse>(new ConflictError($"Folder '{command.Path}' already exists"));

        var folder = new Folder
        {
            Id = Guid.CreateVersion7(),
            OwnerId = userId,
            Path = command.Path,
            CreatedAt = DateTime.UtcNow
        };

        db.Folders.Add(folder);
        await db.SaveChangesAsync(ct);

        return Result.Success(folder.ToFolderInfoResponse());
    }
}
