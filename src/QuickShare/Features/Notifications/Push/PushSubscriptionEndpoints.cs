namespace QuickShare.Features.Notifications.Push;

public sealed class SubscribePushEndpoint : Endpoint<SubscribePushRequest>
{
    public override void Configure()
    {
        Post("/api/push/subscribe");
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s => s.Summary = "Save the browser's push subscription for the caller");
    }

    public override async Task HandleAsync(SubscribePushRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new SubscribePushCommand(req.Endpoint, req.P256dh, req.Auth, req.DeviceName), ct);

        if (result.IsSuccess())
            await Send.OkAsync(new { Subscribed = true }, ct);
    }
}

public sealed class UnsubscribePushEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/push/unsubscribe/{Id}");
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s => s.Summary = "Remove a push subscription");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new UnsubscribePushCommand(id), ct);

        if (result.IsSuccess())
            await Send.OkAsync(new { Unsubscribed = true }, ct);
    }
}

public sealed class PushVapidKeyEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/push/vapid-public-key");
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s => s.Summary = "The VAPID public key the browser needs to subscribe");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result<PushVapidKeyResponse>>(
            new PushVapidKeyQuery(), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
    }
}

public class SubscribePushRequest
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
}
