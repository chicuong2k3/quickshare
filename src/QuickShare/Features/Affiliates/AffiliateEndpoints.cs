using System.Security.Cryptography;

namespace QuickShare.Features.Affiliates;

public sealed class EnrollAffiliateRequest
{
    public string? CustomCode { get; set; }
    public string PayoutMethod { get; set; } = "bank_transfer";
    public string PayoutDetails { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public bool TermsAccepted { get; set; }
}

public sealed record AffiliateConversionDto(
    Guid Id,
    string Plan,
    decimal SaleAmountVnd,
    decimal CommissionAmountVnd,
    string Status,
    DateTime CreatedAt);

public sealed record AffiliateDashboardDto(
    Guid Id,
    string Code,
    string ShareUrl,
    int CommissionPercent,
    string PayoutMethod,
    string PayoutDetails,
    string? TaxCode,
    long ClicksCount,
    long SignupsCount,
    long PaidConversionsCount,
    decimal TotalEarnedVnd,
    decimal PendingPayoutVnd,
    string Status,
    IReadOnlyList<AffiliateConversionDto> RecentConversions);

/// <summary>
/// Affiliate partner enrollment, click tracking, and conversion attribution (issue #298).
/// </summary>
public static class AffiliateService
{
    public const string AffiliateCookieName = "qs_aff";

    public static async Task RecordPaidConversionAsync(
        AppDbContext db,
        User buyer,
        PaymentIntent intent,
        CancellationToken ct)
    {
        if (buyer.ReferredByUserId is not { } referrerUserId || referrerUserId == buyer.Id)
            return;

        var profile = await db.AffiliateProfiles
            .FirstOrDefaultAsync(a => a.UserId == referrerUserId && a.Status == AffiliateProfile.StatusActive, ct);

        if (profile is null)
            return;

        // Only reward first paid conversion per referred user (self-referral and duplicate guard).
        var alreadyConverted = await db.AffiliateConversions
            .AnyAsync(c => c.AffiliateProfileId == profile.Id && c.ReferredUserId == buyer.Id, ct);

        if (alreadyConverted)
            return;

        var saleVnd = intent.Amount * (intent.ExchangeRateSnapshot <= 0 ? 1m : intent.ExchangeRateSnapshot);
        var commissionVnd = Math.Round(saleVnd * profile.CommissionPercent / 100m, 0);

        db.AffiliateConversions.Add(new AffiliateConversion
        {
            Id = Guid.CreateVersion7(),
            AffiliateProfileId = profile.Id,
            ReferredUserId = buyer.Id,
            PaymentIntentId = intent.Id,
            Plan = intent.Plan,
            SaleAmountVnd = saleVnd,
            CommissionAmountVnd = commissionVnd,
            Status = AffiliateConversion.StatusApproved,
        });

        profile.PaidConversionsCount++;
        profile.TotalEarnedVnd += commissionVnd;
        profile.PendingPayoutVnd += commissionVnd;
    }
}

/// <summary>POST /api/affiliate/enroll — enrolls the authenticated user in the affiliate program (issue #298).</summary>
public sealed class EnrollAffiliateEndpoint(AppDbContext db, IConfiguration config)
    : Endpoint<EnrollAffiliateRequest, AffiliateDashboardDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AffiliateApi);
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(EnrollAffiliateRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);

        if (!req.TermsAccepted || string.IsNullOrWhiteSpace(req.PayoutDetails))
        {
            ThrowError(ErrorCodes.AffiliateTermsRequired, StatusCodes.Status400BadRequest);
            return;
        }

        var profile = await db.AffiliateProfiles.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (profile is null)
        {
            var desiredCode = string.IsNullOrWhiteSpace(req.CustomCode)
                ? "aff-" + user.Id.ToString("N")[^8..]
                : req.CustomCode.Trim().ToLowerInvariant();

            if (desiredCode.Length is < 3 or > AffiliateProfile.CodeMaxLength
                || desiredCode.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_'))
            {
                ThrowError(ErrorCodes.AffiliateCodeInvalid, StatusCodes.Status400BadRequest);
                return;
            }

            if (await db.AffiliateProfiles.AnyAsync(a => a.Code == desiredCode, ct))
            {
                ThrowError(ErrorCodes.AffiliateCodeTaken, StatusCodes.Status409Conflict);
                return;
            }

            profile = new AffiliateProfile
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                Code = desiredCode,
                CommissionPercent = AffiliateProfile.DefaultCommissionPercent,
                PayoutMethod = string.IsNullOrWhiteSpace(req.PayoutMethod) ? "bank_transfer" : req.PayoutMethod.Trim(),
                PayoutDetails = req.PayoutDetails.Trim()[..Math.Min(req.PayoutDetails.Trim().Length, AffiliateProfile.PayoutInfoMaxLength)],
                TaxCode = string.IsNullOrWhiteSpace(req.TaxCode) ? null : req.TaxCode.Trim(),
                TermsAccepted = true,
                Status = AffiliateProfile.StatusActive,
            };
            db.AffiliateProfiles.Add(profile);
        }
        else
        {
            profile.PayoutMethod = string.IsNullOrWhiteSpace(req.PayoutMethod) ? profile.PayoutMethod : req.PayoutMethod.Trim();
            profile.PayoutDetails = req.PayoutDetails.Trim()[..Math.Min(req.PayoutDetails.Trim().Length, AffiliateProfile.PayoutInfoMaxLength)];
            profile.TaxCode = string.IsNullOrWhiteSpace(req.TaxCode) ? null : req.TaxCode.Trim();
            profile.TermsAccepted = true;
        }

        await db.SaveChangesAsync(ct);

        var baseUrl = (config["Email:BaseUrl"] ?? "https://quicksharecloud.com").TrimEnd('/');
        await Send.OkAsync(new AffiliateDashboardDto(
            profile.Id,
            profile.Code,
            $"{baseUrl}/?aff={profile.Code}",
            profile.CommissionPercent,
            profile.PayoutMethod,
            profile.PayoutDetails,
            profile.TaxCode,
            profile.ClicksCount,
            profile.SignupsCount,
            profile.PaidConversionsCount,
            profile.TotalEarnedVnd,
            profile.PendingPayoutVnd,
            profile.Status,
            []), ct);
    }
}

