using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Notifications.Unsubscribe;

/// <summary>
/// One-click opt-out from download notifications. Linked at the bottom of every digest email.
/// </summary>
// ponytail: no handler/validator slice — a single tracked flag flip doesn't need a message bus hop.
public sealed class UnsubscribeEndpoint : EndpointWithoutRequest
{
    private const string TokenRouteKey = "token";
    private const string DonePage = "<p>Đã huỷ đăng ký. Bạn sẽ không nhận email thông báo tải file nữa.</p>";
    private const string InvalidPage = "<p>Link huỷ đăng ký không hợp lệ hoặc đã hết hạn.</p>";

    public override void Configure()
    {
        Get($"{RoutePaths.Unsubscribe}/{{{TokenRouteKey}}}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s =>
        {
            s.Summary = "Unsubscribe from download notification emails";
            s.Responses[200] = "Unsubscribed.";
            s.Responses[400] = "Invalid or tampered token.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = Route<string>(TokenRouteKey)!;

        if (!Resolve<UnsubscribeTokens>().TryRead(token, out var userId))
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        var db = Resolve<AppDbContext>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        user.EmailOnDownload = false;
        await db.SaveChangesAsync(ct);

        await Send.StringAsync(DonePage, 200, MediaTypes.Html, ct);
    }
}
