using LightResults;

namespace TeleCloud.Server.Features.TempShares.Get;

public record GetTempShareQuery(Guid Id);

public sealed class GetTempShareHandler
{
    public async Task<Result<TempShareInfoResponse>> Handle(GetTempShareQuery query, TeleCloudDbContext db, CancellationToken ct)
    {
        var share = await db.TempShares
            .Include(t => t.File)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.Id, ct);

        if (share is null)
            return Result.Failure(new NotFoundError("Temp share", query.Id)).AsFailure<TempShareInfoResponse>();

        return Result.Success(share.ToTempShareInfoResponse(DateTime.UtcNow));
    }
}
