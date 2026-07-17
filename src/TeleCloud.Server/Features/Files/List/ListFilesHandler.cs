using LightResults;

namespace TeleCloud.Server.Features.Files.List;

public record ListFilesQuery(
    Guid? FolderId = null,
    string[]? Sort = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50
);

public sealed class ListFilesHandler
{
    public async Task<Result<PagedResult<FileInfoResponse>>> Handle(ListFilesQuery query, TeleCloudDbContext db, CancellationToken ct)
    {
        var sorts = SortDescriptor.Parse(query.Sort).ToList();
        if (!FileMetadataSortExtensions.TryValidateSorts(sorts, out var error))
            return Result.Failure<PagedResult<FileInfoResponse>>(new BadRequestError(error));

        IQueryable<FileMetadata> filesQuery = db.Files.Include(f => f.Folder).AsNoTracking();

        if (query.FolderId.HasValue)
            filesQuery = filesQuery.Where(f => f.FolderId == query.FolderId.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
            filesQuery = filesQuery.Where(f => f.Filename.Contains(query.Search) || (f.Folder != null && f.Folder.Path.Contains(query.Search)));

        filesQuery = filesQuery.OrderBy(sorts);

        var result = await filesQuery.ToPagedResultAsync(query.Page, query.PageSize, f => f.ToFileInfoResponse(), ct);
        return Result.Success(result);
    }
}
