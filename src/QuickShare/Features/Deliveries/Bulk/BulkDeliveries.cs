using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Bulk;

public record BulkDeliveriesCommand(Guid[] Ids, string Action, int Days) : IRequest<Result<BulkDeliveriesResponse>>;

public record BulkDeliveriesResponse(int Affected, int Skipped);

public sealed class BulkDeliveriesHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<BulkDeliveriesCommand, Result<BulkDeliveriesResponse>>
{
    public const string ActionExtend = "extend";
    public const string ActionTrash = "trash";

    private const double DefaultExtendDays = 7.0;
    private const int FreeExtendWindowHours = 24;
    private const int ExpiredGraceHours = 72;

    public async ValueTask<Result<BulkDeliveriesResponse>> Handle(
        BulkDeliveriesCommand cmd,
        CancellationToken cancellationToken)
    {
        var ids = cmd.Ids.Distinct().Take(500).ToArray();
        if (ids.Length == 0)
            return Result.Failure<BulkDeliveriesResponse>(new LightResults.Error(ErrorCodes.NotFound));

        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var plan = user.Plan;

        var deliveries = await db.Deliveries
            .Where(d => ids.Contains(d.Id) && d.SenderId == user.Id)
            .ToListAsync(cancellationToken);

        if (deliveries.Count == 0)
            return Result.Failure<BulkDeliveriesResponse>(new LightResults.Error(ErrorCodes.NotFound));

        var now = DateTime.UtcNow;
        var affected = 0;
        var skipped = 0;

        switch (cmd.Action)
        {
            case ActionTrash:
                foreach (var d in deliveries)
                {
                    if (d.Status == DeliveryStatus.Scheduled)
                    {
                        skipped++;
                        continue;
                    }

                    d.DeletedAt = now;
                    affected++;
                }
                break;

            case ActionExtend:
                var days = cmd.Days > 0 ? cmd.Days : DefaultExtendDays;
                var newExpiry = now.AddDays(days);
                var cap = now.Add(PlanTierLimits.GetMaxExpiry(plan));
                if (newExpiry > cap)
                    newExpiry = cap;

                foreach (var d in deliveries)
                {
                    if (d.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
                    {
                        skipped++;
                        continue;
                    }

                    // Free (and any non-paid) tiers may only extend near or just
                    // after expiry — same rule as the single extend handler (#45).
                    if (plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
                    {
                        if (d.ExpiresAt is not { } expiresAt)
                        {
                            skipped++;
                            continue;
                        }

                        var untilExpiry = expiresAt - now;
                        if (untilExpiry > TimeSpan.FromHours(FreeExtendWindowHours)
                            || untilExpiry < TimeSpan.FromHours(-ExpiredGraceHours))
                        {
                            skipped++;
                            continue;
                        }
                    }

                    d.ExpiresAt = newExpiry;
                    d.Status = DeliveryStatus.Active;
                    affected++;
                }
                break;

            default:
                return Result.Failure<BulkDeliveriesResponse>(new LightResults.Error(ErrorCodes.BadRequest));
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var d in deliveries)
            await cache.RemoveAsync($"download:info:{d.ShareCode}", cancellationToken);

        return Result.Success(new BulkDeliveriesResponse(affected, skipped));
    }
}

public sealed class BulkDeliveriesEndpoint : Endpoint<BulkDeliveriesRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/bulk");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Bulk extend or trash deliveries";
            s.Description = "Sender only (issue #144). Action is 'extend' (Days days, default 7) or 'trash' (soft-delete).";
        });
    }

    public override async Task HandleAsync(BulkDeliveriesRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<BulkDeliveriesResponse>>(
            new BulkDeliveriesCommand(req.Ids ?? [], req.Action ?? string.Empty, req.Days), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}

public class BulkDeliveriesRequest
{
    public Guid[]? Ids { get; set; }

    public string? Action { get; set; }

    public int Days { get; set; }
}
