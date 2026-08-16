namespace QuickShare.Infrastructure;

public sealed class QuickShareOptions
{
    public const string Section = "QuickShare";

    public int CleanupIntervalMinutes { get; init; } = 60;
    public int MaxP2PConnections { get; init; } = 1000;

    // ── Orphan R2 sweep (issue #107) ──
    /// <summary>Dry-run by default: report orphans without deleting. Flip to true to reclaim.</summary>
    public bool OrphanSweepDelete { get; init; }
    public int OrphanObjectMinAgeDays { get; init; } = 7;
    public int OrphanMultipartMinAgeHours { get; init; } = 24;
    public int OrphanSweepIntervalHours { get; init; } = 24;
    /// <summary>Circuit breaker: more object candidates than this → the cycle downgrades to dry-run.</summary>
    public int OrphanSweepMaxDeletesPerCycle { get; init; } = 500;

    // ── Site-wide announcement banner (issue #282) ──
    /// <summary>
    /// Config-driven broadcast: set Id + texts to show a dismissible banner on every page.
    /// Dismissal is stored client-side keyed by Id — change the Id to re-show a new message.
    /// ponytail: per-workspace compose UI belongs to team workspaces (#106) when they exist.
    /// </summary>
    public AnnouncementOptions Announcement { get; init; } = new();
}

public sealed class AnnouncementOptions
{
    /// <summary>Stable id for this announcement; empty = no banner.</summary>
    public string Id { get; init; } = string.Empty;
    public string TextEn { get; init; } = string.Empty;
    public string TextVi { get; init; } = string.Empty;
    /// <summary>Optional link ("read more") shown after the text.</summary>
    public string? Url { get; init; }
    /// <summary>"info" (default) or "warning" — picks the banner tone.</summary>
    public string Level { get; init; } = "info";
}
