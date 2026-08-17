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
/// File-request digest (issue #16): only active deliveries submitted to a request are
/// digested, and each is marked exactly once regardless of email send outcome.
/// </summary>
public sealed class FileRequestDigestJobTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public FileRequestDigestJobTests(TestWebAppFactory factory) => _factory = factory;

    private static readonly string PendingCode = "fr" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string NoRequestCode = "fr" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string AlreadyNotifiedCode = "fr" + Guid.NewGuid().ToString("N")[..8];
    private static readonly string InactiveCode = "fr" + Guid.NewGuid().ToString("N")[..8];

    private async Task<Guid> SeedOwnerAndRequestAsync()
    {
        var owner = await _factory.SeedUserAsync(Guid.CreateVersion7().ToString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = new FileRequest
        {
            OwnerId = owner.Id,
            Code = "rq" + Guid.NewGuid().ToString("N")[..10],
            Title = "Digest request",
            MaxTotalBytes = 2L * 1024 * 1024 * 1024,
            MaxFiles = 20,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Kind = FileRequestKind.Request,
        };
        db.FileRequests.Add(request);

        db.Deliveries.AddRange(
            new Delivery
            {
                FileName = "pending.bin",
                ShareCode = PendingCode,
                Status = DeliveryStatus.Active,
                FileRequestId = request.Id,
            },
            new Delivery
            {
                FileName = "no-request.bin",
                ShareCode = NoRequestCode,
                Status = DeliveryStatus.Active,
                FileRequestId = null,
            },
            new Delivery
            {
                FileName = "already-notified.bin",
                ShareCode = AlreadyNotifiedCode,
                Status = DeliveryStatus.Active,
                FileRequestId = request.Id,
                DigestNotifiedAt = DateTime.UtcNow.AddDays(-1),
            },
            new Delivery
            {
                FileName = "inactive.bin",
                ShareCode = InactiveCode,
                Status = DeliveryStatus.Expired,
                FileRequestId = request.Id,
            });

        await db.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task Notify_MarksOnlyEligibleDeliveries()
    {
        await SeedOwnerAndRequestAsync();

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var emailSender = _factory.Services.GetRequiredService<EmailSender>();
        var unsubscribeTokens = _factory.Services.GetRequiredService<UnsubscribeTokens>();
        var emailOptions = _factory.Services.GetRequiredService<IOptions<EmailOptions>>();

        var job = new FileRequestDigestJob(
            scopeFactory, emailSender, unsubscribeTokens, emailOptions,
            NullLogger<FileRequestDigestJob>.Instance);

        // Email is not configured in tests, so SendAsync throws — the job must still
        // mark every eligible delivery rather than replaying the digest forever.
        await job.NotifyAsync();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == PendingCode);
        var noRequest = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == NoRequestCode);
        var alreadyNotified = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == AlreadyNotifiedCode);
        var inactive = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == InactiveCode);

        Assert.NotNull(pending.DigestNotifiedAt);
        Assert.Null(noRequest.DigestNotifiedAt);
        Assert.Equal(DateTime.UtcNow.AddDays(-1).Date, alreadyNotified.DigestNotifiedAt!.Value.Date);
        Assert.Null(inactive.DigestNotifiedAt);
    }
}
