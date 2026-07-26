using LightResults;

namespace QuickShare.Features.Folders.List;

public record ListFoldersQuery(
    string[]? Sort = null,
    int Page = 1,
    int PageSize = 50
);

public sealed class ListFoldersHandler
{
    public async Task<Result<PagedResult<FolderItemResponse>>> Handle(ListFoldersQuery query, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

        var sorts = SortDescriptor.Parse(query.Sort).ToList();
        if (!FolderSortExtensions.TryValidateSorts(sorts, out var error))
            return Result.Failure<PagedResult<FolderItemResponse>>(new BadRequestError(error));

        var foldersQuery = db.Folders
            .AsNoTracking()
            .Where(f => f.OwnerId == userId)
            .OrderBy(sorts);

        var total = await foldersQuery.CountAsync(ct);

        var items = await foldersQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new FolderItemResponse(
                f.Id,
                f.Name,
                f.CreatedAt,
                f.UpdatedAt,
                f.OwnerId,
                db.Files.Count(fi => fi.FolderId == f.Id)
            ))
            .ToListAsync(ct);

        var result = new PagedResult<FolderItemResponse>(items, total, query.Page, query.PageSize);
        return Result.Success(result);
    }
}
