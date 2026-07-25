using LightResults;

namespace QuickShare.Server.Features.Files.Delete;

public record DeleteFileCommand(Guid Id);

public sealed class DeleteFileHandler
{
    public async Task<Result> Handle(DeleteFileCommand command, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;
        var meta = await db.Files.FirstOrDefaultAsync(f => f.Id == command.Id, ct);
        if (meta is null)
            return Result.Failure(new NotFoundError("File", command.Id));

        if (meta.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own this file"));

        db.Files.Remove(meta);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
