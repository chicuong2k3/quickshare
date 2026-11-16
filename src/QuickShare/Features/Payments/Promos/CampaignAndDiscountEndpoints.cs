using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Payments.Promos;

public sealed record ActiveCampaignDto(
    string Code,
    string CampaignName,
    string? Description,
    string Kind,
    int Value,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    string? TargetCountry,
    bool AutoApplyOnCheckout);

public sealed class CreateCampaignRequest
{
    public string? Template { get; set; }
    public string Code { get; set; } = string.Empty;
    public string CampaignName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Kind { get; set; } = PromoKind.Percent;
    public int Value { get; set; } = 30;
    public int DurationDays { get; set; } = 14;
    public int? MaxRedemptions { get; set; }
    public bool AutoApplyOnCheckout { get; set; } = true;
    public string? TargetCountry { get; set; }
    public bool FirstTimeOnly { get; set; }
}

public sealed record CampaignAdminDto(
    Guid Id,
    string Code,
    string CampaignName,
    string? Description,
    string Kind,
    int Value,
    int RedeemedCount,
    int? MaxRedemptions,
    DateTime? StartsAt,
    DateTime? ExpiresAt,
    bool AutoApplyOnCheckout,
    string? TargetCountry,
    long TotalDiscountAmount);

/// <summary>GET /api/campaigns/active — returns the live seasonal campaign for banners and checkout auto-fill (issue #183).</summary>
public sealed class GetActiveCampaignEndpoint(AppDbContext db, CacheService cache)
    : EndpointWithoutRequest<ActiveCampaignDto?>
{
    public const string CacheKey = "campaigns:active";

    public override void Configure()
    {
        Get(RoutePaths.ActiveCampaignsApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var active = await ResolveActiveAsync(db, cache, HttpContext, ct);
        await Send.OkAsync(active, ct);
    }

    public static async Task<ActiveCampaignDto?> ResolveActiveAsync(
        AppDbContext db, CacheService cache, HttpContext? httpContext, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var country = httpContext?.Request.Headers[AnalyticsConstants.CountryHeader].FirstOrDefault()?.Trim().ToUpperInvariant();

        var candidates = await cache.GetOrSetAsync(
            CacheKey,
            async token => await db.PromoCodes
                .AsNoTracking()
                .Where(p => p.RevokedAt == null && p.CampaignName != null && p.AutoApplyOnCheckout)
                .OrderByDescending(p => p.Value)
                .ToListAsync(token),
            duration: TimeSpan.FromMinutes(1),
            token: ct);

        var match = candidates.FirstOrDefault(p =>
            p.IsLive(now)
            && (string.IsNullOrWhiteSpace(p.TargetCountry)
                || string.IsNullOrWhiteSpace(country)
                || string.Equals(p.TargetCountry, country, StringComparison.OrdinalIgnoreCase)));

        return match is null
            ? null
            : new ActiveCampaignDto(
                match.Code,
                match.CampaignName!,
                match.Description,
                match.Kind,
                match.Value,
                match.StartsAt,
                match.ExpiresAt,
                match.TargetCountry,
                match.AutoApplyOnCheckout);
    }
}

/// <summary>POST /api/admin/campaigns — create a seasonal campaign or instantiate a built-in Tet / Black Friday template (issue #183).</summary>
public sealed class CreateCampaignEndpoint(
    AppDbContext db,
    CacheService cache,
    IOptions<AdminOptions> adminOptions) : Endpoint<CreateCampaignRequest, CampaignAdminDto>
{
    public const string TemplateTet = "tet";
    public const string TemplateBlackFriday = "black_friday";

    public override void Configure()
    {
        Post(RoutePaths.AdminCampaignsApi);
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CreateCampaignRequest req, CancellationToken ct)
    {
        if (!User.IsAdmin(adminOptions))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var now = DateTime.UtcNow;
        var code = PromoCode.NormalizeCode(req.Code);
        var campaignName = req.CampaignName?.Trim() ?? string.Empty;
        var description = req.Description?.Trim();
        var value = req.Value;
        var durationDays = req.DurationDays > 0 ? req.DurationDays : 14;
        var targetCountry = string.IsNullOrWhiteSpace(req.TargetCountry) ? null : req.TargetCountry.Trim().ToUpperInvariant();

        if (string.Equals(req.Template, TemplateTet, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(code))
            {
                code = $"TET{now.Year.ToString(CultureInfo.InvariantCulture)}";
            }
            if (string.IsNullOrEmpty(campaignName))
            {
                campaignName = $"Tết Nguyên Đán {now.Year.ToString(CultureInfo.InvariantCulture)}";
            }
            description ??= "Ưu đãi Tết Nguyên Đán — giảm 30% gói Pro & Studio";
            value = value > 0 ? value : 30;
            durationDays = 14;
            targetCountry ??= "VN";
        }
        else if (string.Equals(req.Template, TemplateBlackFriday, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(code))
            {
                code = $"BF{now.Year.ToString(CultureInfo.InvariantCulture)}";
            }
            if (string.IsNullOrEmpty(campaignName))
            {
                campaignName = $"Black Friday {now.Year.ToString(CultureInfo.InvariantCulture)}";
            }
            description ??= "Black Friday — 40% off Pro & Studio";
            value = value > 0 ? value : 40;
            durationDays = 7;
        }

        if (string.IsNullOrEmpty(code) || code.Length > PromoCode.CodeMaxLength || string.IsNullOrEmpty(campaignName) || value <= 0 || value > PromoCode.MaxPercent)
        {
            ThrowError(ErrorCodes.CampaignCodeInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (promo is null)
        {
            promo = new PromoCode
            {
                Id = Guid.CreateVersion7(),
                Code = code,
            };
            db.PromoCodes.Add(promo);
        }

        promo.CampaignName = campaignName.Length > PromoCode.CampaignMaxLength ? campaignName[..PromoCode.CampaignMaxLength] : campaignName;
        promo.Description = description is { Length: > PromoCode.DescriptionMaxLength } ? description[..PromoCode.DescriptionMaxLength] : description;
        promo.Kind = req.Kind == PromoKind.Fixed ? PromoKind.Fixed : PromoKind.Percent;
        promo.Value = value;
        promo.StartsAt = now.AddMinutes(-1);
        promo.ExpiresAt = now.AddDays(durationDays);
        promo.MaxRedemptions = req.MaxRedemptions;
        promo.AutoApplyOnCheckout = req.AutoApplyOnCheckout;
        promo.TargetCountry = targetCountry;
        promo.FirstTimeOnly = req.FirstTimeOnly;
        promo.RevokedAt = null;

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(GetActiveCampaignEndpoint.CacheKey, ct);
        StaticCache.Instance.Remove(GetActiveCampaignEndpoint.CacheKey);

        await Send.OkAsync(new CampaignAdminDto(
            promo.Id,
            promo.Code,
            promo.CampaignName!,
            promo.Description,
            promo.Kind,
            promo.Value,
            promo.RedeemedCount,
            promo.MaxRedemptions,
            promo.StartsAt,
            promo.ExpiresAt,
            promo.AutoApplyOnCheckout,
            promo.TargetCountry,
            0L), ct);
    }
}

/// <summary>GET /api/admin/campaigns — list seasonal campaigns with redemption and discount totals (issue #183).</summary>
public sealed class ListCampaignsEndpoint(
    AppDbContext db,
    IOptions<AdminOptions> adminOptions) : EndpointWithoutRequest<List<CampaignAdminDto>>
{
    public override void Configure()
    {
        Get(RoutePaths.AdminCampaignsApi);
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(adminOptions))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var rows = await db.PromoCodes
            .AsNoTracking()
            .Where(p => p.CampaignName != null)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new CampaignAdminDto(
                p.Id,
                p.Code,
                p.CampaignName!,
                p.Description,
                p.Kind,
                p.Value,
                p.RedeemedCount,
                p.MaxRedemptions,
                p.StartsAt,
                p.ExpiresAt,
                p.AutoApplyOnCheckout,
                p.TargetCountry,
                db.PromoRedemptions.Where(r => r.PromoCodeId == p.Id).Sum(r => (long?)r.DiscountAmount) ?? 0L))
            .ToListAsync(ct);

        await Send.OkAsync(rows, ct);
    }
}

public sealed class SubmitDiscountApplicationRequest
{
    public string Kind { get; set; } = DiscountApplication.KindStudent;
    public string OrganizationName { get; set; } = string.Empty;
    public string VerificationEmail { get; set; } = string.Empty;
    public string? ProofReference { get; set; }
}

public sealed record DiscountApplicationDto(
    Guid Id,
    Guid UserId,
    string Kind,
    string OrganizationName,
    string VerificationEmail,
    string? ProofReference,
    string Status,
    string? IssuedPromoCode,
    int PercentOff,
    DateTime? VerifiedAt,
    DateTime? ExpiresAt);

public sealed class ReviewDiscountApplicationRequest
{
    public bool Approve { get; set; } = true;
}

/// <summary>GET /api/account/discount-application — returns the caller's student/nonprofit verification state (issue #203).</summary>
public sealed class GetMyDiscountApplicationEndpoint(AppDbContext db)
    : EndpointWithoutRequest<DiscountApplicationDto?>
{
    public override void Configure()
    {
        Get(RoutePaths.AccountDiscountApi);
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var app = await db.DiscountApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == user.Id, ct);

        await Send.OkAsync(app is null ? null : Map(app), ct);
    }

    internal static DiscountApplicationDto Map(DiscountApplication app) => new(
        app.Id,
        app.UserId,
        app.Kind,
        app.OrganizationName,
        app.VerificationEmail,
        app.ProofReference,
        app.Status,
        app.IssuedPromoCode,
        app.Kind == DiscountApplication.KindNonprofit
            ? DiscountApplication.NonprofitPercentOff
            : DiscountApplication.StudentPercentOff,
        app.VerifiedAt,
        app.ExpiresAt);
}

