using LightResults;

namespace TeleCloud.Server.Features.Folders.List;

public record ListFoldersQuery(
    string[]? Sort = null,
    int Page = 1,
    int PageSize = 50
);

public sealed class ListFoldersHandler
{
    public async Task<Result<PagedResult<FolderInfoResponse>>> Handle(ListFoldersQuery query, TeleCloudDbContext db, CancellationToken ct)
    {
        var sorts = SortDescriptor.Parse(query.Sort).ToList();
        if (!FolderSortExtensions.TryValidateSorts(sorts, out var error))
            return Result.Failure<PagedResult<FolderInfoResponse>>(new BadRequestError(error));

        var foldersQuery = db.Folders.AsNoTracking().OrderBy(sorts);

        var result = await foldersQuery.ToPagedResultAsync(
            query.Page, query.PageSize,
            f => f.ToFolderInfoResponse(), ct);

        return Result.Success(result);
    }
}
