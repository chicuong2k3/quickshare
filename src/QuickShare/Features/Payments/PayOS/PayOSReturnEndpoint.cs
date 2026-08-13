namespace QuickShare.Features.Payments.PayOS;

/// <summary>
/// Landing page for the PayOS return/cancel URL. PayOS navigates the checkout iframe here,
/// so this must stay a tiny standalone page — pointing the return URL at /upgrade reloaded
/// the whole app inside the iframe. The parent page picks up the result via its poll.
/// GET /payments/payos/return
/// </summary>
public sealed class PayOSReturnEndpoint(CheckoutService checkout) : EndpointWithoutRequest
{
    private const string OrderCodeQueryKey = "orderCode";
    private const string PaidMessage = "Thanh toán thành công. Gói của bạn đang được kích hoạt…";
    private const string PendingMessage = "Chưa nhận được xác nhận thanh toán. Bạn có thể đóng khung này và thử lại.";

    public override void Configure()
    {
        Get(RoutePaths.PayOSReturn);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments).ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var paid = long.TryParse(Query<string>(OrderCodeQueryKey, isRequired: false), out var orderCode)
            && await checkout.FinalizeByOrderCodeAsync(orderCode, ct);

        var message = paid ? PaidMessage : PendingMessage;
        await Send.StringAsync(
            $"""
            <!doctype html><meta charset="utf-8"><title>PayOS</title>
            <body style="margin:0;display:flex;align-items:center;justify-content:center;height:100vh;font:16px system-ui;text-align:center;padding:1rem">
            <p>{message}</p>
            """, contentType: MediaTypes.Html, cancellation: ct);
    }
}
