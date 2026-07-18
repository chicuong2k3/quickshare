using RizzyUI;

namespace TeleCloud.Web.Infrastructure;

/// <summary>
/// Linear.app-inspired cool blue-gray theme for QuickShareCloud.
/// Blue primary (#3B82F6), cool neutrals, hairline borders, 0.5rem radius.
/// Subclasses ArcticTheme so all component TvDescriptors inherit.
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
            Primary = new Color("#3B82F6"),
            PrimaryForeground = new Color("#FFFFFF"),
            Secondary = new Color("#F1F5F9"),
            SecondaryForeground = new Color("#334155"),
            Muted = new Color("#F1F5F9"),
            MutedForeground = new Color("#64748B"),
            Accent = new Color("#EFF6FF"),
            AccentForeground = new Color("#1E3A5F"),
            Destructive = new Color("#EF4444"),
            DestructiveForeground = new Color("#FFFFFF"),
            Border = new Color("#E2E8F0"),
            Input = new Color("#E2E8F0"),
            Ring = new Color("#3B82F6"),
            Info = new Color("#3B82F6"),
            InfoForeground = new Color("#FFFFFF"),
            Warning = new Color("#F59E0B"),
            WarningForeground = new Color("#0F172A"),
            Success = new Color("#10B981"),
            SuccessForeground = new Color("#FFFFFF"),
            Chart1 = new Color("#3B82F6"),
            Chart2 = new Color("#60A5FA"),
            Chart3 = new Color("#10B981"),
            Chart4 = new Color("#F59E0B"),
            Chart5 = new Color("#EF4444"),
            Sidebar = new Color("#FFFFFF"),
            SidebarForeground = new Color("#0F172A"),
            SidebarPrimary = new Color("#3B82F6"),
            SidebarPrimaryForeground = new Color("#FFFFFF"),
            SidebarAccent = new Color("#F1F5F9"),
            SidebarAccentForeground = new Color("#0F172A"),
            SidebarBorder = new Color("#E2E8F0"),
            SidebarRing = new Color("#3B82F6"),
            Radius = "0.5rem",
            FontSans = "Inter, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif",
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

        Dark = new RzThemeVariant
        {
            Background = new Color("#0B1121"),
            Foreground = new Color("#E2E8F0"),
            Card = new Color("#0F172A"),
            CardForeground = new Color("#E2E8F0"),
            Popover = new Color("#131C31"),
            PopoverForeground = new Color("#E2E8F0"),
            Primary = new Color("#3B82F6"),
            PrimaryForeground = new Color("#FFFFFF"),
            Secondary = new Color("#1E293B"),
            SecondaryForeground = new Color("#94A3B8"),
            Muted = new Color("#1E293B"),
            MutedForeground = new Color("#64748B"),
            Accent = new Color("#1E293B"),
            AccentForeground = new Color("#E2E8F0"),
            Destructive = new Color("#EF4444"),
            DestructiveForeground = new Color("#FFFFFF"),
            Border = new Color("#1E293B"),
            Input = new Color("#1E293B"),
            Ring = new Color("#3B82F6"),
            Info = new Color("#3B82F6"),
            InfoForeground = new Color("#FFFFFF"),
            Warning = new Color("#F59E0B"),
            WarningForeground = new Color("#0F172A"),
            Success = new Color("#10B981"),
            SuccessForeground = new Color("#FFFFFF"),
            Chart1 = new Color("#3B82F6"),
            Chart2 = new Color("#60A5FA"),
            Chart3 = new Color("#10B981"),
            Chart4 = new Color("#F59E0B"),
            Chart5 = new Color("#EF4444"),
            Sidebar = new Color("#0F172A"),
            SidebarForeground = new Color("#E2E8F0"),
            SidebarPrimary = new Color("#3B82F6"),
            SidebarPrimaryForeground = new Color("#FFFFFF"),
            SidebarAccent = new Color("#1E293B"),
            SidebarAccentForeground = new Color("#E2E8F0"),
            SidebarBorder = new Color("#1E293B"),
            SidebarRing = new Color("#3B82F6"),
            Radius = "0.5rem",
            FontSans = "Inter, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif",
            FontSerif = "ui-serif, Georgia, Cambria, 'Times New Roman', Times, serif",
            FontMono = "ui-monospace, 'Cascadia Code', 'SF Mono', 'Fira Code', Consolas, monospace",
            Shadow2Xs = "0 1px 2px rgba(0,0,0,.5)",
            ShadowXs = "0 1px 2px rgba(0,0,0,.5)",
            ShadowSm = "0 1px 3px rgba(0,0,0,.5)",
            ShadowMd = "0 4px 6px rgba(0,0,0,.5)",
            ShadowLg = "0 10px 15px rgba(0,0,0,.5)",
            ShadowXl = "0 20px 25px rgba(0,0,0,.5)",
            Shadow2Xl = "0 25px 50px rgba(0,0,0,.5)",
        };
    }
}
