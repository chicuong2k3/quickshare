using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Referral program (issue #15, layer 1): code claim on signup, +2GB for both
/// sides on first upload, self-referral ignored, 16GB cap, quota includes bonus.
/// </summary>
public sealed class ReferralTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public ReferralTests(TestWebAppFactory factory) => _factory = factory;

    private static long Gb(int n) => n * 1024L * 1024 * 1024;

    [Fact]
    public async Task SignupWithReferralCode_ClaimsReferrer()
    {
        var referrer = await _factory.SeedUserAsync(Guid.NewGuid().ToString());
        referrer.ReferralCode = "ABCDEFG1";
        await SaveAsync(referrer);

        var newId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
            await UserRegistrationService.RegisterAsync(
                "google", newId.ToString(), "New", "new@test.dev", null, db, CancellationToken.None, referralCode: "ABCDEFG1");
        }

        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var newUser = await checkDb.UserLogins
            .Include(l => l.User)
            .Where(l => l.Provider == "google" && l.ProviderKey == newId.ToString() && l.User != null)
            .Select(l => l.User!)
            .FirstAsync();
        Assert.Equal(referrer.Id, newUser.ReferredByUserId);
        Assert.True(await checkDb.ReferralEvents.AnyAsync(e => e.ReferredUserId == newUser.Id && e.Type == ReferralEventTypes.Pending));
    }

    [Fact]
    public async Task SelfReferral_IsIgnored()
    {
        var userId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
            await UserRegistrationService.RegisterAsync(
                "google", userId.ToString(), "Self", "self@test.dev", null, db, CancellationToken.None);
        }

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var user = await db2.UserLogins
            .Include(l => l.User)
            .Where(l => l.Provider == "google" && l.ProviderKey == userId.ToString() && l.User != null)
            .Select(l => l.User!)
            .FirstAsync();
        await ReferralService.ClaimAsync(db2, user.Id, user.ReferralCode, CancellationToken.None);
        await db2.SaveChangesAsync();

        var fresh = await db2.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Null(fresh.ReferredByUserId);
        Assert.False(await db2.ReferralEvents.AnyAsync(e => e.ReferredUserId == user.Id));
    }

    [Fact]
    public async Task UnknownReferralCode_IsIgnored()
    {
        var newId = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
            await UserRegistrationService.RegisterAsync(
                "google", newId.ToString(), "New", "new2@test.dev", null, db, CancellationToken.None, referralCode: "NOSUCHCODE");
        }

        using var check = _factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var user = await checkDb.UserLogins
            .Include(l => l.User)
            .Where(l => l.Provider == "google" && l.ProviderKey == newId.ToString() && l.User != null)
            .Select(l => l.User!)
            .FirstAsync();
        Assert.Null(user.ReferredByUserId);
    }

    [Fact]
    public async Task FirstUpload_ReleasesBonusToBoth()
    {
        var referrer = await SeedReferrerAsync();
        var referred = await SeedReferredAsync(referrer.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var referredUser = await db.Users.FirstAsync(u => u.Id == referred.Id);

        await ReferralService.ReleaseOnFirstUploadAsync(referredUser, db, CancellationToken.None);
        await db.SaveChangesAsync();

        var refAfter = await db.Users.AsNoTracking().FirstAsync(u => u.Id == referrer.Id);
        var referredAfter = await db.Users.AsNoTracking().FirstAsync(u => u.Id == referred.Id);
        Assert.Equal(Gb(2), refAfter.BonusQuotaBytes);
        Assert.Equal(Gb(2), referredAfter.BonusQuotaBytes);
        Assert.True(await db.ReferralEvents.AnyAsync(e =>
            e.ReferredUserId == referred.Id && e.Type == ReferralEventTypes.Rewarded));
    }

    [Fact]
    public async Task BonusCap_StopsAt16Gb()
    {
        var referrer = await SeedReferrerAsync(bonusGb: 15);
        var referred = await SeedReferredAsync(referrer.Id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var referredUser = await db.Users.FirstAsync(u => u.Id == referred.Id);

        await ReferralService.ReleaseOnFirstUploadAsync(referredUser, db, CancellationToken.None);
        await db.SaveChangesAsync();

        var refAfter = await db.Users.AsNoTracking().FirstAsync(u => u.Id == referrer.Id);
        Assert.Equal(Gb(16), refAfter.BonusQuotaBytes);
    }

    [Fact]
    public async Task QuotaCheck_IncludesBonus()
    {
        var user = await SeedReferrerAsync(bonusGb: 2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var quota = new QuotaService(db);

        var result = await quota.CheckAsync(user.Id, PlanTierLimits.Free, Gb(1), CancellationToken.None);
        Assert.False(result.IsExceeded);
        Assert.Equal(PlanTierLimits.FreeMonthlyQuotaBytes + Gb(2), result.Quota);
    }

    private async Task<QuickShare.Data.Entities.User> SeedReferrerAsync(int bonusGb = 0)
    {
        var referrer = await _factory.SeedUserAsync(Guid.NewGuid().ToString());
        referrer.ReferralCode = "REF" + Guid.NewGuid().ToString("N")[..5];
        referrer.BonusQuotaBytes = Gb(bonusGb);
        await SaveAsync(referrer);
        return referrer;
    }

    private async Task<QuickShare.Data.Entities.User> SeedReferredAsync(Guid referrerId)
    {
        var referred = await _factory.SeedUserAsync(Guid.NewGuid().ToString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var tracked = await db.Users.FirstAsync(u => u.Id == referred.Id);
        tracked.ReferredByUserId = referrerId;
        db.ReferralEvents.Add(new ReferralEvent
        {
            Id = Guid.CreateVersion7(),
            ReferrerUserId = referrerId,
            ReferredUserId = referred.Id,
            Type = ReferralEventTypes.Pending
        });
        await db.SaveChangesAsync();
        return referred;
    }

    private async Task SaveAsync(QuickShare.Data.Entities.User user)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var tracked = await db.Users.FirstAsync(u => u.Id == user.Id);
        tracked.ReferralCode = user.ReferralCode;
        tracked.BonusQuotaBytes = user.BonusQuotaBytes;
        await db.SaveChangesAsync();
    }
}
