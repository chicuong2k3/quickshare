using RizzyUI;

namespace QuickShare.Web.Infrastructure;

/// <summary>
/// Light-only theme — warm stone background, editorial navy primary.
/// Inspired by WeTransfer's clean, generous aesthetic.
/// Dark variant mirrors Light intentionally.
/// </summary>
public sealed class LinearTheme : ArcticTheme
{
    public LinearTheme()
    {
        Name = "Linear";
        ThemeCode = "linear";
        Radius = "1rem";

        Light = new RzThemeVariant
        {
            Background = new Color("#FAF8F4"),
            Foreground = new Color("#1E2130"),
            Card = new Color("#FFFFFF"),
            CardForeground = new Color("#1E2130"),
            Popover = new Color("#FFFFFF"),
            PopoverForeground = new Color("#1E2130"),
            Primary = new Color("#3B3F6E"),
            PrimaryForeground = new Color("#FFFFFF"),
            Secondary = new Color("#F5F0EA"),
            SecondaryForeground = new Color("#4A5060"),
            Muted = new Color("#F6F4ED"),
            MutedForeground = new Color("#6B7280"),
            Accent = new Color("#D4885A"),
            AccentForeground = new Color("#3D2510"),
            Destructive = new Color("#C54545"),
            DestructiveForeground = new Color("#FFFFFF"),
            Border = new Color("#EAE6DF"),
            Input = new Color("#EAE6DF"),
            Ring = new Color("#3B3F6E"),
            Info = new Color("#3B3F6E"),
            InfoForeground = new Color("#FFFFFF"),
            Warning = new Color("#C48A42"),
            WarningForeground = new Color("#1E2130"),
            Success = new Color("#5D9B6A"),
            SuccessForeground = new Color("#FFFFFF"),
            Chart1 = new Color("#3B3F6E"),
            Chart2 = new Color("#6B8DB5"),
            Chart3 = new Color("#5D9B6A"),
            Chart4 = new Color("#D4885A"),
            Chart5 = new Color("#C54545"),
            Sidebar = new Color("#f9f7f3"),
            SidebarForeground = new Color("#1E2130"),
            SidebarPrimary = new Color("#3B3F6E"),
            SidebarPrimaryForeground = new Color("#FFFFFF"),
            SidebarAccent = new Color("#D4885A"),
            SidebarAccentForeground = new Color("#3D2510"),
            SidebarBorder = new Color("#EAE6DF"),
            SidebarRing = new Color("#3B3F6E"),
            Radius = "1rem",
            FontSans = "Sora, system-ui, -apple-system, ui-sans-serif, sans-serif",
            FontSerif = "Source Serif 4, ui-serif, Georgia, serif",
            FontMono = "JetBrains Mono, SF Mono, ui-monospace, monospace",
            Shadow2Xs = "0 0 0 1px rgba(0,0,0,.03)",
            ShadowXs = "0 1px 3px rgba(0,0,0,.04)",
            ShadowSm = "0 2px 6px rgba(0,0,0,.05)",
            ShadowMd = "0 4px 12px rgba(0,0,0,.07)",
            ShadowLg = "0 8px 24px rgba(0,0,0,.08)",
            ShadowXl = "0 12px 32px rgba(0,0,0,.10)",
            Shadow2Xl = "0 16px 48px rgba(0,0,0,.12)",
        };

        Dark = new RzThemeVariant
        {
            Background = new Color("#FAF8F4"),
            Foreground = new Color("#1E2130"),
            Card = new Color("#FFFFFF"),
            CardForeground = new Color("#1E2130"),
            Popover = new Color("#FFFFFF"),
            PopoverForeground = new Color("#1E2130"),
            Primary = new Color("#3B3F6E"),
            PrimaryForeground = new Color("#FFFFFF"),
            Secondary = new Color("#F5F0EA"),
            SecondaryForeground = new Color("#4A5060"),
            Muted = new Color("#F6F4ED"),
            MutedForeground = new Color("#6B7280"),
            Accent = new Color("#D4885A"),
            AccentForeground = new Color("#3D2510"),
            Destructive = new Color("#C54545"),
            DestructiveForeground = new Color("#FFFFFF"),
            Border = new Color("#EAE6DF"),
            Input = new Color("#EAE6DF"),
            Ring = new Color("#3B3F6E"),
            Info = new Color("#3B3F6E"),
            InfoForeground = new Color("#FFFFFF"),
            Warning = new Color("#C48A42"),
            WarningForeground = new Color("#1E2130"),
            Success = new Color("#5D9B6A"),
            SuccessForeground = new Color("#FFFFFF"),
            Chart1 = new Color("#3B3F6E"),
            Chart2 = new Color("#6B8DB5"),
            Chart3 = new Color("#5D9B6A"),
            Chart4 = new Color("#D4885A"),
            Chart5 = new Color("#C54545"),
            Sidebar = new Color("#f9f7f3"),
            SidebarForeground = new Color("#1E2130"),
            SidebarPrimary = new Color("#3B3F6E"),
            SidebarPrimaryForeground = new Color("#FFFFFF"),
            SidebarAccent = new Color("#D4885A"),
            SidebarAccentForeground = new Color("#3D2510"),
            SidebarBorder = new Color("#EAE6DF"),
            SidebarRing = new Color("#3B3F6E"),
            Radius = "1rem",
            FontSans = "Sora, system-ui, -apple-system, ui-sans-serif, sans-serif",
            FontSerif = "Source Serif 4, ui-serif, Georgia, serif",
            FontMono = "JetBrains Mono, SF Mono, ui-monospace, monospace",
            Shadow2Xs = "0 0 0 1px rgba(0,0,0,.03)",
            ShadowXs = "0 1px 3px rgba(0,0,0,.04)",
            ShadowSm = "0 2px 6px rgba(0,0,0,.05)",
            ShadowMd = "0 4px 12px rgba(0,0,0,.07)",
            ShadowLg = "0 8px 24px rgba(0,0,0,.08)",
            ShadowXl = "0 12px 32px rgba(0,0,0,.10)",
            Shadow2Xl = "0 16px 48px rgba(0,0,0,.12)",
        };
    }
}
