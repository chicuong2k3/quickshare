namespace QuickShare.Features.Notifications.Center;

public sealed class UnreadNotificationCountEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/notifications/unread-count");
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s => s.Summary = "Unread notification count for the caller's bell badge");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result<UnreadNotificationCountResponse>>(
            new UnreadNotificationCountQuery(), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
    }
}
