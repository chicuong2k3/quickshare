using PayOS;
using PayOS.Exceptions;
using PayOS.Models;
using PayOS.Models.V2.PaymentRequests;

namespace QuickShare.Features.Payments.PayOS;

/// <summary>
/// Creates and manages PayOS payment links for plan upgrades.
/// </summary>
public class PayOSService : IDisposable
{
    private readonly PayOSClient _client;
    private readonly IConfiguration _config;
    private readonly ILogger<PayOSService> _logger;

    public PayOSService(IConfiguration config, ILogger<PayOSService> logger)
    {
        _config = config;
        _logger = logger;

        var clientId = config["PayOS:ClientId"]
            ?? throw new InvalidOperationException("PayOS:ClientId is not configured");
        var apiKey = config["PayOS:ApiKey"]
            ?? throw new InvalidOperationException("PayOS:ApiKey is not configured");
        var checksumKey = config["PayOS:ChecksumKey"]
            ?? throw new InvalidOperationException("PayOS:ChecksumKey is not configured");

        _client = new PayOSClient(clientId, apiKey, checksumKey);
    }

    /// <summary>
    /// Creates a PayOS payment link and returns the checkout URL + QR code.
    /// </summary>
    public virtual async Task<PayOSPaymentResult> CreatePaymentLinkAsync(
        long orderCode,
        int amountVnd,
        string description,
        CancellationToken ct)
    {
        // Only the origin of the configured URLs is used: PayOS navigates the checkout
        // iframe to these, so they must always land on the standalone return page.
        var returnUrl = ToReturnPage(_config["PayOS:ReturnUrl"]);
        var cancelUrl = ToReturnPage(_config["PayOS:CancelUrl"]);

        // Description limited to ~9 chars for non-linked bank accounts
        var truncatedDesc = description.Length > 9
            ? description[..9]
            : description;

        var request = new CreatePaymentLinkRequest
        {
            OrderCode = orderCode,
            Amount = amountVnd,
            Description = truncatedDesc,
            ReturnUrl = returnUrl,
            CancelUrl = cancelUrl,
            ExpiredAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        };

        _logger.LogInformation(
            "Creating PayOS payment link: orderCode={OrderCode} amount={Amount} desc={Desc}",
            orderCode, amountVnd, truncatedDesc);

        try
        {
            var response = await _client.PaymentRequests.CreateAsync(request, new RequestOptions<CreatePaymentLinkRequest>
            {
                CancellationToken = ct
            });

            _logger.LogInformation(
                "PayOS payment link created: orderCode={OrderCode} checkoutUrl={Url}",
                orderCode, response.CheckoutUrl);

            return new PayOSPaymentResult
            {
                Success = true,
                CheckoutUrl = response.CheckoutUrl,
                QrCode = response.QrCode,
                PaymentLinkId = response.PaymentLinkId,
                Bin = response.Bin,
                AccountNumber = response.AccountNumber,
                AccountName = response.AccountName
            };
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex,
                "PayOS API error creating payment link: orderCode={OrderCode} code={ErrorCode} msg={Message}",
                orderCode, ex.ErrorCode, ex.Message);
            return new PayOSPaymentResult
            {
                Success = false,
                ErrorMessage = $"PayOS API error: {ex.Message}"
            };
        }
        catch (PayOSException ex)
        {
            _logger.LogError(ex,
                "PayOS client error creating payment link: orderCode={OrderCode}",
                orderCode);
            return new PayOSPaymentResult
            {
                Success = false,
                ErrorMessage = $"PayOS error: {ex.Message}"
            };
        }
    }

    private const string DefaultOrigin = "https://quicksharecloud.com";

    private static string ToReturnPage(string? configured)
    {
        var origin = Uri.TryCreate(configured, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : DefaultOrigin;
        return origin + RoutePaths.PayOSReturn;
    }

    /// <summary>
    /// Asks PayOS whether the order is actually paid. This is the trust boundary for the
    /// browser return URL — the query string alone must never activate a subscription.
    /// </summary>
    public virtual async Task<bool> IsPaidAsync(long orderCode, CancellationToken ct)
    {
        try
        {
            var link = await _client.PaymentRequests.GetAsync(orderCode, new RequestOptions
            {
                CancellationToken = ct
            });
            return link.Status == PaymentLinkStatus.Paid;
        }
        catch (PayOSException ex)
        {
            _logger.LogError(ex, "PayOS lookup failed for orderCode={OrderCode}", orderCode);
            return false;
        }
    }

    /// <summary>
    /// Generates a unique numeric order code for PayOS.
    /// Uses timestamp-based random to avoid collisions.
    /// </summary>
    public static long GenerateOrderCode()
    {
        // Combine Unix seconds with random digits for uniqueness
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var random = Random.Shared.Next(100, 999);
        return (timestamp * 1000) + random;
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed record PayOSPaymentResult
{
    public bool Success { get; init; }
    public string? CheckoutUrl { get; init; }
    public string? QrCode { get; init; }
    public string? PaymentLinkId { get; init; }
    public string? Bin { get; init; }
    public string? AccountNumber { get; init; }
    public string? AccountName { get; init; }
    public string? ErrorMessage { get; init; }
}
