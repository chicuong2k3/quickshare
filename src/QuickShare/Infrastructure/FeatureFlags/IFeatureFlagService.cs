namespace QuickShare.Infrastructure.FeatureFlags;

/// <summary>
/// Reads feature-flag state and manages admin-set overrides. Rollout is
/// deterministic per (user, flag) via a SHA-256 hash bucket, so a 10% flag is
/// stable for a given user across requests — no flicker between page loads.
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// True when the flag is enabled for the given caller. An anonymous caller
    /// (<paramref name="userId"/> null) only sees a flag when it is fully rolled
    /// out (100%); partial rollouts require an identity to bucket against.
    /// </summary>
    Task<bool> IsEnabledAsync(string key, Guid? userId = null, CancellationToken ct = default);

    /// <summary>All configured flags with their default, override, and effective state (admin UI).</summary>
    Task<IReadOnlyList<FeatureFlagStatus>> ListAsync(CancellationToken ct = default);

    /// <summary>Upsert an admin override (wins over the appsettings default) and invalidate the cache.</summary>
    Task SetOverrideAsync(string key, bool enabled, int rolloutPercent, CancellationToken ct = default);
}