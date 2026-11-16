using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using QuickShare.Extensions;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Payments.Billing;

public sealed record BillingProfileDto(
    string CompanyName,
    string TaxCode,
    string Address,
    string BillingEmail,
    string PoNumber,
    int NetTermsDays,
    bool NetTermsApproved,
    bool IsBusiness);

public sealed class BillingProfileRequest
{
    public string? CompanyName { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? BillingEmail { get; set; }
    public string? PoNumber { get; set; }
    public int NetTermsDays { get; set; }
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

/// <summary>GET /api/billing/profile — the buyer's invoicing identity (issues #112, #192).</summary>
public sealed class GetBillingProfileEndpoint : EndpointWithoutRequest<BillingProfileDto>
{
    public override void Configure()
    {
        Get("/api/billing/profile");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Get the caller's billing profile";
            s.Description = "Company name, tax code and address for VAT invoicing, plus the purchase-order reference and payment terms procurement requires.";
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
        profile?.PoNumber ?? string.Empty,
        profile?.NetTermsDays ?? 0,
        profile?.NetTermsApprovedAt is not null,
        profile?.IsBusiness ?? false);
}

/// <summary>
/// PUT /api/billing/profile — set the invoicing identity (issues #112, #192).
///
/// Asking for net terms here does not grant them. Terms are a credit decision, so the request
/// is stored and stays inert until a platform admin approves it; self-service credit is how a
/// SaaS discovers bad debt. Changing the requested terms after approval clears the approval,
/// because "approved for 30 days" is not approval for 60.
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
            s.Description = "A company name plus a valid tax code turns receipts into VAT invoices. Requested net terms are recorded but only take effect once approved.";
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

        if (!BillingProfile.AllowedNetTermsDays.Contains(req.NetTermsDays))
        {
            ThrowError(ErrorCodes.NetTermsInvalid, StatusCodes.Status400BadRequest);
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

        // Asking for different terms is a new credit question, so the old answer is dropped.
        if (profile.NetTermsDays != req.NetTermsDays)
        {
            profile.NetTermsApprovedAt = null;
            profile.NetTermsApprovedById = null;
        }

        profile.CompanyName = Trim(req.CompanyName, BillingProfile.CompanyNameMaxLength);
        profile.TaxCode = taxCode;
        profile.Address = Trim(req.Address, BillingProfile.AddressMaxLength);
        profile.BillingEmail = Trim(req.BillingEmail, BillingProfile.CompanyNameMaxLength);
        profile.PoNumber = Trim(req.PoNumber, BillingProfile.PoNumberMaxLength);
        profile.NetTermsDays = req.NetTermsDays;
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

public sealed class ApproveNetTermsRequest
{
    public Guid UserId { get; set; }
    public bool Approved { get; set; }
}

/// <summary>
/// POST /api/admin/billing/net-terms — grant or withdraw payment terms (issue #192).
///
/// Platform admins only, and deliberately manual: extending credit is a risk decision about a
/// specific customer, not a setting they can toggle for themselves.
/// </summary>
public sealed class ApproveNetTermsEndpoint : Endpoint<ApproveNetTermsRequest, BillingProfileDto>
{
    public override void Configure()
    {
        Post("/api/admin/billing/net-terms");
        Description(b => b.WithTags(ApiTags.Admin));
        Summary(s =>
        {
            s.Summary = "Approve or withdraw a buyer's net payment terms";
            s.Description = "Platform admins only. Until approved, a buyer's requested terms are recorded but every invoice is still due on issue.";
        });
    }

    public override async Task HandleAsync(ApproveNetTermsRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var admin = await HttpContext.User.GetUserAsync(db, ct);

        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var profile = await db.BillingProfiles.FirstOrDefaultAsync(p => p.UserId == req.UserId, ct);
        if (profile is null)
        {
            ThrowError(ErrorCodes.NotFound, StatusCodes.Status404NotFound);
            return;
        }

        profile.NetTermsApprovedAt = req.Approved ? DateTime.UtcNow : null;
        profile.NetTermsApprovedById = req.Approved ? admin.Id : null;
        profile.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(GetBillingProfileEndpoint.Map(profile), ct);
    }
}
