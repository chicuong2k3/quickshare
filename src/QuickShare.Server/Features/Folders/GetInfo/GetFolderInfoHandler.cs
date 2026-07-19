using LightResults;

namespace QuickShare.Server.Features.Folders.GetInfo;

public record GetFolderInfoQuery(Guid Id);

public sealed class GetFolderInfoHandler
{
    public async Task<Result<FolderInfoResponse>> Handle(GetFolderInfoQuery query, AppDbContext db, CancellationToken ct)
    {
        var folder = await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.Id, ct);
        if (folder is null)
            return Result.Failure(new NotFoundError("Folder", query.Id)).AsFailure<FolderInfoResponse>();

        return Result.Success(folder.ToFolderInfoResponse());
    }
}
