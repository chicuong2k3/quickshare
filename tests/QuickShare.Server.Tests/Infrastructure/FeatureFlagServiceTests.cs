using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.FeatureFlags;
using ZiggyCreatures.Caching.Fusion;

namespace QuickShare.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="FeatureFlagService"/>: config defaults, DB overrides,
/// kill switch, and the deterministic rollout hash bucket.
/// </summary>
public sealed class FeatureFlagServiceTests
{
    private const string OnFlag = "on-flag";
    private const string OffFlag = "off-flag";
    private const string PartialFlag = "partial-flag";
    private const string ZeroFlag = "zero-flag";

    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // ── Config defaults ──

    [Fact]
    public async Task IsEnabled_ConfigDefaultOnAt100_TrueForAnyUser()
    {
        var (service, _) = CreateService();

        Assert.True(await service.IsEnabledAsync(OnFlag, UserA));
        Assert.True(await service.IsEnabledAsync(OnFlag, UserB));
        Assert.True(await service.IsEnabledAsync(OnFlag));
    }

    [Fact]
    public async Task IsEnabled_ConfigDefaultOff_FalseForAnyUser()
    {
        var (service, _) = CreateService();

        Assert.False(await service.IsEnabledAsync(OffFlag, UserA));
        Assert.False(await service.IsEnabledAsync(OffFlag));
    }

    [Fact]
    public async Task IsEnabled_UnknownKey_False()
    {
        var (service, _) = CreateService();

        Assert.False(await service.IsEnabledAsync("no-such-flag", UserA));
    }

    // ── Kill switch ──

    [Fact]
    public async Task IsEnabled_KillSwitchOff_EverythingOff_EvenWithOverride()
    {
        var (service, db) = CreateService(killSwitch: false);
        db.FeatureFlagOverrides.Add(new FeatureFlagOverride
        {
            Id = Guid.CreateVersion7(),
            Key = OnFlag,
            Enabled = true,
            RolloutPercent = 100
        });
        await db.SaveChangesAsync();

        Assert.False(await service.IsEnabledAsync(OnFlag, UserA));
        Assert.False(await service.IsEnabledAsync(OffFlag, UserA));
    }

    // ── Overrides ──

    [Fact]
    public async Task IsEnabled_OverrideOn_WinsOverConfigOff()
    {
        var (service, _) = CreateService();
        await service.SetOverrideAsync(OffFlag, true, 100);

        Assert.True(await service.IsEnabledAsync(OffFlag, UserA));
    }

    [Fact]
    public async Task IsEnabled_OverrideOff_WinsOverConfigOn()
    {
        var (service, _) = CreateService();
        await service.SetOverrideAsync(OnFlag, false, 100);

        Assert.False(await service.IsEnabledAsync(OnFlag, UserA));
    }

    [Fact]
    public async Task SetOverride_ExistingRow_Updates_NoDuplicate()
    {
        var (service, db) = CreateService();
        await service.SetOverrideAsync(OnFlag, false, 0);
        await service.SetOverrideAsync(OnFlag, true, 100);

        var rows = await db.FeatureFlagOverrides.Where(o => o.Key == OnFlag).ToListAsync();
        Assert.Single(rows);
        Assert.True(rows[0].Enabled);
        Assert.Equal(100, rows[0].RolloutPercent);
        Assert.True(await service.IsEnabledAsync(OnFlag, UserA));
    }

    // ── Rollout ──

    [Fact]
    public async Task IsEnabled_PartialRollout_DeterministicPerUser()
    {
        var (service, _) = CreateService();
        var bucket = FeatureFlagService.HashBucket(UserA, PartialFlag);
        var expected = bucket < 50;

        Assert.Equal(expected, await service.IsEnabledAsync(PartialFlag, UserA));
        Assert.Equal(expected, await service.IsEnabledAsync(PartialFlag, UserA));
    }

    [Fact]
    public async Task IsEnabled_Anonymous_PartialRollout_False()
    {
        var (service, _) = CreateService();

        Assert.False(await service.IsEnabledAsync(PartialFlag));
    }

    [Fact]
    public async Task IsEnabled_RolloutZero_FalseForEveryone()
    {
        var (service, _) = CreateService();

        Assert.False(await service.IsEnabledAsync(ZeroFlag, UserA));
        Assert.False(await service.IsEnabledAsync(ZeroFlag));
    }

    [Fact]
    public void HashBucket_Deterministic_AndInRange()
    {
        var first = FeatureFlagService.HashBucket(UserA, PartialFlag);
        var second = FeatureFlagService.HashBucket(UserA, PartialFlag);

        Assert.Equal(first, second);
        Assert.InRange(first, 0, 99);
    }

    // ── List ──

    [Fact]
    public async Task List_NoOverrides_ShowsConfigDefaults()
    {
        var (service, _) = CreateService();

        var flags = await service.ListAsync();

        Assert.Equal(4, flags.Count);
        var on = flags.Single(f => f.Key == OnFlag);
        Assert.True(on.DefaultEnabled);
        Assert.Equal(100, on.DefaultRolloutPercent);
        Assert.Null(on.OverrideEnabled);
        Assert.True(on.EffectiveEnabled);
    }

    [Fact]
    public async Task List_AfterOverride_ShowsOverrideAndEffective()
    {
        var (service, _) = CreateService();
        await service.SetOverrideAsync(OffFlag, true, 42);

        var flags = await service.ListAsync();
        var off = flags.Single(f => f.Key == OffFlag);

        Assert.True(off.OverrideEnabled);
        Assert.Equal(42, off.OverrideRolloutPercent);
        Assert.True(off.EffectiveEnabled);
        Assert.Equal(42, off.EffectiveRolloutPercent);
    }

    private static (FeatureFlagService Service, AppDbContext Db) CreateService(bool killSwitch = true)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        var db = new AppDbContext(options);

        var settings = Options.Create(new FeatureFlagSettings
        {
            Enabled = killSwitch,
            Flags = new Dictionary<string, FeatureFlagConfig>
            {
                [OnFlag] = new() { Enabled = true, RolloutPercent = 100, Description = "On" },
                [OffFlag] = new() { Enabled = false, RolloutPercent = 100, Description = "Off" },
                [PartialFlag] = new() { Enabled = true, RolloutPercent = 50, Description = "Half" },
                [ZeroFlag] = new() { Enabled = true, RolloutPercent = 0, Description = "Zero" },
            }
        });

        var cache = new CacheService(
            new FusionCache(new FusionCacheOptions()),
            Options.Create(new CacheSettings { Enabled = false }));

        return (new FeatureFlagService(cache, settings, db), db);
    }
}