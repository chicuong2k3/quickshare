using RizzyUI;

namespace TeleCloud.Web.Infrastructure;

/// <summary>
/// Light-only theme for QuickShareCloud (forced light, no dark mode).
/// Blue primary (#2563EB), cool neutrals, hairline borders, 0.5rem radius.
/// Subclasses ArcticTheme so all component TvDescriptors inherit.
/// Dark variant is intentionally set to identical Light values so the
/// theme always renders light regardless of any `dark` class on &lt;html&gt;.
/// </summary>
public sealed class LinearTheme : ArcticTheme
{
    public LinearTheme()
    {
        Name = "Linear";
        ThemeCode = "linear";
        Radius = "0.5rem";

        Light = new RzThemeVariant
        {
            Background = new Color("#F8FAFC"),
            Foreground = new Color("#0F172A"),
            Card = new Color("#FFFFFF"),
            CardForeground = new Color("#0F172A"),
            Popover = new Color("#FFFFFF"),
            PopoverForeground = new Color("#0F172A"),
            Primary = new Color("#2563EB"),
            PrimaryForeground = new Color("#FFFFFF"),
            Secondary = new Color("#F1F5F9"),
            SecondaryForeground = new Color("#334155"),
            Muted = new Color("#F1F5F9"),
            MutedForeground = new Color("#475569"),
            Accent = new Color("#EFF6FF"),
            AccentForeground = new Color("#1E3A5F"),
            Destructive = new Color("#DC2626"),
            DestructiveForeground = new Color("#FFFFFF"),
            Border = new Color("#E2E8F0"),
            Input = new Color("#E2E8F0"),
            Ring = new Color("#2563EB"),
            Info = new Color("#2563EB"),
            InfoForeground = new Color("#FFFFFF"),
            Warning = new Color("#F59E0B"),
            WarningForeground = new Color("#0F172A"),
            Success = new Color("#10B981"),
            SuccessForeground = new Color("#FFFFFF"),
            Chart1 = new Color("#2563EB"),
            Chart2 = new Color("#60A5FA"),
            Chart3 = new Color("#10B981"),
            Chart4 = new Color("#F59E0B"),
            Chart5 = new Color("#DC2626"),
            Sidebar = new Color("#FFFFFF"),
            SidebarForeground = new Color("#0F172A"),
            SidebarPrimary = new Color("#2563EB"),
            SidebarPrimaryForeground = new Color("#FFFFFF"),
            SidebarAccent = new Color("#F1F5F9"),
            SidebarAccentForeground = new Color("#0F172A"),
            SidebarBorder = new Color("#E2E8F0"),
            SidebarRing = new Color("#2563EB"),
            Radius = "0.5rem",
            FontSans = "Sora, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif",
            FontSerif = "ui-serif, Georgia, Cambria, 'Times New Roman', Times, serif",
            FontMono = "ui-monospace, 'Cascadia Code', 'SF Mono', 'Fira Code', Consolas, monospace",
            Shadow2Xs = "0 1px 2px rgba(0,0,0,.05)",
            ShadowXs = "0 1px 2px rgba(0,0,0,.05)",
            ShadowSm = "0 1px 3px rgba(0,0,0,.08)",
            ShadowMd = "0 4px 6px rgba(0,0,0,.08)",
            ShadowLg = "0 10px 15px rgba(0,0,0,.08)",
            ShadowXl = "0 20px 25px rgba(0,0,0,.08)",
            Shadow2Xl = "0 25px 50px rgba(0,0,0,.08)",
        };

        // Dark variant mirrors Light intentionally — this theme is light-only.
        Dark = new RzThemeVariant
        {
            Background = new Color("#F8FAFC"),
            Foreground = new Color("#0F172A"),
            Card = new Color("#FFFFFF"),
            CardForeground = new Color("#0F172A"),
            Popover = new Color("#FFFFFF"),
            PopoverForeground = new Color("#0F172A"),
            Primary = new Color("#2563EB"),
            PrimaryForeground = new Color("#FFFFFF"),
            Secondary = new Color("#F1F5F9"),
            SecondaryForeground = new Color("#334155"),
            Muted = new Color("#F1F5F9"),
            MutedForeground = new Color("#475569"),
            Accent = new Color("#EFF6FF"),
            AccentForeground = new Color("#1E3A5F"),
            Destructive = new Color("#DC2626"),
            DestructiveForeground = new Color("#FFFFFF"),
            Border = new Color("#E2E8F0"),
            Input = new Color("#E2E8F0"),
            Ring = new Color("#2563EB"),
            Info = new Color("#2563EB"),
            InfoForeground = new Color("#FFFFFF"),
            Warning = new Color("#F59E0B"),
            WarningForeground = new Color("#0F172A"),
            Success = new Color("#10B981"),
            SuccessForeground = new Color("#FFFFFF"),
            Chart1 = new Color("#2563EB"),
            Chart2 = new Color("#60A5FA"),
            Chart3 = new Color("#10B981"),
            Chart4 = new Color("#F59E0B"),
            Chart5 = new Color("#DC2626"),
            Sidebar = new Color("#FFFFFF"),
            SidebarForeground = new Color("#0F172A"),
            SidebarPrimary = new Color("#2563EB"),
            SidebarPrimaryForeground = new Color("#FFFFFF"),
            SidebarAccent = new Color("#F1F5F9"),
            SidebarAccentForeground = new Color("#0F172A"),
            SidebarBorder = new Color("#E2E8F0"),
            SidebarRing = new Color("#2563EB"),
            Radius = "0.5rem",
            FontSans = "Sora, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif",
            FontSerif = "ui-serif, Georgia, Cambria, 'Times New Roman', Times, serif",
            FontMono = "ui-monospace, 'Cascadia Code', 'SF Mono', 'Fira Code', Consolas, monospace",
            Shadow2Xs = "0 1px 2px rgba(0,0,0,.05)",
            ShadowXs = "0 1px 2px rgba(0,0,0,.05)",
            ShadowSm = "0 1px 3px rgba(0,0,0,.08)",
            ShadowMd = "0 4px 6px rgba(0,0,0,.08)",
            ShadowLg = "0 10px 15px rgba(0,0,0,.08)",
            ShadowXl = "0 20px 25px rgba(0,0,0,.08)",
            Shadow2Xl = "0 25px 50px rgba(0,0,0,.08)",
        };
    }
}
