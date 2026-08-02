using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Infrastructure;

namespace QuickShare.Server.Tests.E2E;

public sealed class DiagRewardTest : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DiagRewardTest(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Dump_RewardCallback_Response()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);

        // Check the seed is visible from a fresh scope in the SAME factory.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var loginCount = await db.UserLogins.CountAsync();
            var login = await db.UserLogins
                .FirstOrDefaultAsync(l => l.ProviderKey == userId);
            Assert.Fail($"SeedLoginVisible={login is not null} totalLogins={loginCount} " +
                        $"seedId={userId} dbName={db.Database.ProviderName}");
        }
    }
}
