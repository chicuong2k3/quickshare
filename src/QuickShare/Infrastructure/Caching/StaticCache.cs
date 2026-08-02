namespace QuickShare.Infrastructure.Caching;

/// <summary>
/// Static holder for CacheService — allows static classes like PlanTierLimits
/// to access the cache without constructor DI.
/// </summary>
public static class StaticCache
{
    private static CacheService? _instance;

    public static CacheService Instance =>
        _instance ?? throw new InvalidOperationException(
            "StaticCache not initialized. Call StaticCache.Initialize() after DI container is built.");

    public static void Initialize(CacheService cacheService)
    {
        _instance = cacheService;
    }
}
