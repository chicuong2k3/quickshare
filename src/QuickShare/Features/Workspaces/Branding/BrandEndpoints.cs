using System.Text.Json;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Workspaces.Branding;

public sealed record BrandDto(
    bool Enabled,
    bool HasLogo,
    string? AccentColor,
    string? AccentTextColor,
    string? HeaderText,
    string? FooterText,
    bool HideQuickShareBranding,
    string? HeadingFontName,
    string? BodyFontName,
    bool HasHeadingFont,
    bool HasBodyFont,
    string? FontLicenseNote);

public sealed class BrandRequest
{
    public bool Enabled { get; set; }
    public string? AccentColor { get; set; }
    public string? AccentTextColor { get; set; }
    public string? HeaderText { get; set; }
    public string? FooterText { get; set; }
    public bool HideQuickShareBranding { get; set; }
    public string? HeadingFontName { get; set; }
    public string? BodyFontName { get; set; }
    public string? FontLicenseNote { get; set; }
}

/// <summary>GET /api/workspace/brand — the workspace's recipient-facing brand (issues #285, #286).</summary>
public sealed class GetBrandEndpoint : EndpointWithoutRequest<BrandDto?>
{
    public override void Configure()
    {
        Get("/api/workspace/brand");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Get the workspace brand";
            s.Description = "Workspace admins only. Colours, header and footer text, and which assets are uploaded.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var brand = await db.WorkspaceBrands.AsNoTracking()
            .FirstOrDefaultAsync(b => b.WorkspaceId == seat.WorkspaceId, ct);

        await Send.OkAsync(brand is null ? null : Map(brand), ct);
    }

    internal static BrandDto Map(WorkspaceBrand brand) => new(
        brand.Enabled,
        brand.LogoKey is not null,
        brand.AccentColor,
        brand.AccentTextColor,
        brand.HeaderText,
        brand.FooterText,
        brand.HideQuickShareBranding,
        brand.HeadingFontName,
        brand.BodyFontName,
        brand.HeadingFontKey is not null,
        brand.BodyFontKey is not null,
        brand.FontLicenseNote);
}

