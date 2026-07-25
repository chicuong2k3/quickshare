using LightResults;

namespace QuickShare.Server.Features.Folders.Update;

public record UpdateFolderCommand(Guid Id, string Name);

public sealed class UpdateFolderHandler
{
    public async Task<Result<FolderItemResponse>> Handle(UpdateFolderCommand command, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (folder is null)
            return Result.Failure<FolderItemResponse>(new NotFoundError("Folder", command.Id));

        if (folder.OwnerId != userId)
            return Result.Failure<FolderItemResponse>(new ForbiddenError("You do not own this folder"));

        var exists = await db.Folders.AnyAsync(f => f.Name == command.Name && f.Id != command.Id, ct);
        if (exists)
            return Result.Failure<FolderItemResponse>(new ConflictError($"Folder '{command.Name}' already exists"));

        await db.Folders.Where(f => f.Id == command.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.Name, command.Name), ct);

        return Result.Success(folder.ToFolderItemResponse() with { Name = command.Name });
    }
}
