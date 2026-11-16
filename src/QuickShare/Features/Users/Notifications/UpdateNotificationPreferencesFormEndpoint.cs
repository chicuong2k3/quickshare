using Microsoft.AspNetCore.Antiforgery;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Users.Notifications;

public sealed class UpdateNotificationPreferencesFormEndpoint : EndpointWithoutRequest
{
    public const string FormRoute = "/account/notifications/save";
    public const string EmailOnDownloadField = "emailOnDownload";
    public const string TrackMyDownloadsField = "trackMyDownloads";

    public override void Configure()
    {
        Post(FormRoute);
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Users));
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

        var form = await HttpContext.Request.ReadFormAsync(ct);
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var emailOnDownload = form.ContainsKey(EmailOnDownloadField);
        var trackMyDownloads = form.ContainsKey(TrackMyDownloadsField);

        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.EmailOnDownload, emailOnDownload)
                .SetProperty(u => u.TrackMyDownloads, trackMyDownloads), ct);
        await UserCacheInvalidator.EvictAsync(db, user.Id, ct);

        HttpContext.Response.StatusCode = StatusCodes.Status303SeeOther;
        HttpContext.Response.Headers.Location = $"{RoutePaths.Account}#notifications-card";
    }
}
