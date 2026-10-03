using Microsoft.AspNetCore.Antiforgery;

namespace QuickShare.Features.Notifications.Center;

/// <summary>
/// No-JavaScript form path. The JSON API stays unchanged for clients and Unpoly.
/// Validate CSRF before dispatching; a POST that fails verification must never write.
/// </summary>
public sealed class MarkAllNotificationsReadFormEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post(RoutePaths.AccountNotificationReadAll);
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s => s.Summary = "Mark all notifications as read and return to the inbox");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            await Resolve<IAntiforgery>().ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            await Send.ResponseAsync(null!, StatusCodes.Status400BadRequest, ct);
            return;
        }

        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new MarkAllNotificationsReadCommand(), ct);
        if (!result.IsSuccess())
        {
            await Send.ResponseAsync(null!, StatusCodes.Status422UnprocessableEntity, ct);
            return;
        }

        await Send.RedirectAsync(RoutePaths.AccountNotificationCenter);
    }
}
