using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Full upload → share → download flow through the real HTTP pipeline
/// (R2 storage stubbed, everything else real).
/// </summary>
public sealed class UploadDownloadFlowTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "report.pdf";
    private const string TestContentType = "application/pdf";
    private const long TestFileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public UploadDownloadFlowTests(TestWebAppFactory factory) => _factory = factory;

    private HttpClient CreateNoRedirectClient(bool authenticated = false)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (authenticated)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeader, "1");
        return client;
    }

    private static async Task<CommitR2Response> UploadFileAsync(HttpClient client, string? otp = null)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);
        Assert.NotEmpty(start.Parts);

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            Otp = otp,
        });
        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        var commit = await commitResponse.Content.ReadFromJsonAsync<CommitR2Response>();
        Assert.NotNull(commit);
        Assert.NotEmpty(commit.ShareCode);
        return commit;
    }

    [Fact]
    public async Task FullFlow_Upload_Share_Download()
    {
        var client = CreateNoRedirectClient(authenticated: true);

        var commit = await UploadFileAsync(client);
        Assert.Contains($"/d/{commit.ShareCode}", commit.ShareLink);

        var download = await client.GetAsync($"/api/downloads/{commit.ShareCode}");
        Assert.Equal(HttpStatusCode.Found, download.StatusCode);
        Assert.Equal("http://stub.local/download", download.Headers.Location!.AbsoluteUri);
    }

    [Fact]
    public async Task Download_IsAnonymous_NoAuthNeeded()
    {
        var authed = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(authed);

        var anonymous = CreateNoRedirectClient();
        var download = await anonymous.GetAsync($"/api/downloads/{commit.ShareCode}");
        Assert.Equal(HttpStatusCode.Found, download.StatusCode);
    }

    [Fact]
    public async Task Download_IncrementsDownloadCount()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(client);

        var download = await client.GetAsync($"/api/downloads/{commit.ShareCode}");
        Assert.Equal(HttpStatusCode.Found, download.StatusCode);

        // Counter update runs on a background task — poll briefly.
        var counted = false;
        for (var i = 0; i < 20 && !counted; i++)
        {
            await Task.Delay(100);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.AsNoTracking()
                .FirstAsync(d => d.ShareCode == commit.ShareCode);
            counted = delivery.DownloadCount == 1;
        }
        Assert.True(counted, "DownloadCount was not incremented within 2s");
    }

    [Fact]
    public async Task AnonymousUpload_FullFlow_Succeeds()
    {
        var client = CreateNoRedirectClient();

        var commit = await UploadFileAsync(client);
        Assert.NotEmpty(commit.ShareCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == commit.ShareCode);
            Assert.Null(delivery.SenderId);
        }

        var download = await client.GetAsync($"/api/downloads/{commit.ShareCode}");
        Assert.Equal(HttpStatusCode.Found, download.StatusCode);
    }

    [Fact]
    public async Task AnonymousUpload_ExceedingAnonymousLimit_Returns400()
    {
        var client = CreateNoRedirectClient();
        var overAnonymousLimit = 104_857_600L + 1;
        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = overAnonymousLimit,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousUpload_PartUrls_WorkForOwnSession()
    {
        var client = CreateNoRedirectClient();
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var response = await client.PostAsJsonAsync("/api/uploads/part-urls", new
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            PartNumbers = new[] { 1 },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedSession_PartUrls_NotVisibleToAnonymous()
    {
        var authed = CreateNoRedirectClient(authenticated: true);
        var startResponse = await authed.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var anonymous = CreateNoRedirectClient();
        var response = await anonymous.PostAsJsonAsync("/api/uploads/part-urls", new
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            PartNumbers = new[] { 1 },
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StartUpload_ExceedingFreePlanLimit_Returns400()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var overFreeLimit = 2_147_483_648L + 1;
        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = overFreeLimit,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartUpload_EmptyFileName_Returns400()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = "",
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PartUrls_InProgressSession_ReturnsFreshUrls()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1, 2],
        });
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var response = await client.PostAsJsonAsync("/api/uploads/part-urls", new
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            PartNumbers = new[] { 2 },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"partNumber\":2", body);
    }

    [Fact]
    public async Task PartUrls_CommittedSession_Returns404()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(client);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == commit.ShareCode);

        var response = await client.PostAsJsonAsync("/api/uploads/part-urls", new
        {
            DeliveryId = delivery.Id,
            UploadId = "stub-upload-id",
            PartNumbers = new[] { 1 },
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PartUrls_UnknownSession_Returns404()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var response = await client.PostAsJsonAsync("/api/uploads/part-urls", new
        {
            DeliveryId = Guid.NewGuid(),
            UploadId = "no-such-upload",
            PartNumbers = new[] { 1 },
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Commit_WrongUploadId_Returns400()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var start = await (await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        })).Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = "forged-upload-id",
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
        });
        Assert.Equal(HttpStatusCode.BadRequest, commit.StatusCode);
    }

    [Fact]
    public async Task Commit_OtherUsersSession_Returns400()
    {
        var authed = CreateNoRedirectClient(authenticated: true);
        var start = await (await authed.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        })).Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var anonymous = CreateNoRedirectClient();
        var commit = await anonymous.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
        });
        Assert.Equal(HttpStatusCode.BadRequest, commit.StatusCode);
    }

    [Fact]
    public async Task Abort_OtherUsersSession_IsNoOp()
    {
        var authed = CreateNoRedirectClient(authenticated: true);
        var start = await (await authed.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        })).Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var anonymous = CreateNoRedirectClient();
        var abort = await anonymous.PostAsJsonAsync("/api/uploads/abort-r2", new
        {
            start.DeliveryId,
            start.UploadId,
            start.ObjectKey,
        });
        Assert.Equal(HttpStatusCode.OK, abort.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UploadSessions.FindAsync(start.DeliveryId);
        Assert.NotNull(session);
    }

    [Fact]
    public async Task StartUpload_PartCountExceedingDeclaredSize_Returns400()
    {
        var client = CreateNoRedirectClient();
        var manyParts = Enumerable.Range(1, 50).ToArray();
        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize, // 1 KB declared, 50 parts requested
            ContentType = TestContentType,
            PartNumbers = manyParts,
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Commit_ActualSizeOverPlanLimit_Returns400()
    {
        var stub = (LocalDiskStorageProviderStub)_factory.Services.GetRequiredService<IStorageProvider>();
        var client = CreateNoRedirectClient();
        var start = await (await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        })).Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        stub.NextObjectSize = 104_857_600L * 3; // 300 MB actually stored vs 1 KB declared
        try
        {
            var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
            {
                DeliveryId = start.DeliveryId,
                UploadId = start.UploadId,
                ObjectKey = start.ObjectKey,
                FileName = TestFileName,
                ContentType = TestContentType,
                FileSize = TestFileSize,
                Parts = [new R2PartEtag(1, "etag-1")],
            });
            Assert.Equal(HttpStatusCode.BadRequest, commit.StatusCode);
        }
        finally
        {
            stub.NextObjectSize = -1;
        }
    }

    [Fact]
    public async Task Download_UnknownShareCode_Returns404()
    {
        var client = CreateNoRedirectClient();
        var response = await client.GetAsync("/api/downloads/no-such-code");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OtpProtected_Download_RequiresCorrectOtp()
    {
        const string otp = "123456";
        var client = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(client, otp);

        var noOtp = await client.GetAsync($"/api/downloads/{commit.ShareCode}");
        Assert.Equal(HttpStatusCode.Forbidden, noOtp.StatusCode);

        var wrongOtp = await client.GetAsync($"/api/downloads/{commit.ShareCode}?otp=999999");
        Assert.Equal(HttpStatusCode.Forbidden, wrongOtp.StatusCode);

        var rightOtp = await client.GetAsync($"/api/downloads/{commit.ShareCode}?otp={otp}");
        Assert.Equal(HttpStatusCode.Found, rightOtp.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_Endpoint_ValidatesOtp()
    {
        const string otp = "654321";
        var client = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(client, otp);

        var wrong = await client.PostAsJsonAsync(
            $"/api/deliveries/{commit.ShareCode}/verify-otp", new { Otp = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var right = await client.PostAsJsonAsync(
            $"/api/deliveries/{commit.ShareCode}/verify-otp", new { Otp = otp });
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task Download_ExpiredDelivery_Returns410()
    {
        var shareCode = await SeedDeliveryAsync(d => d.ExpiresAt = DateTime.UtcNow.AddMinutes(-1));

        var client = CreateNoRedirectClient();
        var response = await client.GetAsync($"/api/downloads/{shareCode}");
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Download_LimitReached_Returns403()
    {
        var shareCode = await SeedDeliveryAsync(d =>
        {
            d.MaxDownloads = 3;
            d.DownloadCount = 3;
        });

        var client = CreateNoRedirectClient();
        var response = await client.GetAsync($"/api/downloads/{shareCode}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListDeliveries_ShowsUploadedFile()
    {
        var client = CreateNoRedirectClient(authenticated: true);
        var commit = await UploadFileAsync(client);

        var response = await client.GetAsync("/api/deliveries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(commit.ShareCode, body);
        Assert.Contains(TestFileName, body);
    }

    [Fact]
    public async Task ListDeliveries_Unauthenticated_Returns401()
    {
        var client = CreateNoRedirectClient();
        var response = await client.GetAsync("/api/deliveries");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> SeedDeliveryAsync(Action<Delivery> mutate)
    {
        var shareCode = $"seed-{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            TotalBytes = TestFileSize,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 100,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        mutate(delivery);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return shareCode;
    }
}
