using System.Security.Cryptography;
using QuickShare.Features.Payments.PayOS;

namespace QuickShare.Features.Payments.Credits;

public sealed class PurchaseTransferPassRequest
{
    public string? Email { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Currency { get; set; }
}

public sealed record TransferPassDto(
    Guid Id,
    string Token,
    string Status,
    int CreditsRemaining,
    long MaxFileSizeBytes,
    int MaxExpiryDays,
    decimal AmountPaid,
    string Currency,
    string? CheckoutUrl,
    DateTime ExpiresAt);

/// <summary>
/// POST /api/credits/transfer-pass — creates a one-time pay-per-transfer credit pass (issue #85).
/// Works for both anonymous and signed-in senders so a user hitting the Free size/expiry wall can
/// pay once via PayOS without subscribing.
/// </summary>
public sealed class PurchaseTransferPassEndpoint(
    AppDbContext db,
    PayOSService payOS) : Endpoint<PurchaseTransferPassRequest, TransferPassDto>
{
    public override void Configure()
    {
        Post(RoutePaths.TransferCreditsApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(PurchaseTransferPassRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserOrDefaultAsync(db, ct);
        var quantity = Math.Clamp(req.Quantity <= 0 ? 1 : req.Quantity, 1, 20);
        var currency = PlanCatalog.NormalizeCurrency(req.Currency);

        var unitPrice = currency switch
        {
            PricingKeys.CurrencyUsd => TransferCreditPass.DefaultPassPriceUsd,
            PricingKeys.CurrencyEur => TransferCreditPass.DefaultPassPriceEur,
            _ => TransferCreditPass.DefaultPassPriceVnd,
        };
        var totalAmount = unitPrice * quantity;

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var orderCode = PayOSService.GenerateOrderCode();

        Guid? intentId = null;
        if (user is not null)
        {
            var intent = new PaymentIntent
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                Code = "QSP-" + token[..6].ToUpperInvariant(),
                PayOSOrderCode = orderCode,
                Plan = PlanTierLimits.Pro,
                Billing = "one_time_pass",
                Amount = totalAmount,
                Currency = currency,
                ExchangeRateSnapshot = PlanCatalog.ExchangeRateFor(currency),
                Provider = PaymentProviders.PayOS,
                Status = PaymentIntentStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
            };
            db.PaymentIntents.Add(intent);
            intentId = intent.Id;
        }

        var pass = new TransferCreditPass
        {
            Id = Guid.CreateVersion7(),
            UserId = user?.Id,
            BuyerEmail = string.IsNullOrWhiteSpace(req.Email) ? user?.Email : req.Email.Trim().ToLowerInvariant(),
            Token = token,
            CreditsRemaining = quantity,
            MaxFileSizeBytes = TransferCreditPass.MaxSingleTransferBytes,
            MaxExpiryDays = TransferCreditPass.ExtendedExpiryDays,
            Status = TransferCreditPass.StatusPendingPayment,
            PaymentIntentId = intentId,
            ExpiresAt = DateTime.UtcNow.AddYears(1),
        };

        db.TransferCreditPasses.Add(pass);
        await db.SaveChangesAsync(ct);

        var payResult = await payOS.CreatePaymentLinkAsync(orderCode, totalAmount, $"QSP-{token[..6].ToUpperInvariant()}", ct);

        await Send.OkAsync(new TransferPassDto(
            pass.Id,
            pass.Token,
            pass.Status,
            pass.CreditsRemaining,
            pass.MaxFileSizeBytes,
            pass.MaxExpiryDays,
            totalAmount,
            currency,
            payResult.CheckoutUrl,
            pass.ExpiresAt), ct);
    }
}

/// <summary>GET /api/credits/transfer-pass/{Token} — inspects a one-time transfer pass by token (issue #85).</summary>
public sealed class GetTransferPassEndpoint(AppDbContext db) : EndpointWithoutRequest<TransferPassDto>
{
    public override void Configure()
    {
        Get(RoutePaths.TransferCreditsApi + "/{Token}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = (Route<string>("Token") ?? string.Empty).Trim().ToLowerInvariant();
        var pass = await db.TransferCreditPasses
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Token == token, ct);

        if (pass is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new TransferPassDto(
            pass.Id,
            pass.Token,
            pass.Status,
            pass.CreditsRemaining,
            pass.MaxFileSizeBytes,
            pass.MaxExpiryDays,
            TransferCreditPass.DefaultPassPriceVnd,
            PricingKeys.DefaultCurrency,
            null,
            pass.ExpiresAt), ct);
    }
}

/// <summary>
/// POST /api/credits/transfer-pass/{Token}/activate — marks a pending pass as paid/available
/// (used by webhook/return callback or admin verification, issue #85).
/// </summary>
public sealed class ActivateTransferPassEndpoint(AppDbContext db) : EndpointWithoutRequest<TransferPassDto>
{
    public override void Configure()
    {
        Post(RoutePaths.TransferCreditsApi + "/{Token}/activate");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = (Route<string>("Token") ?? string.Empty).Trim().ToLowerInvariant();
        var pass = await db.TransferCreditPasses.FirstOrDefaultAsync(p => p.Token == token, ct);
        if (pass is null)
        {
            ThrowError(ErrorCodes.TransferPassNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (pass.Status == TransferCreditPass.StatusPendingPayment)
        {
            pass.Status = TransferCreditPass.StatusAvailable;
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(new TransferPassDto(
            pass.Id,
            pass.Token,
            pass.Status,
            pass.CreditsRemaining,
            pass.MaxFileSizeBytes,
            pass.MaxExpiryDays,
            TransferCreditPass.DefaultPassPriceVnd,
            PricingKeys.DefaultCurrency,
            null,
            pass.ExpiresAt), ct);
    }
}
