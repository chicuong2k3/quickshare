namespace QuickShare.Features.Notifications.Center;

public sealed class MarkAllNotificationsReadEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/notifications/read-all");
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s => s.Summary = "Mark all notifications as read");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result>(new MarkAllNotificationsReadCommand(), ct);

        if (result.IsSuccess())
            await Send.OkAsync(new { Read = true }, ct);
    }
}
