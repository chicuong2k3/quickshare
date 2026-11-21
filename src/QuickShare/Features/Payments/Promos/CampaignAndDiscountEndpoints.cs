using System.Globalization;
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