/// <summary>
/// POST /api/account/discount-application — submit student or nonprofit verification (issue #203).
///
/// Academic email domains (<c>.edu</c>, <c>.edu.vn</c>, <c>.ac.vn</c>, <c>.ac.uk</c>) are
/// auto-verified immediately and issue a single-use, non-shareable promo code valid for 1 year.
/// Other student proofs and nonprofit registrations enter the admin review queue.
/// </summary>
public sealed class SubmitDiscountApplicationEndpoint(AppDbContext db)
    : Endpoint<SubmitDiscountApplicationRequest, DiscountApplicationDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AccountDiscountApi);
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(SubmitDiscountApplicationRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var kind = string.Equals(req.Kind, DiscountApplication.KindNonprofit, StringComparison.OrdinalIgnoreCase)
            ? DiscountApplication.KindNonprofit
            : DiscountApplication.KindStudent;

        var org = req.OrganizationName?.Trim() ?? string.Empty;
        var email = req.VerificationEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        var proof = string.IsNullOrWhiteSpace(req.ProofReference) ? null : req.ProofReference.Trim();

        if (string.IsNullOrEmpty(org) || org.Length > DiscountApplication.OrganizationMaxLength
            || string.IsNullOrEmpty(email) || !email.Contains('@', StringComparison.Ordinal) || email.Length > DiscountApplication.VerificationEmailMaxLength)
        {
            ThrowError(ErrorCodes.DiscountApplicationInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var app = await db.DiscountApplications.FirstOrDefaultAsync(a => a.UserId == user.Id, ct);
        if (app is null)
        {
            app = new DiscountApplication
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
            };
            db.DiscountApplications.Add(app);
        }

        app.Kind = kind;
        app.OrganizationName = org;
        app.VerificationEmail = email;
        app.ProofReference = proof is { Length: > DiscountApplication.ProofReferenceMaxLength }
            ? proof[..DiscountApplication.ProofReferenceMaxLength]
            : proof;

        var now = DateTime.UtcNow;
        var autoVerify = kind == DiscountApplication.KindStudent
            && DiscountApplication.IsAutoVerifiedAcademicEmail(email);

        if (autoVerify)
        {
            await IssueDiscountCodeAsync(db, app, now, ct);
        }
        else
        {
            app.Status = DiscountApplication.StatusPending;
            app.VerifiedAt = null;
            app.ExpiresAt = null;
        }

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            EventType = SecurityEventType.DiscountVerificationSubmitted,
            IpHash = string.Empty,
            UserAgentLabel = nameof(SubmitDiscountApplicationEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { app.Kind, app.Status, app.OrganizationName }),
        });

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(GetMyDiscountApplicationEndpoint.Map(app), ct);
    }

    internal static async Task IssueDiscountCodeAsync(
        AppDbContext db, DiscountApplication app, DateTime now, CancellationToken ct)
    {
        var prefix = app.Kind == DiscountApplication.KindNonprofit ? "NPO" : "EDU";
        var suffix = app.UserId.ToString("N")[^10..].ToUpperInvariant();
        var code = $"{prefix}-{suffix}";
        var percent = app.Kind == DiscountApplication.KindNonprofit
            ? DiscountApplication.NonprofitPercentOff
            : DiscountApplication.StudentPercentOff;

        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (promo is null)
        {
            promo = new PromoCode
            {
                Id = Guid.CreateVersion7(),
                Code = code,
            };
            db.PromoCodes.Add(promo);
        }

        promo.Kind = PromoKind.Percent;
        promo.Value = percent;
        promo.Description = $"Verified {app.Kind} discount ({app.OrganizationName})";
        promo.AppliesToPlans = PlanTierLimits.Pro;
        promo.MaxRedemptions = 1;
        promo.RedeemedCount = 0;
        promo.ExpiresAt = now.AddYears(1);
        promo.RevokedAt = null;

        app.Status = DiscountApplication.StatusApproved;
        app.IssuedPromoCode = code;
        app.VerifiedAt = now;
        app.ExpiresAt = now.AddYears(1);
    }
}

