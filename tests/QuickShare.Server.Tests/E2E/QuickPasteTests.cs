using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Quick Paste (issue #13): pasted uploads are tagged with a source, and the
/// SEO landing pages that funnel into the paste flow render.
/// </summary>
public sealed class QuickPasteTests : IClassFixture<TestWebAppFactory>
{
    private const string TextFileName = "pasted-2026-08-13.txt";
    private const string TextContentType = "text/plain";
    private const long TextFileSize = 64;

    private readonly TestWebAppFactory _factory;

    public QuickPasteTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData(RoutePaths.ToolsOnlineClipboard)]
    [InlineData(RoutePaths.ToolsSendImagesUncompressed)]
    [InlineData(RoutePaths.ToolsShareText)]
    public async Task ToolsLandingPage_Returns200(string url)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PastedUpload_PersistsPasteSource()
    {
        var shareCode = await UploadTextAsync(DeliverySources.Paste);
        Assert.Equal(DeliverySources.Paste, await SourceOfAsync(shareCode));
    }

    [Fact]
    public async Task Upload_WithoutSource_DefaultsToWeb()
    {
        var shareCode = await UploadTextAsync(source: null);
        Assert.Equal(DeliverySources.Web, await SourceOfAsync(shareCode));
    }

    [Fact]
    public async Task Upload_WithUnknownSource_FallsBackToWeb()
    {
        var shareCode = await UploadTextAsync("'; drop table deliveries; --");
        Assert.Equal(DeliverySources.Web, await SourceOfAsync(shareCode));
    }

    [Fact]
    public async Task PastedText_KeepsTextContentType_SoTheDownloadPageCanPreviewIt()
    {
        var shareCode = await UploadTextAsync(DeliverySources.Paste);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);

        Assert.Equal(TextContentType, delivery.ContentType);
        Assert.Equal(TextFileName, delivery.FileName);
    }

    private async Task<string> UploadTextAsync(string? source)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TextFileName,
            FileSize = TextFileSize,
            ContentType = TextContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TextFileName,
            ContentType = TextContentType,
            FileSize = TextFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            Source = source,
        });
        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        var commit = await commitResponse.Content.ReadFromJsonAsync<CommitR2Response>();
        Assert.NotNull(commit);
        return commit.ShareCode;
    }

    private async Task<string> SourceOfAsync(string shareCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        return delivery.Source;
    }
}
