using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.List;
using QuickShare.Features.Deliveries.Ocr;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;
using QuickShare.Querying;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// OCR search (#243).
///
/// The recognition itself is stubbed — a suite that shelled out to tesseract would be testing
/// tesseract. What is ours, and what is asserted here, is who gets indexed at all (opt-in only,
/// images only, never in privacy mode), that a search finds the words inside a picture, and that
/// a result says *why* it matched.
/// </summary>
public sealed class OcrSearchTests : IClassFixture<TestWebAppFactory>
{
    private const string ImageName = "invoice.png";
    private const string ImageType = "image/png";
    private const long ImageSize = 4096;
    private const string InvoiceText = "PO-4417 total 1,240,000 VND";

    private readonly TestWebAppFactory _factory;

    public OcrSearchTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Ocr_RunsOnlyWhenTheSenderAsksForIt()
    {
        var (client, _) = await SenderAsync();

        var indexed = await UploadAsync(client, ocr: true);
        var untouched = await UploadAsync(client, ocr: false);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Reading somebody's screenshot is a data touch, and not one to make by default.
        Assert.True(await db.Deliveries.AsNoTracking().Where(d => d.Id == indexed).Select(d => d.OcrRequested).SingleAsync());
        Assert.False(await db.Deliveries.AsNoTracking().Where(d => d.Id == untouched).Select(d => d.OcrRequested).SingleAsync());
    }

    [Fact]
    public async Task Ocr_IsRefusedByPrivacyMode()
    {
        var (client, _) = await SenderAsync();

        var deliveryId = await UploadAsync(client, ocr: true, privacyMode: true);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A share that deliberately keeps no record must not grow a searchable transcript instead.
        Assert.False(await db.Deliveries.AsNoTracking().Where(d => d.Id == deliveryId).Select(d => d.OcrRequested).SingleAsync());
    }

    [Fact]
    public async Task Ocr_IgnoresThingsThatAreNotImages()
    {
        var (client, _) = await SenderAsync();

        var deliveryId = await UploadAsync(client, ocr: true, fileName: "contract.pdf", contentType: "application/pdf");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // A PDF carries its own text layer; asking an image OCR to read it is the wrong tool.
        Assert.False(await db.Deliveries.AsNoTracking().Where(d => d.Id == deliveryId).Select(d => d.OcrRequested).SingleAsync());
    }

    [Fact]
    public async Task Search_FindsAFileByTheTextInsideIt()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, ocr: true);
        await RunOcrAsync();

        var page = await SearchAsync(client, "PO-4417");

        var hit = Assert.Single(page.Items);
        Assert.Equal(deliveryId, hit.Id);
        // The badge is the point: without it an OCR hit looks like a name match that is not there.
        Assert.True(hit.MatchedInImage);
    }

    [Fact]
    public async Task Search_ByNameStillSaysItWasNotAnImageMatch()
    {
        var (client, _) = await SenderAsync();
        await UploadAsync(client, ocr: true);
        await RunOcrAsync();

        var page = await SearchAsync(client, "invoice");

        var hit = Assert.Single(page.Items);
        Assert.False(hit.MatchedInImage);
    }

    [Fact]
    public async Task Search_DoesNotReachFilesThatWereNeverIndexed()
    {
        var (client, _) = await SenderAsync();
        await UploadAsync(client, ocr: false);
        await RunOcrAsync();

        var page = await SearchAsync(client, "PO-4417");

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Ocr_RecordsAnEmptyResultSoItIsNotRetriedForever()
    {
        var (client, _) = await SenderAsync();
        var deliveryId = await UploadAsync(client, ocr: true, fileName: "blank.png");

        StubOcrEngine.Text = null;
        try
        {
            await RunOcrAsync();
        }
        finally
        {
            StubOcrEngine.Text = InvoiceText;
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.DeliveryOcrTexts.AsNoTracking().SingleOrDefaultAsync(o => o.DeliveryId == deliveryId);

        // "We looked and found nothing" is a result, and the row is what stops the same
        // unreadable screenshot being re-processed on every tick.
        Assert.NotNull(row);
        Assert.Null(row.Text);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid UserId)> SenderAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);

        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private async Task RunOcrAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<OcrJob>().RunAsync();
    }

    private static async Task<CursorPage<DeliveryItem>> SearchAsync(HttpClient client, string term)
    {
        var response = await client.GetAsync($"/api/deliveries?q={Uri.EscapeDataString(term)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<CursorPage<DeliveryItem>>())!;
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, bool ocr, bool privacyMode = false,
        string fileName = ImageName, string contentType = ImageType)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = fileName,
            FileSize = ImageSize,
            ContentType = contentType,
            PartNumbers = [1],
        });
        startResponse.EnsureSuccessStatusCode();
        var start = (await startResponse.Content.ReadFromJsonAsync<StartR2Response>())!;

        var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = fileName,
            ContentType = contentType,
            FileSize = ImageSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            ExpiryHours = 24,
            OcrEnabled = ocr,
            PrivacyMode = privacyMode,
        });
        commit.EnsureSuccessStatusCode();

        return start.DeliveryId;
    }
}