/// <summary>POST /api/admin/discount-applications/{Id}/review — approve or reject a pending student/nonprofit application (issue #203).</summary>
public sealed class ReviewDiscountApplicationEndpoint(
    AppDbContext db,
    IOptions<AdminOptions> adminOptions) : Endpoint<ReviewDiscountApplicationRequest, DiscountApplicationDto>
{
    public override void Configure()
    {
        Post(RoutePaths.AdminDiscountsApi + "/{Id}/review");
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(ReviewDiscountApplicationRequest req, CancellationToken ct)
    {
        if (!User.IsAdmin(adminOptions))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var id = Route<Guid>("Id");
        var app = await db.DiscountApplications.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (app is null)
        {
            ThrowError(ErrorCodes.DiscountApplicationNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var now = DateTime.UtcNow;
        if (req.Approve)
        {
            await SubmitDiscountApplicationEndpoint.IssueDiscountCodeAsync(db, app, now, ct);
        }
        else
        {
            app.Status = DiscountApplication.StatusRejected;
            app.IssuedPromoCode = null;
            app.VerifiedAt = null;
            app.ExpiresAt = null;
        }

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = app.UserId,
            EventType = SecurityEventType.DiscountVerificationDecided,
            IpHash = string.Empty,
            UserAgentLabel = nameof(ReviewDiscountApplicationEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { app.Kind, app.Status, app.IssuedPromoCode }),
        });

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(GetMyDiscountApplicationEndpoint.Map(app), ct);
    }
}
