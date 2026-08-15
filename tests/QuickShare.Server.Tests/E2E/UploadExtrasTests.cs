using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Upload extras (issues #22 + #137): client-chosen expiry (clamped to plan
/// max), optional sender note stored at commit.
/// </summary>
public sealed class UploadExtrasTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "extras-test.bin";
    private const long TestFileSize = 1024;
    private const string TestContentType = "application/octet-stream";

    private readonly TestWebAppFactory _factory;

    public UploadExtrasTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Commit_WithExpiryHours_SetsExpiry()
    {
        var client = _factory.CreateClient();
        var commit = await UploadAsync(client, expiryHours: 48);
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        var body = await commit.Content.ReadFromJsonAsync<CommitR2Response>();

        var info = await InfoAsync(body!.ShareCode);
        Assert.NotNull(info);
        Assert.True(info!.ExpiresAt.HasValue);
        var expected = DateTime.UtcNow.AddHours(48);
        Assert.True(Math.Abs((info.ExpiresAt.Value - expected).TotalMinutes) < 5);
    }

    [Fact]
    public async Task Commit_ExpiryOverPlanMax_Is400()
    {
        // Anonymous = Free tier, max 168h (7 days). 200h must be rejected.
        var client = _factory.CreateClient();
        var response = await UploadAsync(client, expiryHours: 200);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Commit_ZeroExpiry_Is400()
    {
        var client = _factory.CreateClient();
        var response = await UploadAsync(client, expiryHours: 0);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Commit_WithMessage_StoresMessage()
    {
        var client = _factory.CreateClient();
        var commit = await UploadAsync(client, message: "Due Friday — please review");
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        var body = await commit.Content.ReadFromJsonAsync<CommitR2Response>();

        var info = await InfoAsync(body!.ShareCode);
        Assert.NotNull(info);
        Assert.Equal("Due Friday — please review", info!.Message);
    }

    [Fact]
    public async Task Commit_MessageTooLong_Is400()
    {
        var client = _factory.CreateClient();
        var response = await UploadAsync(client, message: new string('x', 501));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> UploadAsync(HttpClient client, double? expiryHours = null, string? message = null)
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

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start!.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            ExpiryHours = expiryHours,
            Message = message
        });
        return commitResponse;
    }

    private async Task<InfoResult?> InfoAsync(string shareCode)
    {
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
        var result = await bus.InvokeAsync<LightResults.Result<QuickShare.Features.Downloads.Info.DownloadInfoResponse>>(
            new QuickShare.Features.Downloads.Info.DownloadInfoQuery(shareCode), CancellationToken.None);
        return result.IsSuccess(out var value) ? new InfoResult(value.ExpiresAt, value.Message) : null;
    }

    private sealed record InfoResult(DateTime? ExpiresAt, string? Message);
}
