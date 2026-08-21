using System.Text;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Workspaces.Branding;

/// <summary>What a recipient page needs to render a workspace's brand (issues #285, #286).</summary>
public sealed record BrandView(
    Guid WorkspaceId,
    bool HasLogo,
    string? AccentColor,
    string? AccentTextColor,
    string? HeaderText,
    string? FooterText,
    bool HideQuickShareBranding,
    string? HeadingFontName,
    string? BodyFontName,
    bool HasHeadingFont,
    bool HasBodyFont);

/// <summary>
/// Resolves the brand a delivery should be shown under, and renders it to CSS (issues #285,
/// #286).
///
/// Cached per workspace, because the recipient page is the hottest page in the product and a
/// brand changes about as often as a company rebrands. Eviction is explicit from every write
/// path — a stale logo is a small wrong, but a stale *removed* logo is an agency's old identity
/// still showing on a client's screen after they asked for it to go.
/// </summary>
public sealed class BrandReader(AppDbContext db, CacheService cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private static string CacheKey(Guid workspaceId) => $"brand:{workspaceId}";

    public static ValueTask EvictAsync(CacheService cache, Guid workspaceId, CancellationToken ct) =>
        cache.RemoveAsync(CacheKey(workspaceId), ct);

    /// <summary>Null when the delivery is personal, or the workspace has no applicable brand.</summary>
    public async Task<BrandView?> ForWorkspaceAsync(Guid? workspaceId, CancellationToken ct)
    {
        if (workspaceId is not { } id)
            return null;

        return await cache.GetOrSetAsync(
            CacheKey(id),
            async token =>
            {
                var brand = await db.WorkspaceBrands
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.WorkspaceId == id, token);

                if (brand is null || !brand.IsApplicable)
                    return null;

                return new BrandView(
                    id,
                    brand.LogoKey is not null,
                    brand.AccentColor,
                    brand.AccentTextColor,
                    brand.HeaderText,
                    brand.FooterText,
                    brand.HideQuickShareBranding,
                    brand.HeadingFontName,
                    brand.BodyFontName,
                    brand.HeadingFontKey is not null,
                    brand.BodyFontKey is not null);
            },
            duration: CacheDuration,
            token: ct);
    }

    /// <summary>
    /// The brand as a stylesheet: token overrides plus <c>@font-face</c> rules pointing at the
    /// app's own asset route.
    ///
    /// Only *values* are overridden, never token names or type sizes — DESIGN.md owns the scale,
    /// and a brand that can resize headings can break every layout it touches. Everything
    /// interpolated here was validated on save (hex colours, restricted font names), so nothing
    /// user-supplied can close a declaration and open a new one.
    /// </summary>
    public static string ToCss(BrandView brand)
    {
        var css = new StringBuilder();

        if (brand.HasHeadingFont && brand.HeadingFontName is { } headingName)
            AppendFontFace(css, headingName, AssetUrl(brand.WorkspaceId, BrandAssetKind.HeadingFont));

        if (brand.HasBodyFont && brand.BodyFontName is { } bodyName)
            AppendFontFace(css, bodyName, AssetUrl(brand.WorkspaceId, BrandAssetKind.BodyFont));

        css.Append(":root{");

        if (brand.AccentColor is { } accent)
        {
            css.Append("--db-color-accent:").Append(accent).Append(';');
            css.Append("--db-foil:").Append(accent).Append(';');
        }

        if (brand.AccentTextColor is { } accentText)
            css.Append("--db-color-accent-text:").Append(accentText).Append(';');

        // Families only. The fallback stack stays so the page is readable while the font loads
        // and still readable if it never does.
        if (brand.HasHeadingFont && brand.HeadingFontName is { } heading)
            css.Append("--db-font-display:\"").Append(heading).Append("\",var(--db-font-ui),system-ui,sans-serif;");

        if (brand.HasBodyFont && brand.BodyFontName is { } body)
            css.Append("--db-font-ui:\"").Append(body).Append("\",system-ui,sans-serif;");

        css.Append('}');

        return css.ToString();
    }

    private static void AppendFontFace(StringBuilder css, string family, string url)
    {
        css.Append("@font-face{font-family:\"").Append(family).Append("\";");
        css.Append("src:url(\"").Append(url).Append("\") format(\"woff2\");");
        // swap: show the fallback immediately rather than blocking the download button behind a
        // font request that may never finish.
        css.Append("font-display:swap;}");
    }

    public static string AssetUrl(Guid workspaceId, string kind) =>
        $"/brand/{workspaceId:N}/{kind}";
}
