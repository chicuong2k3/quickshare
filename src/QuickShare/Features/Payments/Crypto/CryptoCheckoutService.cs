using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace QuickShare.Features.Payments.Crypto;

/// <summary>What a hosted crypto checkout came back with. Mirrors the PayOS result shape.</summary>
public sealed record CryptoChargeResult(bool Success, string? HostedUrl, string? ChargeId, string? ErrorMessage);

/// <summary>
/// Hosted crypto checkout through Coinbase Commerce (issue #225).
///
/// **Onramp, never custody.** The provider takes the coin, settles to fiat, and we are told a
/// charge was confirmed — the same shape as a card payment. Holding a wallet would make this a
/// treasury feature: key management, volatility between charge and settlement, and a balance
/// somebody has to reconcile. None of that is a file-sharing product's business.
///
/// The plan activates on the **webhook**, not on the browser coming back: a chain confirmation
/// takes minutes and nobody sits on a redirect for that long, which is exactly the case a return
/// URL handles badly and a webhook handles for free.
/// </summary>
public class CryptoCheckoutService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<CryptoCheckoutService> logger)
{
    public const string ApiKeyConfigKey = "Crypto:ApiKey";
    public const string WebhookSecretConfigKey = "Crypto:WebhookSecret";
    public const string ApiBaseConfigKey = "Crypto:ApiBase";
    public const string DefaultApiBase = "https://api.commerce.coinbase.com";
    public const string ApiKeyHeader = "X-CC-Api-Key";
    public const string VersionHeader = "X-CC-Version";
    public const string SignatureHeader = "X-CC-Webhook-Signature";
    public const string ApiVersion = "2018-03-22";

    /// <summary>Charge states that mean the money is ours. Anything else is still in flight.</summary>
    public static readonly string[] PaidEventTypes = ["charge:confirmed", "charge:resolved"];

    /// <summary>False when no API key is configured — the button is not offered at all then.</summary>
    public virtual bool IsConfigured => !string.IsNullOrEmpty(config[ApiKeyConfigKey]);

    /// <summary>Creates a hosted charge and returns the URL the buyer is sent to.</summary>
    public virtual async Task<CryptoChargeResult> CreateChargeAsync(
        string reference, decimal amount, string currency, string description, CancellationToken ct)
    {
        if (!IsConfigured)
            return new CryptoChargeResult(false, null, null, "Crypto payments are not configured.");

        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(config[ApiBaseConfigKey] ?? DefaultApiBase);
        client.DefaultRequestHeaders.Add(ApiKeyHeader, config[ApiKeyConfigKey]);
        client.DefaultRequestHeaders.Add(VersionHeader, ApiVersion);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var body = new
        {
            name = description,
            description,
            pricing_type = "fixed_price",
            local_price = new { amount = amount.ToString(CultureInfo.InvariantCulture), currency },
            // Our own reference travels with the charge so a webhook can be matched back to an
            // intent even if the provider's id is all we are given.
            metadata = new { reference },
        };

        try
        {
            var response = await client.PostAsJsonAsync("/charges", body, ct);
            var payload = await response.Content.ReadFromJsonAsync<JsonNode>(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Crypto charge failed: {Status}", response.StatusCode);
                return new CryptoChargeResult(false, null, null, $"Provider returned {(int)response.StatusCode}.");
            }

            var data = payload?["data"];
            var hostedUrl = data?["hosted_url"]?.GetValue<string>();
            var chargeId = data?["code"]?.GetValue<string>();

            if (string.IsNullOrEmpty(hostedUrl) || string.IsNullOrEmpty(chargeId))
                return new CryptoChargeResult(false, null, null, "Provider returned no checkout URL.");

            return new CryptoChargeResult(true, hostedUrl, chargeId, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogError(ex, "Crypto charge request failed");
            return new CryptoChargeResult(false, null, null, "The payment provider could not be reached.");
        }
    }

    /// <summary>
    /// True when this body really came from the provider.
    ///
    /// HMAC over the **raw** body, compared in constant time. Parsing first and re-serialising
    /// would change bytes the provider signed — the same mistake that once made every PayOS
    /// webhook fail verification here, and the reason that one is written the way it is.
    /// </summary>
    public virtual bool VerifySignature(string rawBody, string? signature)
    {
        var secret = config[WebhookSecretConfigKey];
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature))
            return false;

        var expected = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}
