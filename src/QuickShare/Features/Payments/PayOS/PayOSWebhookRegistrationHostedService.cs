using PayOS;
using PayOS.Exceptions;
using PayOS.Models.Webhooks;

namespace QuickShare.Features.Payments.PayOS;

/// <summary>
/// Registers the PayOS webhook URL at application startup.
/// PayOS requires calling POST /confirm-webhook to validate and register the endpoint
/// that receives payment notifications. Without this, PayOS won't send webhooks.
/// </summary>
public sealed class PayOSWebhookRegistrationHostedService : IHostedService
{
    private readonly IConfiguration _config;
    private readonly ILogger<PayOSWebhookRegistrationHostedService> _logger;

    public PayOSWebhookRegistrationHostedService(
        IConfiguration config,
        ILogger<PayOSWebhookRegistrationHostedService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var webhookUrl = _config["PayOS:WebhookUrl"];

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning(
                "PayOS:WebhookUrl not configured — skipping webhook registration. " +
                "For development, set PAYOS__WebhookUrl env var to your tunnel URL (e.g. ngrok). " +
                "For production, set the real domain URL.");
            return;
        }

        if (webhookUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "PayOS:WebhookUrl is set to localhost ({Url}) — PayOS cannot reach this URL. " +
                "Use a tunnel (ngrok, Cloudflare Tunnel, etc.) for development and " +
                "set PAYOS__WebhookUrl to the tunnel URL.",
                webhookUrl);
            return;
        }

        try
        {
            var clientId = _config["PayOS:ClientId"]
                ?? throw new InvalidOperationException("PayOS:ClientId not configured");
            var apiKey = _config["PayOS:ApiKey"]
                ?? throw new InvalidOperationException("PayOS:ApiKey not configured");
            var checksumKey = _config["PayOS:ChecksumKey"]
                ?? throw new InvalidOperationException("PayOS:ChecksumKey not configured");

            var client = new PayOSClient(clientId, apiKey, checksumKey);
            var result = await client.Webhooks.ConfirmAsync(webhookUrl);

            _logger.LogInformation(
                "PayOS webhook registered successfully. URL: {WebhookUrl}, Account: {AccountName} ({AccountNumber})",
                result.WebhookUrl, result.AccountName, result.AccountNumber);
        }
        catch (WebhookException ex)
        {
            _logger.LogError(ex,
                "PayOS webhook registration failed for URL: {WebhookUrl}. " +
                "This must be resolved manually on https://my.payos.vn for production.",
                webhookUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error registering PayOS webhook URL: {WebhookUrl}",
                webhookUrl);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
