using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Folders.GetInfo;

public record GetFolderInfoQuery(Guid Id);

public sealed class GetFolderInfoHandler
{
    public async Task<Result<FolderItemResponse>> Handle(GetFolderInfoQuery query, AppDbContext db, CacheService cache, CancellationToken ct)
    {
        return await cache.GetOrSetAsync(
            $"folder:info:{query.Id}",
            async ct2 =>
            {
                var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.Id, ct2);
                if (folder is null)
                    return Result.Failure(new NotFoundError("Folder", query.Id)).AsFailure<FolderItemResponse>();

                return Result.Success(folder.ToFolderItemResponse());
            },
            duration: TimeSpan.FromSeconds(30),
            token: ct
        );
    }
}
