namespace QuickShare.Infrastructure.FeatureFlags;

/// <summary>Read-model for the admin flags screen: config default, DB override, and the resolved state.</summary>
public sealed record FeatureFlagStatus(
    string Key,
    string? Description,
    bool DefaultEnabled,
    int DefaultRolloutPercent,
    bool? OverrideEnabled,
    int? OverrideRolloutPercent,
    bool EffectiveEnabled,
    int EffectiveRolloutPercent);