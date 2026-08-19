using LightResults;

namespace QuickShare.Features.Deliveries.Lock;

public sealed class LockDeliveryRequest
{
    public int? DurationMinutes { get; set; }
    public string? Reason { get; set; }
}

/// <summary>POST /api/deliveries/{Id}/lock — lock a delivery (issue #308).</summary>
public sealed class LockDeliveryEndpoint : Endpoint<LockDeliveryRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/lock");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Lock a delivery for replacement";
            s.Description = "Sender only. Blocks file replacement while the lock is active. Self-lock is idempotent; a lock held by another user is force-broken.";
        });
    }

    public override async Task HandleAsync(LockDeliveryRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");

        var result = await Resolve<IMediator>().Send<LightResults.Result<LockDeliveryResponse>>(
            new LockDeliveryCommand(id, req.DurationMinutes, req.Reason), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.LockDurationInvalid => 400,
                _ => 400
            });
    }
}