/// <summary>
/// PUT /api/workspace/brand — set colours and copy (issues #285, #286).
///
/// The accent pair is contrast-checked at 3:1. A brand that can make its own recipient page
/// unreadable is not self-expression, it is a support ticket from the client who could not find
/// the download button; the point is looking like the agency, not looking like nothing.
/// </summary>
public sealed class UpdateBrandEndpoint : Endpoint<BrandRequest, BrandDto>
{
    public override void Configure()
    {
        Put("/api/workspace/brand");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Set the workspace brand";
            s.Description = "Workspace admins only. Accent colours are contrast-checked; font family names are restricted to characters that are safe inside a CSS declaration. Type sizes are not configurable — only families change.";
        });
    }

    public override async Task HandleAsync(BrandRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var accent = Normalize(req.AccentColor);
        var accentText = Normalize(req.AccentTextColor);

        if (accent is not null && !BrandAssets.IsValidColor(accent))
        {
            ThrowError(ErrorCodes.BrandColorInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (accentText is not null && !BrandAssets.IsValidColor(accentText))
        {
            ThrowError(ErrorCodes.BrandColorInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (accent is not null && accentText is not null
            && BrandAssets.ContrastRatio(accent, accentText) < BrandAssets.MinContrastRatio)
        {
            ThrowError(ErrorCodes.BrandContrastTooLow, StatusCodes.Status400BadRequest);
            return;
        }

        var headingFont = Normalize(req.HeadingFontName);
        var bodyFont = Normalize(req.BodyFontName);

        if ((headingFont is not null && !BrandAssets.IsValidFontName(headingFont))
            || (bodyFont is not null && !BrandAssets.IsValidFontName(bodyFont)))
        {
            ThrowError(ErrorCodes.BrandFontNameInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var brand = await db.WorkspaceBrands.FirstOrDefaultAsync(b => b.WorkspaceId == seat.WorkspaceId, ct);
        if (brand is null)
        {
            brand = new WorkspaceBrand
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = seat.WorkspaceId,
                CreatedAt = DateTime.UtcNow,
            };
            db.WorkspaceBrands.Add(brand);
        }

        brand.Enabled = req.Enabled;
        brand.AccentColor = accent;
        brand.AccentTextColor = accentText;
        brand.HeaderText = Truncate(req.HeaderText, WorkspaceBrand.HeaderMaxLength);
        brand.FooterText = Truncate(req.FooterText, WorkspaceBrand.FooterMaxLength);
        brand.HideQuickShareBranding = req.HideQuickShareBranding;
        brand.HeadingFontName = headingFont;
        brand.BodyFontName = bodyFont;
        brand.FontLicenseNote = Truncate(req.FontLicenseNote, WorkspaceBrand.LicenseNoteMaxLength);
        brand.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await BrandReader.EvictAsync(Resolve<CacheService>(), seat.WorkspaceId, ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceBrandChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { brand.Enabled, brand.HideQuickShareBranding }));

        await Send.OkAsync(GetBrandEndpoint.Map(brand), ct);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? Truncate(string? value, int max)
    {
        var trimmed = Normalize(value);
        if (trimmed is null)
            return null;

        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}

/// <summary>
/// POST /api/workspace/brand/assets/{Kind} — upload a logo or a woff2 font (issues #285, #286).
///
/// Files are sniffed by signature, not by the declared content type, which is only the
/// uploader's claim about their own file. Replacing an asset writes a new key and deletes the
/// old one: the far-future cache header on the public route means an in-place overwrite would
/// leave a client's browser showing last month's logo.
/// </summary>
public sealed class UploadBrandAssetEndpoint : EndpointWithoutRequest<BrandDto>
{
    public override void Configure()
    {
        Post("/api/workspace/brand/assets/{Kind}");
        AllowFileUploads();
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Upload a brand asset (logo, heading-font, body-font)";
            s.Description = "Workspace admins only. Logos: PNG, JPEG or WebP, 512 KB. Fonts: woff2, 512 KB. SVG is refused — it can carry script, and a logo is not worth an XSS surface on the recipient page.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var kind = Route<string>("Kind") ?? string.Empty;
        if (!BrandAssetKind.All.Contains(kind))
        {
            ThrowError(ErrorCodes.BrandAssetKindInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var file = Files.Count > 0 ? Files[0] : null;
        if (file is null || file.Length == 0)
        {
            ThrowError(ErrorCodes.BrandAssetMissing, StatusCodes.Status400BadRequest);
            return;
        }

        var isFont = BrandAssetKind.IsFont(kind);
        var cap = isFont ? BrandAssets.MaxFontBytes : BrandAssets.MaxLogoBytes;
        if (file.Length > cap)
        {
            ThrowError(ErrorCodes.BrandAssetTooLarge, StatusCodes.Status400BadRequest);
            return;
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        var accepted = isFont
            ? BrandAssets.LooksLikeWoff2(bytes)
            : BrandAssets.LooksLikeImage(bytes);

        if (!accepted)
        {
            ThrowError(ErrorCodes.BrandAssetTypeInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var brand = await db.WorkspaceBrands.FirstOrDefaultAsync(b => b.WorkspaceId == seat.WorkspaceId, ct);
        if (brand is null)
        {
            brand = new WorkspaceBrand
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = seat.WorkspaceId,
                CreatedAt = DateTime.UtcNow,
            };
            db.WorkspaceBrands.Add(brand);
        }

        var previousKey = KeyOf(brand, kind);
        var newKey = BrandAssets.Key(seat.WorkspaceId, kind);

        var storage = Resolve<IStorageProvider>();
        await storage.UploadObjectAsync(
            newKey,
            new MemoryStream(bytes),
            isFont ? BrandAssets.FontContentType : BrandAssets.LogoContentType,
            ct);

        SetKey(brand, kind, newKey);
        brand.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Best-effort: an orphaned old asset costs a few hundred kilobytes, whereas failing the
        // upload because cleanup failed would lose the new one the admin just chose.
        if (!string.IsNullOrEmpty(previousKey))
        {
            try
            {
                await storage.DeleteObjectAsync(previousKey, ct);
            }
            catch (Exception ex)
            {
                Resolve<ILogger<UploadBrandAssetEndpoint>>()
                    .LogWarning(ex, "Could not delete replaced brand asset {Key}", previousKey);
            }
        }

        await BrandReader.EvictAsync(Resolve<CacheService>(), seat.WorkspaceId, ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceBrandChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { asset = kind }));

        await Send.OkAsync(GetBrandEndpoint.Map(brand), ct);
    }

    internal static string? KeyOf(WorkspaceBrand brand, string kind) => kind switch
    {
        BrandAssetKind.HeadingFont => brand.HeadingFontKey,
        BrandAssetKind.BodyFont => brand.BodyFontKey,
        _ => brand.LogoKey,
    };

    internal static void SetKey(WorkspaceBrand brand, string kind, string? key)
    {
        switch (kind)
        {
            case BrandAssetKind.HeadingFont:
                brand.HeadingFontKey = key;
                break;
            case BrandAssetKind.BodyFont:
                brand.BodyFontKey = key;
                break;
            default:
                brand.LogoKey = key;
                break;
        }
    }
}

/// <summary>DELETE /api/workspace/brand/assets/{Kind} — remove an uploaded asset (issues #285, #286).</summary>
public sealed class DeleteBrandAssetEndpoint : EndpointWithoutRequest<BrandDto>
{
    public override void Configure()
    {
        Delete("/api/workspace/brand/assets/{Kind}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Remove a brand asset");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var kind = Route<string>("Kind") ?? string.Empty;
        if (!BrandAssetKind.All.Contains(kind))
        {
            ThrowError(ErrorCodes.BrandAssetKindInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var brand = await db.WorkspaceBrands.FirstOrDefaultAsync(b => b.WorkspaceId == seat.WorkspaceId, ct);
        if (brand is null)
        {
            ThrowError(ErrorCodes.BrandNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var key = UploadBrandAssetEndpoint.KeyOf(brand, kind);
        UploadBrandAssetEndpoint.SetKey(brand, kind, null);
        brand.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(key))
        {
            try
            {
                await Resolve<IStorageProvider>().DeleteObjectAsync(key, ct);
            }
            catch (Exception ex)
            {
                Resolve<ILogger<DeleteBrandAssetEndpoint>>()
                    .LogWarning(ex, "Could not delete brand asset {Key}", key);
            }
        }

        await BrandReader.EvictAsync(Resolve<CacheService>(), seat.WorkspaceId, ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceBrandChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { removed = kind }));

        await Send.OkAsync(GetBrandEndpoint.Map(brand), ct);
    }
}
