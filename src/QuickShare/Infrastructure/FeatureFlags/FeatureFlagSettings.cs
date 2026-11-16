namespace QuickShare.Infrastructure.FeatureFlags;

/// <summary>
/// Configuration defaults for the feature-flag system. <see cref="Enabled"/> is the
/// master kill switch (mirrors the FusionCache convention): when false, every flag
/// resolves to OFF regardless of per-flag defaults or DB overrides.
/// </summary>
public sealed class FeatureFlagSettings
{
    public const string Section = "FeatureFlags";

    /// <summary>Master kill switch. When false, ALL flags return OFF.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Per-flag defaults. Key = stable flag key (see <see cref="FeatureFlagKeys"/>).</summary>
    public Dictionary<string, FeatureFlagConfig> Flags { get; set; } = [];
}

public sealed class FeatureFlagConfig
{
    /// <summary>Default enabled state (before any DB override / rollout check).</summary>
    public bool Enabled { get; set; }

    /// <summary>Rollout percentage 0-100. Partial rollouts only affect identified users (hash bucket).</summary>
    public int RolloutPercent { get; set; } = 100;

    /// <summary>Human-readable description shown in the admin UI.</summary>
    public string? Description { get; set; }
}