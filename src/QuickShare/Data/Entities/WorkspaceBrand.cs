namespace QuickShare.Data.Entities;

/// <summary>
/// A workspace's own look on the pages its clients see (issues #285, #286).
///
/// The recipient page is an agency's brand moment: a client opening a contract sees the studio
/// that sent it, not us. Everything here is therefore scoped to surfaces the *workspace's own
/// recipients* reach — the delivery page and the upload success page. It never re-skins the
/// signed-in app, because a member of two workspaces would have no stable idea where they are.
///
/// Type sizes stay fixed (DESIGN.md owns the scale); only families change. A brand that can
/// resize headings can break every layout it touches, and "your font, our rhythm" is the trade
/// that keeps the page readable.
/// </summary>
public record WorkspaceBrand : BaseAuditEntity
{
    public const int ColorMaxLength = 7;
    public const int HeaderMaxLength = 120;
    public const int FooterMaxLength = 200;
    public const int FontNameMaxLength = 60;
    public const int LicenseNoteMaxLength = 200;
    public const int AssetKeyMaxLength = 400;

    public Guid WorkspaceId { get; set; }

    /// <summary>Off by default: an unfinished brand should not leak onto a client's page.</summary>
    public bool Enabled { get; set; }

    /// <summary>Object-storage key of the logo. Served through the app, never hot-linked.</summary>
    public string? LogoKey { get; set; }

    /// <summary>Fill colour, as <c>#rrggbb</c>. Null keeps the QuickShare accent.</summary>
    public string? AccentColor { get; set; }
    /// <summary>Text colour used on top of the accent; the pair is contrast-checked on save.</summary>
    public string? AccentTextColor { get; set; }

    /// <summary>Short line above the file, e.g. "Sent by Studio Nam".</summary>
    public string? HeaderText { get; set; }
    /// <summary>Small print under the card — usually the agency's contact or legal line.</summary>
    public string? FooterText { get; set; }

    /// <summary>
    /// Drops the QuickShare wordmark from the recipient page (issue #81's brand half). The page
    /// still says who processed the file where the law requires it; this only removes marketing.
    /// </summary>
    public bool HideQuickShareBranding { get; set; }

    // ── Typography (issue #286) ──

    public string? HeadingFontKey { get; set; }
    public string? BodyFontKey { get; set; }
    /// <summary>Family name used in <c>@font-face</c> and the CSS variable. Sanitised on save.</summary>
    public string? HeadingFontName { get; set; }
    public string? BodyFontName { get; set; }
    /// <summary>The uploader's own declaration that they may embed these files. We cannot verify it.</summary>
    public string? FontLicenseNote { get; set; }

    public Workspace Workspace { get; init; } = null!;

    /// <summary>A brand only applies once switched on and given something to show.</summary>
    public bool IsApplicable =>
        Enabled && (LogoKey is not null
            || AccentColor is not null
            || HeaderText is not null
            || FooterText is not null
            || HeadingFontKey is not null
            || BodyFontKey is not null);
}

/// <summary>Which uploaded asset a request refers to (issues #285, #286).</summary>
public static class BrandAssetKind
{
    public const string Logo = "logo";
    public const string HeadingFont = "heading-font";
    public const string BodyFont = "body-font";

    public static readonly string[] All = [Logo, HeadingFont, BodyFont];

    public static bool IsFont(string kind) => kind is HeadingFont or BodyFont;
}
