namespace QuickShare.Features.Notifications.Center;

public sealed class MarkNotificationReadEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/notifications/{Id}/read");
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s => s.Summary = "Mark one notification as read");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(new MarkNotificationReadCommand(id), ct);

        if (result.IsSuccess())
            await Send.OkAsync(new { Read = true }, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotificationNotFound => 404,
                _ => 400
            });
    }
}
