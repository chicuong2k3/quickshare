using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

public sealed class DiagRewardTest : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DiagRewardTest(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Dump_CrossScopePersistence()
    {
        var userId = Guid.NewGuid().ToString();

        // Scope A: save.
        using (var scopeA = _factory.Services.CreateScope())
        {
            var db = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = new User
            {
                Id = Guid.Parse(userId),
                Name = "Test User",
                Email = "test@localhost",
                Plan = "free"
            };
            db.Users.Add(user);
            db.UserLogins.Add(new UserLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                Provider = "test",
                ProviderKey = userId
            });
            var saved = await db.SaveChangesAsync();

            // Scope B: read in a DIFFERENT scope from the same host.
            using (var scopeB = _factory.Services.CreateScope())
            {
                var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();
                var readBack = await dbB.UserLogins.CountAsync(l => l.ProviderKey == userId);
                Assert.Fail($"SavedRows={saved} ReadBackOtherScope={readBack} " +
                            $"SameProvider={ReferenceEquals(scopeA.ServiceProvider, scopeB.ServiceProvider)}");
            }
        }
    }
}
