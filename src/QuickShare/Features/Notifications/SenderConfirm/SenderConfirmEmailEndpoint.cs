using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Notifications.SenderConfirm;

/// <summary>
/// Marks an anonymous sender's email as confirmed. Linked from the sender link email
/// ("Xác nhận email"). Effectively single-use: an already-confirmed delivery rejects the token.
/// </summary>
// ponytail: no handler/validator slice — a single tracked flag flip doesn't need a message bus hop.
public sealed class SenderConfirmEmailEndpoint : EndpointWithoutRequest
{
    private const string TokenRouteKey = "token";
    private const string DonePage = "<p>Email của bạn đã được xác nhận. Bạn sẽ nhận thông báo khi file được tải.</p>";
    private const string InvalidPage = "<p>Link xác nhận không hợp lệ hoặc đã hết hạn.</p>";

    public override void Configure()
    {
        Get($"{RoutePaths.ConfirmSenderEmail}/{{{TokenRouteKey}}}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s =>
        {
            s.Summary = "Confirm anonymous sender email";
            s.Responses[200] = "Email confirmed.";
            s.Responses[400] = "Invalid, expired, or already-confirmed token.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = Route<string>(TokenRouteKey)!;

        if (!Resolve<SenderEmailTokens>().TryReadConfirmToken(token, out var deliveryId, out _))
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        var db = Resolve<AppDbContext>();

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId && d.SenderEmail != null, ct);
        if (delivery is null || delivery.SenderEmailConfirmed)
        {
            await Send.StringAsync(InvalidPage, 400, MediaTypes.Html, ct);
            return;
        }

        delivery.SenderEmailConfirmed = true;
        await db.SaveChangesAsync(ct);

        await Send.StringAsync(DonePage, 200, MediaTypes.Html, ct);
    }
}
