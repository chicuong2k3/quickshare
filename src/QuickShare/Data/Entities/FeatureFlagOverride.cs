namespace QuickShare.Data.Entities;

/// <summary>
/// Admin-set override for a feature flag (wins over the appsettings default).
/// Rollout percent mirrors the config shape so an admin can flip a flag to 10%,
/// monitor (issue #89), then ramp to 100%.
/// </summary>
public record FeatureFlagOverride : BaseAuditEntity
{
    /// <summary>Stable flag key (see <c>FeatureFlagKeys</c>). Unique per row.</summary>
    public string Key { get; init; } = string.Empty;

    public bool Enabled { get; set; }

    public int RolloutPercent { get; set; } = 100;
}