using LightResults;

namespace QuickShare.Features.Deliveries.ExtensionRequests;

public record ListExtensionRequestsQuery(Guid DeliveryId) : IRequest<Result<IReadOnlyList<ExtensionRequestItem>>>;

public record ExtensionRequestItem(
    Guid Id, string Status, DateTime CreatedAt, DateTime? RespondedAt, string? RejectMessage);

public sealed class ListExtensionRequestsHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<ListExtensionRequestsQuery, Result<IReadOnlyList<ExtensionRequestItem>>>
{
    public async ValueTask<Result<IReadOnlyList<ExtensionRequestItem>>> Handle(
        ListExtensionRequestsQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == query.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<IReadOnlyList<ExtensionRequestItem>>(new LightResults.Error(ErrorCodes.NotFound));

        var items = await db.ExtensionRequests
            .AsNoTracking()
            .Where(r => r.DeliveryId == query.DeliveryId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ExtensionRequestItem(r.Id, r.Status, r.CreatedAt, r.RespondedAt, r.RejectMessage))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ExtensionRequestItem>>(items);
    }
}
