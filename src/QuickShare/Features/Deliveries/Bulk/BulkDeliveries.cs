using LightResults;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Bulk;

public record BulkDeliveriesCommand(Guid[] Ids, string Action, int Days, Guid? FolderId = null) : IRequest<Result<BulkDeliveriesResponse>>;

public record BulkDeliveriesResponse(int Affected, int Skipped);

public sealed class BulkDeliveriesHandler(
    AppDbContext db, IHttpContextAccessor http, CacheService cache, WorkspaceService workspaces)
    : IRequestHandler<BulkDeliveriesCommand, Result<BulkDeliveriesResponse>>
{
    public const string ActionExtend = "extend";
    public const string ActionTrash = "trash";
    /// <summary>Refile into a shared folder, or out of one (issues #244, #284).</summary>
    public const string ActionMove = "move";

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

                    // Legal hold (issue #266): held rows are reported as skipped rather
                    // than failing the batch — one frozen file must not block the rest.
                    if (d.LegalHoldAt is not null)
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

            case ActionMove:
                var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);

                // Filing is a workspace act. A personal share has no team to file it in front
                // of, and moving one into a shared folder would be a *share*, not a move —
                // silently handing colleagues a file its owner never offered them.
                if (seat is null || !seat.IsActive)
                    return Result.Failure<BulkDeliveriesResponse>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

                WorkspaceFolder.EffectiveDefaults? defaults = null;
                if (cmd.FolderId is { } targetId)
                {
                    defaults = await FolderDefaults.ResolveAsync(db, seat.WorkspaceId, targetId, cancellationToken);

                    // Null means the folder is not this workspace's. Refused rather than
                    // reported as "skipped": the caller asked for a destination that is not
                    // theirs, and saying nothing would look like the move worked.
                    if (defaults is null)
                        return Result.Failure<BulkDeliveriesResponse>(new LightResults.Error(ErrorCodes.FolderNotFound));

                    // The same refusal the mount write gets: a folder whose locked policy
                    // demands a password cannot accept a file that does not have one, and this
                    // path has nowhere to ask for it.
                    if (defaults is { Locked: true, PasswordRequired: true })
                        return Result.Failure<BulkDeliveriesResponse>(new LightResults.Error(ErrorCodes.FolderPasswordRequired));
                }

                foreach (var d in deliveries)
                {
                    // Only within the same workspace, and never a personal row.
                    if (d.WorkspaceId != seat.WorkspaceId)
                    {
                        skipped++;
                        continue;
                    }

                    if (d.LegalHoldAt is not null)
                    {
                        skipped++;
                        continue;
                    }

                    d.FolderId = cmd.FolderId;

                    // Locked defaults travel with the destination and only ever tighten
                    // (issue #245): privacy switches on, an expiry window shortens, and
                    // neither ever loosens because the file was dragged somewhere else.
                    if (defaults is { Locked: true })
                    {
                        if (defaults.PrivacyMode)
                            d.PrivacyMode = true;

                        if (defaults.ExpiryHours is { } hours)
                        {
                            var window = now.AddHours(hours);
                            if (d.ExpiresAt is null || d.ExpiresAt > window)
                                d.ExpiresAt = window;
                        }
                    }

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
            s.Summary = "Bulk extend, trash or move deliveries";
            s.Description = "Sender only (issues #144, #244, #284). Action is 'extend' (Days days, default 7), 'trash' (soft-delete), or 'move' (into FolderId, or out of a folder when null).";
        });
    }

    public override async Task HandleAsync(BulkDeliveriesRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<BulkDeliveriesResponse>>(
            new BulkDeliveriesCommand(req.Ids ?? [], req.Action ?? string.Empty, req.Days, req.FolderId), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.FolderNotFound => 404,
                ErrorCodes.WorkspaceForbidden => 403,
                ErrorCodes.FolderPasswordRequired => 409,
                _ => 400
            });
    }
}

public class BulkDeliveriesRequest
{
    public Guid[]? Ids { get; set; }

    public string? Action { get; set; }

    public int Days { get; set; }

    /// <summary>Destination folder for the 'move' action; null files the deliveries loose in the workspace.</summary>
    public Guid? FolderId { get; set; }
}
