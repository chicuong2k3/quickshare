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
}
