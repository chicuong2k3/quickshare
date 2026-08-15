using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickShare.Data;
using QuickShare.Infrastructure.Caching;
using System.Security.Cryptography;
using System.Text;

namespace QuickShare.Infrastructure.FeatureFlags;

/// <summary>
/// Feature-flag evaluation with three layers, in order of precedence:
/// <list type="number">
/// <item>Master kill switch (<see cref="FeatureFlagSettings.Enabled"/>) — false turns EVERYTHING off.</item>
/// <item>DB override (<see cref="FeatureFlagOverride"/>) — admin-set via the admin API.</item>
/// <item>Config default (<see cref="FeatureFlagConfig"/>) — the appsettings baseline.</item>
/// </list>
/// Rollout buckets are deterministic: <c>SHA256("{userId:N}:{key}") % 100 &lt; percent</c>.
/// The override table is cached via <see cref="CacheService"/> (short TTL) and evicted on write.
/// </summary>
public sealed class FeatureFlagService(
    CacheService cache,
    IOptions<FeatureFlagSettings> settings,
    AppDbContext db) : IFeatureFlagService
{
    private const string OverridesCacheKey = "feature-flags:overrides";
    private static readonly TimeSpan OverridesCacheTtl = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public async Task<bool> IsEnabledAsync(string key, Guid? userId = null, CancellationToken ct = default)
    {
        // Kill switch — off beats everything, including overrides.
        var config = settings.Value;
        if (!config.Enabled) return false;

        if (!config.Flags.TryGetValue(key, out var flagConfig)) return false;

        var overrides = await GetOverridesAsync(ct);
        var hasOverride = overrides.TryGetValue(key, out var flagOverride);

        if (!(hasOverride ? flagOverride!.Enabled : flagConfig.Enabled)) return false;

        var rolloutPercent = hasOverride ? flagOverride!.RolloutPercent : flagConfig.RolloutPercent;
        if (rolloutPercent >= 100) return true;
        if (userId is null) return false;

        return HashBucket(userId.Value, key) < rolloutPercent;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FeatureFlagStatus>> ListAsync(CancellationToken ct = default)
    {
        var config = settings.Value;
        var overrides = await GetOverridesAsync(ct);

        var result = new List<FeatureFlagStatus>(config.Flags.Count);
        foreach (var (key, flagConfig) in config.Flags)
        {
            var hasOverride = overrides.TryGetValue(key, out var flagOverride);
            var overrideEnabled = hasOverride ? flagOverride!.Enabled : (bool?)null;
            var overrideRollout = hasOverride ? flagOverride!.RolloutPercent : (int?)null;

            result.Add(new FeatureFlagStatus(
                key,
                flagConfig.Description,
                flagConfig.Enabled,
                flagConfig.RolloutPercent,
                overrideEnabled,
                overrideRollout,
                EffectiveEnabled: overrideEnabled ?? flagConfig.Enabled,
                EffectiveRolloutPercent: overrideRollout ?? flagConfig.RolloutPercent));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SetOverrideAsync(string key, bool enabled, int rolloutPercent, CancellationToken ct = default)
    {
        var existing = await db.FeatureFlagOverrides.FirstOrDefaultAsync(o => o.Key == key, ct);
        if (existing is null)
        {
            db.FeatureFlagOverrides.Add(new FeatureFlagOverride
            {
                Id = Guid.CreateVersion7(),
                Key = key,
                Enabled = enabled,
                RolloutPercent = rolloutPercent
            });
        }
        else
        {
            existing.Enabled = enabled;
            existing.RolloutPercent = rolloutPercent;
        }

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync(OverridesCacheKey, ct);
    }

    private async Task<Dictionary<string, FeatureFlagOverride>> GetOverridesAsync(CancellationToken ct)
    {
        var overrides = await cache.GetOrSetAsync(
            OverridesCacheKey,
            async ct2 => await db.FeatureFlagOverrides.AsNoTracking().ToDictionaryAsync(o => o.Key, ct2),
            OverridesCacheTtl,
            ct);
        return overrides ?? new Dictionary<string, FeatureFlagOverride>();
    }

    /// <summary>
    /// Deterministic rollout bucket for an identified user: first 8 bytes of
    /// SHA-256("{userId:N}:{key}") → ulong → mod 100. Stable across requests.
    /// </summary>
    public static int HashBucket(Guid userId, string flagKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{flagKey}"));
        var value = BitConverter.ToUInt64(bytes, 0);
        return (int)(value % 100);
    }
}