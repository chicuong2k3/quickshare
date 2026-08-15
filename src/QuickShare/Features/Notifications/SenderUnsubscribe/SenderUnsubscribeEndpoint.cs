using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Notifications.SenderUnsubscribe;

/// <summary>
/// One-click opt-out from sender link emails. Linked at the bottom of the sender link email.
/// Distinct from /unsubscribe, which targets User.EmailOnDownload for digest emails.
/// </summary>
// ponytail: no handler/validator slice — a single tracked flag flip doesn't need a message bus hop.
public sealed class SenderUnsubscribeEndpoint : EndpointWithoutRequest
{
    private const string TokenRouteKey = "token";
    private const string DonePage = "<p>Đã huỷ nhận email cho file này. Bạn sẽ không nhận thêm thông báo nữa.</p>";
    private const string InvalidPage = "<p>Link huỷ đăng ký không hợp lệ hoặc đã hết hạn.</p>";

    public override void Configure()
    {
        Get($"{RoutePaths.UnsubscribeSender}/{{{TokenRouteKey}}}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s =>
        {
            s.Summary = "Unsubscribe from sender link emails for one delivery";
            s.Responses[200] = "Unsubscribed.";
            s.Responses[400] = "Invalid or tampered token.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = Route<string>(TokenRouteKey)!;

        if (!Resolve<SenderEmailTokens>().TryReadUnsubscribeToken(token, out var deliveryId, out _))
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        var db = Resolve<AppDbContext>();

        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null)
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        delivery.SenderEmailConsent = false;
        await db.SaveChangesAsync(ct);

        await Send.StringAsync(DonePage, 200, MediaTypes.Html, ct);
    }
}
