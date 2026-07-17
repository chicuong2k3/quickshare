using LightResults;

namespace TeleCloud.Server.Features.Folders.Update;

public record UpdateFolderCommand(Guid Id, string Path);

public sealed class UpdateFolderHandler
{
    public async Task<Result<FolderInfoResponse>> Handle(UpdateFolderCommand command, TeleCloudDbContext db, CancellationToken ct)
    {
        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (folder is null)
            return Result.Failure<FolderInfoResponse>(new NotFoundError("Folder", command.Id));

        var exists = await db.Folders.AnyAsync(f => f.Path == command.Path && f.Id != command.Id, ct);
        if (exists)
            return Result.Failure<FolderInfoResponse>(new ConflictError($"Folder '{command.Path}' already exists"));

        await db.Folders.Where(f => f.Id == command.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.Path, command.Path), ct);

        return Result.Success(folder.ToFolderInfoResponse() with { Path = command.Path });
    }
}
