using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace QuickShare.Infrastructure.Caching;

/// <summary>
/// Thin wrapper around FusionCache with a kill-switch toggle.
/// When <see cref="CacheSettings.Enabled"/> is false, all operations are no-ops
/// (GetOrSet always calls the factory, Set/Remove do nothing).
/// </summary>
public sealed class CacheService
{
    private readonly IFusionCache _cache;
    private readonly CacheSettings _settings;

    public CacheService(IFusionCache cache, IOptions<CacheSettings> settings)
    {
        _cache = cache;
        _settings = settings.Value;
    }

    public bool Enabled => _settings.Enabled;

    /// <summary>Get from cache or call factory. When disabled, always calls factory.</summary>
    public T? GetOrSet<T>(
        string key,
        Func<FusionCacheFactoryExecutionContext<T>, CancellationToken, T> factory,
        FusionCacheEntryOptions? options = null)
    {
        if (!_settings.Enabled) return factory(default!, default);
        return _cache.GetOrSet<T>(key, factory, options: options);
    }

    /// <summary>Async version — Get from cache or call factory.</summary>
    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<FusionCacheFactoryExecutionContext<T>, CancellationToken, Task<T>> factory,
        FusionCacheEntryOptions? options = null,
        CancellationToken token = default)
    {
        if (!_settings.Enabled) return await factory(default!, token);
        return await _cache.GetOrSetAsync<T>(key, factory, options: options, token: token);
    }

    // ── Simple overloads (no FusionCacheExecutionContext ceremony) ──

    /// <summary>Get or set with a plain factory. Uses default entry options with optional duration override.</summary>
    public T? GetOrSet<T>(string key, Func<T> factory, TimeSpan? duration = null)
    {
        if (!_settings.Enabled) return factory();
        return _cache.GetOrSet<T>(key, (_, _) => factory(), DurationOptions(duration));
    }

    /// <summary>Get or set with a CancellationToken-aware factory.</summary>
    public T? GetOrSet<T>(string key, Func<CancellationToken, T> factory, TimeSpan? duration = null)
    {
        if (!_settings.Enabled) return factory(default);
        return _cache.GetOrSet<T>(key, (_, ct) => factory(ct), DurationOptions(duration));
    }

    /// <summary>Async get-or-set with a CancellationToken-aware factory.</summary>
    public async Task<T> GetOrSetAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? duration = null, CancellationToken token = default)
    {
        if (!_settings.Enabled) return await factory(token);
        return await _cache.GetOrSetAsync<T>(key, (_, ct) => factory(ct), DurationOptions(duration), token);
    }

    /// <summary>Invalidate a cache entry (semantic alias for Remove).</summary>
    public async ValueTask InvalidateAsync(string key, CancellationToken token = default) => await RemoveAsync(key, token);

    private FusionCacheEntryOptions? DurationOptions(TimeSpan? duration)
    {
        if (duration is null) return null;
        return new FusionCacheEntryOptions
        {
            Duration = duration.Value,
            FailSafeMaxDuration = TimeSpan.FromHours(_settings.FailSafeMaxDurationHours),
            FailSafeThrottleDuration = TimeSpan.FromSeconds(_settings.FailSafeThrottleDurationSeconds),
        };
    }

    /// <summary>Set a value in cache. No-op when disabled.</summary>
    public void Set<T>(string key, T value, FusionCacheEntryOptions? options = null)
    {
        if (!_settings.Enabled) return;
        _cache.Set(key, value, options);
    }

    /// <summary>Set a value in cache (async). No-op when disabled.</summary>
    public async ValueTask SetAsync<T>(string key, T value, FusionCacheEntryOptions? options = null, CancellationToken token = default)
    {
        if (!_settings.Enabled) return;
        await _cache.SetAsync<T>(key, value, options, token);
    }

    /// <summary>Remove a cache entry. No-op when disabled.</summary>
    public void Remove(string key)
    {
        if (!_settings.Enabled) return;
        _cache.Remove(key);
    }

    /// <summary>Remove a cache entry (async). No-op when disabled.</summary>
    public async ValueTask RemoveAsync(string key, CancellationToken token = default)
    {
        if (!_settings.Enabled) return;
        await _cache.RemoveAsync(key, token: token);
    }
}
