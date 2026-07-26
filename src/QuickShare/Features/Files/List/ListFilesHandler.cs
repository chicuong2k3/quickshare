using LightResults;

namespace QuickShare.Features.Files.List;

public record ListFilesQuery(
    Guid? FolderId = null,
    string[]? Sort = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50
);

public sealed class ListFilesHandler
{
    public async Task<Result<PagedResult<FileItemResponse>>> Handle(ListFilesQuery query, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;

        var sorts = SortDescriptor.Parse(query.Sort).ToList();
        if (!FileMetadataSortExtensions.TryValidateSorts(sorts, out var error))
            return Result.Failure<PagedResult<FileItemResponse>>(new BadRequestError(error));

        IQueryable<FileMetadata> filesQuery = db.Files
            .Include(f => f.Folder)
            .AsNoTracking()
            .Where(f => f.OwnerId == userId);

        if (query.FolderId.HasValue)
            filesQuery = filesQuery.Where(f => f.FolderId == query.FolderId.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
            filesQuery = filesQuery.Where(f => f.Filename.Contains(query.Search) || (f.Folder != null && f.Folder.Name.Contains(query.Search)));

        filesQuery = filesQuery.OrderBy(sorts);

        var result = await filesQuery.ToPagedResultAsync(query.Page, query.PageSize, f => f.ToFileItemResponse(), ct);
        return Result.Success(result);
    }
}
