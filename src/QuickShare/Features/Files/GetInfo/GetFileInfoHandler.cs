using LightResults;

namespace QuickShare.Features.Files.GetInfo;

public record GetFileInfoQuery(Guid Id);

public sealed class GetFileInfoHandler
{
    public async Task<Result<FileItemResponse>> Handle(GetFileInfoQuery query, AppDbContext db, CancellationToken ct)
    {
        var meta = await db.Files.Include(f => f.Folder).AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.Id, ct);
        if (meta is null)
            return Result.Failure(new NotFoundError("File", query.Id)).AsFailure<FileItemResponse>();

        return Result.Success(meta.ToFileItemResponse());
    }
}
