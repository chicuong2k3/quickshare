using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Email;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Sender-expiry reminder (issue #29): only active deliveries expiring within
/// 24h whose sender opted in with an email are reminded, and only once.
/// </summary>
public sealed class DeliveryExpiryReminderJobTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DeliveryExpiryReminderJobTests(TestWebAppFactory factory) => _factory = factory;

    private static readonly string DueCode = "exp" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string NoEmailCode = "exp" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string NoConsentCode = "exp" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string FarCode = "exp" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string ExpiredCode = "exp" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string RemindedCode = "exp" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Remind_MarksOnlyEligibleDeliveries()
    {
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var emailSender = _factory.Services.GetRequiredService<EmailSender>();
        var emailOptions = _factory.Services.GetRequiredService<IOptions<EmailOptions>>();

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.AddRange(
                new Delivery
                {
                    FileName = "due.bin",
                    ShareCode = DueCode,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddHours(12),
                    SenderEmail = "sender@example.com",
                    SenderEmailConsent = true,
                },
                new Delivery
                {
                    FileName = "no-email.bin",
                    ShareCode = NoEmailCode,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddHours(12),
                },
                new Delivery
                {
                    FileName = "no-consent.bin",
                    ShareCode = NoConsentCode,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddHours(12),
                    SenderEmail = "sender@example.com",
                },
                new Delivery
                {
                    FileName = "far.bin",
                    ShareCode = FarCode,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddDays(5),
                    SenderEmail = "sender@example.com",
                    SenderEmailConsent = true,
                },
                new Delivery
                {
                    FileName = "expired.bin",
                    ShareCode = ExpiredCode,
                    Status = DeliveryStatus.Expired,
                    ExpiresAt = DateTime.UtcNow.AddHours(12),
                    SenderEmail = "sender@example.com",
                    SenderEmailConsent = true,
                },
                new Delivery
                {
                    FileName = "reminded.bin",
                    ShareCode = RemindedCode,
                    Status = DeliveryStatus.Active,
                    ExpiresAt = DateTime.UtcNow.AddHours(12),
                    SenderEmail = "sender@example.com",
                    SenderEmailConsent = true,
                    ExpiryReminderSentAt = DateTime.UtcNow.AddDays(-1),
                });
            await db.SaveChangesAsync();
        }

        var job = new DeliveryExpiryReminderJob(scopeFactory, emailSender, NullLogger<DeliveryExpiryReminderJob>.Instance);
        await job.RemindAsync();

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var due = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == DueCode);
            var noEmail = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == NoEmailCode);
            var noConsent = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == NoConsentCode);
            var far = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == FarCode);
            var expired = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == ExpiredCode);
            var reminded = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == RemindedCode);

            Assert.NotNull(due.ExpiryReminderSentAt);
            Assert.Null(noEmail.ExpiryReminderSentAt);
            Assert.Null(noConsent.ExpiryReminderSentAt);
            Assert.Null(far.ExpiryReminderSentAt);
            Assert.Null(expired.ExpiryReminderSentAt);
            Assert.Equal(DateTime.UtcNow.AddDays(-1).Date, reminded.ExpiryReminderSentAt!.Value.Date);
        }
    }
}