/// <summary>GET /api/affiliate — returns the authenticated user's affiliate dashboard (issue #298).</summary>
public sealed class GetAffiliateDashboardEndpoint(AppDbContext db, IConfiguration config)
    : EndpointWithoutRequest<AffiliateDashboardDto>
{
    public override void Configure()
    {
        Get(RoutePaths.AffiliateApi);
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var profile = await db.AffiliateProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == user.Id, ct);

        if (profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var conversions = await db.AffiliateConversions
            .AsNoTracking()
            .Where(c => c.AffiliateProfileId == profile.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Take(25)
            .Select(c => new AffiliateConversionDto(
                c.Id,
                c.Plan,
                c.SaleAmountVnd,
                c.CommissionAmountVnd,
                c.Status,
                c.CreatedAt))
            .ToListAsync(ct);

        var baseUrl = (config["Email:BaseUrl"] ?? "https://quicksharecloud.com").TrimEnd('/');
        await Send.OkAsync(new AffiliateDashboardDto(
            profile.Id,
            profile.Code,
            $"{baseUrl}/?aff={profile.Code}",
            profile.CommissionPercent,
            profile.PayoutMethod,
            profile.PayoutDetails,
            profile.TaxCode,
            profile.ClicksCount,
            profile.SignupsCount,
            profile.PaidConversionsCount,
            profile.TotalEarnedVnd,
            profile.PendingPayoutVnd,
            profile.Status,
            conversions), ct);
    }
}

/// <summary>POST /api/affiliate/{Code}/click — tracks an anonymous click on an affiliate link and sets 90-day cookie (issue #298).</summary>
public sealed class TrackAffiliateClickEndpoint(AppDbContext db) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post(RoutePaths.AffiliateApi + "/{Code}/click");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Analytics));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var code = (Route<string>("Code") ?? string.Empty).Trim().ToLowerInvariant();
        var profile = await db.AffiliateProfiles.FirstOrDefaultAsync(
            a => a.Code == code && a.Status == AffiliateProfile.StatusActive, ct);

        if (profile is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        profile.ClicksCount++;
        await db.SaveChangesAsync(ct);

        HttpContext.Response.Cookies.Append(AffiliateService.AffiliateCookieName, profile.Code, new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(90),
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
        });

        await Send.OkAsync(new { code = profile.Code, clicks = profile.ClicksCount }, ct);
    }
}
