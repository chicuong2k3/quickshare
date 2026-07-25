using LightResults;

namespace QuickShare.Server.Features.Folders.Create;

public record CreateFolderCommand(string Name);

public sealed class CreateFolderHandler
{
    public async Task<Result<FolderItemResponse>> Handle(CreateFolderCommand command, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

        var exists = await db.Folders.AsNoTracking().AnyAsync(f => f.Name == command.Name, ct);
        if (exists)
            return Result.Failure<FolderItemResponse>(new ConflictError($"Folder '{command.Name}' already exists"));

        var folder = new Folder
        {
            Id = Guid.CreateVersion7(),
            OwnerId = userId,
            Name = command.Name
        };

        db.Folders.Add(folder);
        await db.SaveChangesAsync(ct);

        return Result.Success(folder.ToFolderItemResponse());
    }
}
