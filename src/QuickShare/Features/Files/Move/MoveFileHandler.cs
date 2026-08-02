using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Files.Move;

public record MoveFileCommand(Guid FileId, Guid TargetFolderId);
public sealed class MoveFileHandler
{
    public async Task<Result<FileItemResponse>> Handle(MoveFileCommand command, AppDbContext db, IHttpContextAccessor http, CacheService cache, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

        var file = await db.Files
            .Include(f => f.Folder)
            .FirstOrDefaultAsync(f => f.Id == command.FileId, ct);

        if (file is null)
            return Result.Failure(new NotFoundError("File", command.FileId)).AsFailure<FileItemResponse>();

        if (file.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own this file")).AsFailure<FileItemResponse>();

        var targetFolder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == command.TargetFolderId, ct);
        if (targetFolder is null)
            return Result.Failure(new NotFoundError("Folder", command.TargetFolderId)).AsFailure<FileItemResponse>();

        if (targetFolder.OwnerId != userId)
            return Result.Failure(new ForbiddenError("You do not own the target folder")).AsFailure<FileItemResponse>();

        if (file.FolderId == command.TargetFolderId)
            return Result.Success(file.ToFileItemResponse());

        var sourceFolderKey = file.FolderId.ToString();
        var targetFolderKey = command.TargetFolderId.ToString();

        await db.Files
            .Where(f => f.Id == command.FileId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.FolderId, command.TargetFolderId), ct);

        await cache.RemoveAsync($"files:list:{userId}:root", ct);
        await cache.RemoveAsync($"files:list:{userId}:{sourceFolderKey}", ct);
        await cache.RemoveAsync($"files:list:{userId}:{targetFolderKey}", ct);

        return Result.Success(file.ToFileItemResponse() with { FolderId = command.TargetFolderId, FolderName = targetFolder.Name });
    }
}
