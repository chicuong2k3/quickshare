using System.Text.RegularExpressions;
using QuickShare.Extensions;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Payments.Billing;

public sealed record BillingProfileDto(
    string CompanyName,
    string TaxCode,
    string Address,
    string BillingEmail,
    bool IsBusiness);

public sealed class BillingProfileRequest
{
    public string? CompanyName { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? BillingEmail { get; set; }
}

/// <summary>
/// Vietnamese tax codes (mã số thuế): ten digits, optionally followed by a three-digit branch
/// suffix. Validated because it is copied onto every invoice we issue — a typo here is a typo
/// on a legal document the buyer files with their tax authority.
/// </summary>
internal static partial class TaxCodes
{
    [GeneratedRegex(@"^\d{10}(-\d{3})?$")]
    private static partial Regex Pattern();

    public static bool IsValid(string value) => Pattern().IsMatch(value);
}

/// <summary>GET /api/billing/profile — the buyer's invoicing identity (issue #112).</summary>
public sealed class GetBillingProfileEndpoint : EndpointWithoutRequest<BillingProfileDto>
{
    public override void Configure()
    {
        Get("/api/billing/profile");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Get the caller's billing profile";
            s.Description = "Company name, tax code and address for VAT invoicing.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var profile = await db.BillingProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, ct);

        await Send.OkAsync(Map(profile), ct);
    }

    internal static BillingProfileDto Map(BillingProfile? profile) => new(
        profile?.CompanyName ?? string.Empty,
        profile?.TaxCode ?? string.Empty,
        profile?.Address ?? string.Empty,
        profile?.BillingEmail ?? string.Empty,
        profile?.IsBusiness ?? false);
}

/// <summary>
/// PUT /api/billing/profile — set the invoicing identity (issue #112).
/// </summary>
public sealed class UpdateBillingProfileEndpoint : Endpoint<BillingProfileRequest, BillingProfileDto>
{
    public override void Configure()
    {
        Put("/api/billing/profile");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Set the caller's billing profile";
            s.Description = "A company name plus a valid tax code turns receipts into VAT invoices.";
        });
    }

    public override async Task HandleAsync(BillingProfileRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var taxCode = (req.TaxCode ?? string.Empty).Trim();
        if (taxCode.Length > 0 && !TaxCodes.IsValid(taxCode))
        {
            ThrowError(ErrorCodes.TaxCodeInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var profile = await db.BillingProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is null)
        {
            profile = new BillingProfile
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                CreatedAt = DateTime.UtcNow,
            };
            db.BillingProfiles.Add(profile);
        }

        profile.CompanyName = Trim(req.CompanyName, BillingProfile.CompanyNameMaxLength);
        profile.TaxCode = taxCode;
        profile.Address = Trim(req.Address, BillingProfile.AddressMaxLength);
        profile.BillingEmail = Trim(req.BillingEmail, BillingProfile.CompanyNameMaxLength);
        profile.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(GetBillingProfileEndpoint.Map(profile), ct);
    }

    private static string Trim(string? value, int max)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
