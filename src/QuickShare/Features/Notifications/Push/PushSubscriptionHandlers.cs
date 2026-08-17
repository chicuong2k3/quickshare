using LightResults;
using Microsoft.EntityFrameworkCore;

namespace QuickShare.Features.Notifications.Push;

public record SubscribePushCommand(string Endpoint, string P256dh, string Auth, string? DeviceName) : IRequest<Result>;

public record UnsubscribePushCommand(Guid Id) : IRequest<Result>;

public record PushVapidKeyQuery : IRequest<Result<PushVapidKeyResponse>>;

public record PushVapidKeyResponse(string Key);

public sealed class SubscribePushHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<SubscribePushCommand, Result>
{
    public async ValueTask<Result> Handle(SubscribePushCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        // Upsert by endpoint: re-subscribing the same browser refreshes its keys.
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == cmd.Endpoint, cancellationToken);

        if (existing is null)
        {
            db.PushSubscriptions.Add(new Data.Entities.PushSubscription
            {
                UserId = user.Id,
                Endpoint = cmd.Endpoint,
                P256dh = cmd.P256dh,
                Auth = cmd.Auth,
                DeviceName = string.IsNullOrWhiteSpace(cmd.DeviceName) ? null : cmd.DeviceName.Trim(),
            });
        }
        else if (existing.UserId == user.Id)
        {
            existing.P256dh = cmd.P256dh;
            existing.Auth = cmd.Auth;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed class UnsubscribePushHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<UnsubscribePushCommand, Result>
{
    public async ValueTask<Result> Handle(UnsubscribePushCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        await db.PushSubscriptions
            .Where(s => s.Id == cmd.Id && s.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class PushVapidKeyHandler(Infrastructure.Push.IPushService pushService)
    : IRequestHandler<PushVapidKeyQuery, Result<PushVapidKeyResponse>>
{
    public ValueTask<Result<PushVapidKeyResponse>> Handle(PushVapidKeyQuery query, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result.Success(new PushVapidKeyResponse(pushService.PublicKey)));
}
