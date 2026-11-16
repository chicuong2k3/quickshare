using System.Text.Json.Nodes;

namespace QuickShare.Features.Payments.Crypto;

/// <summary>
/// Receives confirmed crypto charges (issue #225).
/// POST /api/payments/crypto/webhook
///
/// This is where a crypto plan actually activates. The buyer's browser is long gone by the time
/// a chain confirms, so the redirect proves nothing and the webhook proves everything — which
/// makes its signature check the whole security of the feature, not a formality.
/// </summary>
public sealed class CryptoWebhookEndpoint : EndpointWithoutRequest
{
    private const string EventKey = "event";
    private const string TypeKey = "type";
    private const string DataKey = "data";
    private const string CodeKey = "code";

    public override void Configure()
    {
        Post("/api/payments/crypto/webhook");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Receive a crypto payment webhook";
            s.Description = "Verifies the provider's HMAC over the raw body, matches the PaymentIntent by charge id, and activates the subscription. Idempotent.";
            s.Responses[200] = "Webhook processed.";
            s.Responses[401] = "Invalid signature.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var crypto = Resolve<CryptoCheckoutService>();
        var logger = Resolve<ILogger<CryptoWebhookEndpoint>>();

        // Read the body as text and verify before parsing: the signature covers these bytes.
        using var reader = new StreamReader(HttpContext.Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);
        var signature = HttpContext.Request.Headers[CryptoCheckoutService.SignatureHeader].FirstOrDefault();

        if (!crypto.VerifySignature(rawBody, signature))
        {
            logger.LogWarning("Crypto webhook: signature did not verify");
            ThrowError("Invalid signature", StatusCodes.Status401Unauthorized);
            return;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(rawBody);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Crypto webhook: malformed JSON body");
            ThrowError("Invalid webhook payload", StatusCodes.Status400BadRequest);
            return;
        }

        var eventNode = root?[EventKey];
        var type = eventNode?[TypeKey]?.GetValue<string>();
        var chargeId = eventNode?[DataKey]?[CodeKey]?.GetValue<string>();

        if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(chargeId))
        {
            ThrowError("Invalid webhook payload", StatusCodes.Status400BadRequest);
            return;
        }

        // Every other event — created, pending, delayed, failed — is acknowledged and ignored.
        // A 200 for an event we do not act on stops the provider retrying something that is not
        // a failure, and only confirmation moves a plan.
        if (!CryptoCheckoutService.PaidEventTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            await Send.OkAsync(cancellation: ct);
            return;
        }

        var completed = await Resolve<CheckoutService>().FinalizeByChargeIdAsync(chargeId, ct);
        if (!completed)
            logger.LogWarning("Crypto webhook: no pending intent for charge {ChargeId}", chargeId);

        // Acknowledged either way. A charge we cannot match is not something the provider can
        // fix by sending it again, and an error would earn us hours of retries.
        await Send.OkAsync(cancellation: ct);
    }
}
