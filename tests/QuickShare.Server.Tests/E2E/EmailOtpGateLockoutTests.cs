using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Otp;
using QuickShare.Features.Downloads.Download;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public sealed class EmailOtpGateLockoutTests : IClassFixture<TestWebAppFactory>
{
    private const string Password = "correct-password";
    private const string Code = "123456";
    private const string WrongCode = "000000";
    private const string Email = "recipient@example.com";
    private const int BarrierTimeoutSeconds = 30;
    private readonly TestWebAppFactory _factory;

    public EmailOtpGateLockoutTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EmailOtpFailures_LockTheDelivery()
    {
        var shareCode = await SeedAsync(withPassword: false, requireRecipient: false);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var response = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={WrongCode}&email={Email}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var locked = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={Code}&email={Email}");
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task RequestsWithoutEmailOtp_DoNotLockDelivery()
    {
        var shareCode = await SeedAsync(withPassword: false, requireRecipient: false);
        using var client = Client();
        for (var i = 0; i <= OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var response = await client.GetAsync($"/api/downloads/{shareCode}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var valid = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={Code}&email={Email}");
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InactiveEmailOtp_DoesNotLockDelivery(bool expired)
    {
        var shareCode = await SeedAsync(withPassword: false, requireRecipient: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.SingleAsync(d => d.ShareCode == shareCode);
            if (expired)
            {
                delivery.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            }
            else
            {
                delivery.EmailOtpUsedAt = DateTime.UtcNow.AddMinutes(-11);
            }
            await db.SaveChangesAsync();
        }

        using var client = Client();
        for (var i = 0; i <= OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var response = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={WrongCode}&email={Email}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.SingleAsync(d => d.ShareCode == shareCode);
            delivery.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(10);
            delivery.EmailOtpUsedAt = null;
            await db.SaveChangesAsync();
        }

        using var valid = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={Code}&email={Email}");
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
    }

    [Fact]
    public async Task CorrectPassword_CannotResetEmailOtpFailures()
    {
        var shareCode = await SeedAsync(withPassword: true, requireRecipient: false);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var password = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/verify-password", new { password = Password });
            Assert.Equal(HttpStatusCode.OK, password.StatusCode);
            using var bad = await client.GetAsync($"/api/downloads/{shareCode}?password={Password}&emailOtp={WrongCode}&email={Email}");
            Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        }

        using var locked = await client.GetAsync($"/api/downloads/{shareCode}?password={Password}&emailOtp={Code}&email={Email}");
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task RecipientGateFailure_DoesNotConsumeEmailOtp()
    {
        var shareCode = await SeedAsync(withPassword: false, requireRecipient: true);
        using var client = Client();
        using (var rejected = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={Code}&email={Email}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.Deliveries.Where(d => d.ShareCode == shareCode).Select(d => d.EmailOtpUsedAt).SingleAsync());
        }

        using var accepted = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={Code}&email={Email}&recipientEmail={Email}");
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull(await verifyDb.Deliveries.Where(d => d.ShareCode == shareCode).Select(d => d.EmailOtpUsedAt).SingleAsync());
    }

    [Fact]
    public async Task RetryWithinGrace_PreservesFirstUseTime()
    {
        var shareCode = await SeedAsync(withPassword: false, requireRecipient: false);
        var firstUse = DateTime.UtcNow.AddMinutes(-9);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.SingleAsync(d => d.ShareCode == shareCode);
            delivery.EmailOtpUsedAt = firstUse;
            await db.SaveChangesAsync();
        }

        using var client = Client();
        using var response = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp={Code}&email={Email}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(firstUse, await verifyDb.Deliveries.Where(d => d.ShareCode == shareCode).Select(d => d.EmailOtpUsedAt).SingleAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReissuedOrExpiredCode_IsNotConsumedByAnEarlierRequest(bool reissued)
    {
        var shareCode = await SeedAsync(withPassword: false, requireRecipient: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var seedDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deliveryId = await seedDb.Deliveries.Where(d => d.ShareCode == shareCode).Select(d => d.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<OtpLockoutService>().RecordFailureAsync(seedDb, deliveryId, CancellationToken.None);
        }
        var storage = new BlockingStorage();
        using var requestScope = _factory.Services.CreateScope();
        var db = requestScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
        context.Response.Body = new MemoryStream();
        accessor.HttpContext = context;
        var handler = new DownloadDeliveryHandler(storage, accessor, db,
            requestScope.ServiceProvider.GetRequiredService<OtpLockoutService>(),
            requestScope.ServiceProvider.GetRequiredService<DeliveryAccessService>(),
            requestScope.ServiceProvider.GetRequiredService<QuickShare.Features.Workspaces.Domains.WorkspaceDomainReader>());
        var query = new DownloadDeliveryQuery(shareCode, null, null, Code, Email, null, false);
        var request = handler.Handle(query, CancellationToken.None).AsTask();

        try
        {
            await storage.Entered.Task.WaitAsync(TimeSpan.FromSeconds(BarrierTimeoutSeconds));
            using var replacementScope = _factory.Services.CreateScope();
            var replacementDb = replacementScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await replacementDb.Deliveries.SingleAsync(d => d.ShareCode == shareCode);
            if (reissued)
            {
                delivery.EmailOtpHash = BCrypt.Net.BCrypt.HashPassword("654321");
                delivery.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(15);
            }
            else
            {
                delivery.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            }
            delivery.EmailOtpUsedAt = null;
            await replacementDb.SaveChangesAsync();
        }
        finally
        {
            storage.Release.TrySetResult();
        }

        await request;
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verifiedDelivery = await verifyDb.Deliveries.AsNoTracking().SingleAsync(d => d.ShareCode == shareCode);
        Assert.Null(verifiedDelivery.EmailOtpUsedAt);
        var failures = await verifyDb.OtpLockouts.Where(l => l.DeliveryId == verifiedDelivery.Id).Select(l => l.FailedAttempts).SingleAsync();
        Assert.Equal(1, failures);
    }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    private async Task<string> SeedAsync(bool withPassword, bool requireRecipient)
    {
        var shareCode = $"eotp{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "email-otp.bin",
            ContentType = "application/octet-stream",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            PasswordHash = withPassword ? BCrypt.Net.BCrypt.HashPassword(Password) : null,
            EmailOtpEnabled = true,
            EmailOtpHash = BCrypt.Net.BCrypt.HashPassword(Code),
            EmailOtpEmail = Email,
            EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(10),
            RequireRecipientEmail = requireRecipient,
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    private sealed class BlockingStorage : IStorageProvider
    {
        private readonly LocalDiskStorageProviderStub _inner = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return await _inner.GetPresignedDownloadUrlAsync(objectKey, expiry, ct);
        }

        public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, string? region = null, CancellationToken ct = default) => _inner.InitR2MultipartUploadAsync(deliveryId, fileName, region, ct);
        public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default) => _inner.GenerateUploadPartUrlsAsync(uploadId, objectKey, partNumbers, ct);
        public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default) => _inner.CompleteR2MultipartUploadAsync(uploadId, objectKey, parts, ct);
        public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default) => _inner.AbortR2MultipartUploadAsync(uploadId, objectKey, ct);
        public Task UploadObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default) => _inner.UploadObjectAsync(objectKey, content, contentType, ct);
        public Task<long> GetObjectSizeAsync(string objectKey, CancellationToken ct = default) => _inner.GetObjectSizeAsync(objectKey, ct);
        public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default) => _inner.DeleteObjectsAsync(prefix, ct);
        public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default) => _inner.DeleteObjectAsync(objectKey, ct);
        public Task CopyObjectAsync(string sourceKey, string destinationKey, CancellationToken ct = default) => _inner.CopyObjectAsync(sourceKey, destinationKey, ct);
        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default) => _inner.OpenReadAsync(objectKey, ct);
        public Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, DateTime olderThan, CancellationToken ct = default) => _inner.ListObjectsAsync(prefix, olderThan, ct);
        public Task<IReadOnlyList<R2MultipartInfo>> ListMultipartUploadsAsync(string prefix, DateTime olderThan, CancellationToken ct = default) => _inner.ListMultipartUploadsAsync(prefix, olderThan, ct);
    }
}
