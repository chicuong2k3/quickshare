using System.Text.RegularExpressions;
using QuickShare.Features.Payments.Billing;

namespace QuickShare.Features.Deliveries.Sms;

public sealed class SendDeliverySmsRequest
{
    public string RecipientPhone { get; set; } = string.Empty;
    public string? PasswordHint { get; set; }
    public bool RecipientConsentConfirmed { get; set; }
}

public sealed class SmsOptOutRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Keyword { get; set; }
}

public sealed record SmsDeliveryResponse(
    Guid Id,
    string RecipientPhone,
    string MessageBody,
    int CharacterCount,
    string Status,
    decimal CostVnd);

/// <summary>
/// Phone normalization (+84 Vietnam &amp; E.164) and 160-character SMS template builder (issue #269).
/// Never includes the actual share password — only an optional hint.
/// </summary>
public static partial class SmsFormatter
{
    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Regex();

    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new string(raw.Trim().Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        if (digits.StartsWith('0') && digits.Length is 10 or 11)
        {
            // Vietnamese local mobile format (e.g. 0901234567 -> +84901234567)
            digits = "+84" + digits[1..];
        }
        else if (digits.StartsWith("84", StringComparison.Ordinal) && !digits.StartsWith('+'))
        {
            digits = "+" + digits;
        }

        return E164Regex().IsMatch(digits) ? digits : null;
    }

    public static string BuildBody(string senderName, string shareUrl, DateTime expiresAt, string? passwordHint)
    {
        var cleanSender = string.IsNullOrWhiteSpace(senderName) ? "QuickShare" : senderName.Trim();
        if (cleanSender.Length > 20)
            cleanSender = cleanSender[..20];

        var expText = expiresAt.ToString("dd/MM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var hintPart = string.IsNullOrWhiteSpace(passwordHint)
            ? string.Empty
            : $" (Hint: {passwordHint.Trim()[..Math.Min(20, passwordHint.Trim().Length)]})";

        var body = $"[QuickShare] {cleanSender}: {shareUrl} exp {expText}{hintPart}. Reply STOP to opt out";
        if (body.Length > SmsDeliveryLog.MessageMaxLength)
        {
            body = body[..SmsDeliveryLog.MessageMaxLength];
        }

        return body;
    }
}

/// <summary>
/// POST /api/deliveries/{ShareCode}/sms — sends an SMS link for a delivery (issue #269).
/// Enforces PDPD consent, STOP opt-out suppression, 160-char budget, and metered invoice billing.
/// </summary>
public sealed class SendDeliverySmsEndpoint(
    AppDbContext db,
    IConfiguration config,
    InvoiceService invoices) : Endpoint<SendDeliverySmsRequest, SmsDeliveryResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.DeliverySmsApi);
        Description(b => b.WithTags(ApiTags.Deliveries));
    }

    public override async Task HandleAsync(SendDeliverySmsRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            ThrowError(ErrorCodes.RequiresPaidPlan, StatusCodes.Status403Forbidden);
            return;
        }

        if (!req.RecipientConsentConfirmed)
        {
            ThrowError(ErrorCodes.SmsConsentRequired, StatusCodes.Status400BadRequest);
            return;
        }

        var normalizedPhone = SmsFormatter.NormalizePhone(req.RecipientPhone);
        if (normalizedPhone is null)
        {
            ThrowError(ErrorCodes.SmsPhoneInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var shareCode = (Route<string>("ShareCode") ?? string.Empty).Trim();
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == shareCode && d.SenderId == user.Id && d.DeletedAt == null, ct);

        if (delivery is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var isOptedOut = await db.SmsOptOuts
            .AsNoTracking()
            .AnyAsync(o => o.PhoneNumber == normalizedPhone, ct);

        if (isOptedOut)
        {
            ThrowError(ErrorCodes.SmsRecipientOptedOut, StatusCodes.Status409Conflict);
            return;
        }

        var baseUrl = (config["Email:BaseUrl"] ?? "https://quicksharecloud.com").TrimEnd('/');
        var shareUrl = $"{baseUrl}/d/{delivery.ShareCode}";
        var messageBody = SmsFormatter.BuildBody(user.Name, shareUrl, delivery.ExpiresAt ?? DateTime.UtcNow.AddDays(7), req.PasswordHint);

        var log = new SmsDeliveryLog
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            SenderUserId = user.Id,
            RecipientPhone = normalizedPhone,
            MessageBody = messageBody,
            Status = SmsDeliveryLog.StatusSent,
            CostVnd = SmsDeliveryLog.CostPerSmsVnd,
        };
        db.SmsDeliveryLogs.Add(log);

        // Metered SMS billing on the user's open draft invoice (billing invariant #278/#141).
        await invoices.AddOrUpdateLineAsync(
            user.Id,
            "sms_delivery",
            $"SMS share notification ({normalizedPhone})",
            1m,
            SmsDeliveryLog.CostPerSmsVnd,
            ct);

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new SmsDeliveryResponse(
            log.Id,
            log.RecipientPhone,
            log.MessageBody,
            log.MessageBody.Length,
            log.Status,
            log.CostVnd), ct);
    }
}

/// <summary>POST /api/webhooks/sms/opt-out — records a recipient STOP opt-out so future SMS sends are suppressed (issue #269).</summary>
public sealed class SmsOptOutEndpoint(AppDbContext db) : Endpoint<SmsOptOutRequest>
{
    public override void Configure()
    {
        Post(RoutePaths.SmsOptOutApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Deliveries));
    }

    public override async Task HandleAsync(SmsOptOutRequest req, CancellationToken ct)
    {
        var normalizedPhone = SmsFormatter.NormalizePhone(req.PhoneNumber);
        if (normalizedPhone is null)
        {
            ThrowError(ErrorCodes.SmsPhoneInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var exists = await db.SmsOptOuts.AnyAsync(o => o.PhoneNumber == normalizedPhone, ct);
        if (!exists)
        {
            db.SmsOptOuts.Add(new SmsOptOut
            {
                Id = Guid.CreateVersion7(),
                PhoneNumber = normalizedPhone,
                Reason = string.IsNullOrWhiteSpace(req.Keyword) ? "STOP" : req.Keyword.Trim().ToUpperInvariant(),
            });
            await db.SaveChangesAsync(ct);
        }

        await Send.OkAsync(new { phoneNumber = normalizedPhone, optedOut = true }, ct);
    }
}
