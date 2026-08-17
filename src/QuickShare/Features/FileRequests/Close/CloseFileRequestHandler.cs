using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.FileRequests.Close;

public record CloseFileRequestCommand(Guid RequestId) : IRequest<Result>;

public sealed class CloseFileRequestHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<CloseFileRequestCommand, Result>
{
    public async ValueTask<Result> Handle(
        CloseFileRequestCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var request = await db.FileRequests
            .FirstOrDefaultAsync(r => r.Id == cmd.RequestId && r.OwnerId == user.Id, cancellationToken);

        if (request is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (request.ClosedAt is not null)
            return Result.Success();

        request.ClosedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await cache.RemoveAsync($"file-request:info:{request.Code}", cancellationToken);

        return Result.Success();
    }
}
