using LightResults;

namespace QuickShare.Server.Features.Files.GetInfo;

public record GetFileInfoQuery(Guid Id);

public sealed class GetFileInfoHandler
{
    public async Task<Result<FileInfoResponse>> Handle(GetFileInfoQuery query, AppDbContext db, CancellationToken ct)
    {
        var meta = await db.Files.Include(f => f.Folder).AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.Id, ct);
        if (meta is null)
            return Result.Failure(new NotFoundError("File", query.Id)).AsFailure<FileInfoResponse>();

        return Result.Success(meta.ToFileInfoResponse());
    }
}
