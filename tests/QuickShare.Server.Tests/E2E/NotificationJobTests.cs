using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Notification sources + retention (issue #246): expiring a delivery notifies its sender,
/// and the cleanup job purges notifications older than the retention window.
/// </summary>
public sealed class NotificationJobTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public NotificationJobTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DeliveryExpiryJob_CreatesNotificationForSender()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var sender = await _factory.SeedUserAsync(providerKey);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                SenderId = sender.Id,
                FileName = "expires-soon.pdf",
                ContentType = "application/pdf",
                ShareCode = "exp" + Guid.NewGuid().ToString("N")[..8],
                Status = DeliveryStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var storage = _factory.Services.GetRequiredService<IStorageProvider>();
        var job = new DeliveryExpiryJob(scopeFactory, storage, NullLogger<DeliveryExpiryJob>.Instance);
        await job.ExpireDeliveriesAsync();

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notification = await db.Notifications
                .SingleAsync(n => n.UserId == sender.Id && n.Type == NotificationType.DeliveryExpired);
            Assert.Equal("expires-soon.pdf", notification.Title);
            Assert.Null(notification.LinkUrl);
        }
    }

    [Fact]
    public async Task DeliveryExpiryJob_SkipsAnonymousDeliveries()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                SenderId = null,
                FileName = "anon.pdf",
                ContentType = "application/pdf",
                ShareCode = "anon" + Guid.NewGuid().ToString("N")[..8],
                Status = DeliveryStatus.Active,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync();
        }

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var storage = _factory.Services.GetRequiredService<IStorageProvider>();
        var job = new DeliveryExpiryJob(scopeFactory, storage, NullLogger<DeliveryExpiryJob>.Instance);
        await job.ExpireDeliveriesAsync();

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.Notifications.CountAsync();
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task NotificationCleanupJob_PurgesOldNotifications()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Notifications.AddRange(
                new Notification { UserId = user.Id, Type = NotificationType.Info, Title = "recent" },
                new Notification { UserId = user.Id, Type = NotificationType.Info, Title = "old" });
            await db.SaveChangesAsync();

            // CreatedAt is interceptor-owned, so backdate the "old" row after insert.
            await db.Notifications
                .Where(n => n.Title == "old")
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, DateTime.UtcNow.AddDays(-40)));
        }

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var job = new NotificationCleanupJob(scopeFactory, NullLogger<NotificationCleanupJob>.Instance);
        await job.CleanupAsync();

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var titles = await db.Notifications.Select(n => n.Title).ToListAsync();
            Assert.Contains("recent", titles);
            Assert.DoesNotContain("old", titles);
        }
    }
}
