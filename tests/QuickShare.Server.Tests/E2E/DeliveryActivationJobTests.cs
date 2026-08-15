using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Scheduled send (issue #69): only deliveries whose DeliverAt has passed are
/// activated; future-scheduled deliveries stay Scheduled.
/// </summary>
public sealed class DeliveryActivationJobTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DeliveryActivationJobTests(TestWebAppFactory factory) => _factory = factory;

    private static readonly string DueCode = "sched" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string FutureCode = "sched" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Activate_FlipsOnlyDueDeliveries()
    {
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.AddRange(
                new Delivery
                {
                    FileName = "due.bin",
                    ShareCode = DueCode,
                    Status = DeliveryStatus.Scheduled,
                    DeliverAt = DateTime.UtcNow.AddMinutes(-1),
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                },
                new Delivery
                {
                    FileName = "future.bin",
                    ShareCode = FutureCode,
                    Status = DeliveryStatus.Scheduled,
                    DeliverAt = DateTime.UtcNow.AddHours(2),
                    ExpiresAt = DateTime.UtcNow.AddDays(30),
                });
            await db.SaveChangesAsync();
        }

        var job = new DeliveryActivationJob(scopeFactory, NullLogger<DeliveryActivationJob>.Instance);
        await job.ActivateDeliveriesAsync(CancellationToken.None);

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var due = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == DueCode);
            var future = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == FutureCode);

            Assert.Equal(DeliveryStatus.Active, due.Status);
            Assert.Equal(DeliveryStatus.Scheduled, future.Status);
        }
    }
}
