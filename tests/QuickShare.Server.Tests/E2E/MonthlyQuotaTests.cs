using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Monthly upload quota enforcement (issue #10): authenticated uploads are
/// checked against a per-plan monthly byte cap before start-r2 and commit-r2,
/// and bytes are accumulated on successful commit. Anonymous uploads keep only
/// the per-file cap.
/// </summary>
public sealed class MonthlyQuotaTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public MonthlyQuotaTests(TestWebAppFactory factory) => _factory = factory;

    private static StartR2Request SmallUpload() => new()
    {
        FileName = "quota.bin",
        FileSize = 1024,
        ContentType = "application/octet-stream",
        PartNumbers = [1],
    };

    private static DateTime CurrentMonthStartUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private async Task SeedUsageAsync(Guid userId, long bytes, DateTime? periodStart = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserUsages.Add(new UserUsage
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            PeriodStart = periodStart ?? CurrentMonthStartUtc(),
            TotalBytesUploaded = bytes,
            DailyBandwidthBonus = 0,
            TransferCount = 1
        });
        await db.SaveChangesAsync();
    }

    private async Task<long> GetUsedBytesAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usage = await db.UserUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId && u.PeriodStart == CurrentMonthStartUtc());
        return usage?.TotalBytesUploaded ?? 0;
    }

    [Fact]
    public async Task QuotaExceeded_StartR2_Returns400WithErrorCode()
    {
        var userId = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        await SeedUsageAsync(user.Id, PlanTierLimits.FreeMonthlyQuotaBytes);
        var client = _factory.CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", SmallUpload());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ErrorCodes.MonthlyQuotaExceeded, body);
    }

    [Fact]
    public async Task NewMonth_ResetsQuota()
    {
        var userId = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        await SeedUsageAsync(user.Id, PlanTierLimits.FreeMonthlyQuotaBytes, CurrentMonthStartUtc().AddMonths(-1));
        var client = _factory.CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", SmallUpload());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProUser_HasHigherMonthlyCap()
    {
        var userId = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(userId, PlanTierLimits.Pro);
        await SeedUsageAsync(user.Id, PlanTierLimits.FreeMonthlyQuotaBytes);
        var client = _factory.CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", SmallUpload());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Commit_RecordsActualBytesIntoUsage()
    {
        var stub = (LocalDiskStorageProviderStub)_factory.Services.GetRequiredService<IStorageProvider>();
        var userId = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(userId);

        const long actualBytes = 2L * 1024 * 1024;
        stub.NextObjectSize = actualBytes;
        try
        {
            var start = await (await client.PostAsJsonAsync("/api/uploads/start-r2", SmallUpload()))
                .Content.ReadFromJsonAsync<StartR2Response>();
            Assert.NotNull(start);

            var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
            {
                DeliveryId = start.DeliveryId,
                UploadId = start.UploadId,
                ObjectKey = start.ObjectKey,
                FileName = "quota.bin",
                ContentType = "application/octet-stream",
                FileSize = 1024,
                Parts = [new R2PartEtag(1, "etag-1")],
            });
            Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        }
        finally
        {
            stub.NextObjectSize = -1;
        }

        Assert.Equal(actualBytes, await GetUsedBytesAsync(user.Id));
    }

    [Fact]
    public async Task Downgrade_BlocksImmediately_WhenNewQuotaExceeded()
    {
        var userId = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        await SeedUsageAsync(user.Id, 20L * 1024 * 1024 * 1024);
        var client = _factory.CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", SmallUpload());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(ErrorCodes.MonthlyQuotaExceeded, body);
    }

    [Fact]
    public async Task AccountUsage_Authenticated_ReturnsUsageCard()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, TestWebAppFactory.TestUserProviderKey);
        var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountUsage);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("usage-card", body);
    }
}
