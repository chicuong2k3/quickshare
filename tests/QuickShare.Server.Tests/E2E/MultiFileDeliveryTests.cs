using System.IO.Compression;
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
/// Multi-file deliveries (#79) and folder upload (#43).
///
/// Three things carry the feature and each is easy to get quietly wrong:
///
/// - **Every delivery has at least one file row**, including single-file ones. If that is not
///   true without exception, every reader needs a fallback and the ones that forget show an empty
///   list for the majority of deliveries.
/// - **A committed object key must belong to the delivery claiming it.** Commit is client-
///   supplied; without the check, a caller names somebody else's key and has it served under a
///   share code they control.
/// - **Quota counts the whole bundle.** Charging only file #1 would make a hundred-file upload
///   free after the first.
/// </summary>
public sealed class MultiFileDeliveryTests : IClassFixture<TestWebAppFactory>
{
    private const string ContentType = "application/pdf";
    private const long FileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public MultiFileDeliveryTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SingleFileDelivery_StillGetsAFileRow()
    {
        var (client, _) = await AccountAsync();
        var deliveryId = await UploadAsync(client, "single.pdf");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var files = await db.DeliveryFiles.AsNoTracking()
            .Where(f => f.DeliveryId == deliveryId).ToListAsync();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == deliveryId);

        // The invariant. A single-file delivery is a one-file bundle, not a different shape.
        Assert.Single(files);
        Assert.Equal(0, files[0].SortOrder);
        Assert.Equal(1, delivery.FileCount);
        Assert.False(delivery.IsMultiFile);
        // And the legacy columns still describe file #1, so nothing written before this changed.
        Assert.Equal("single.pdf", delivery.FileName);
        Assert.Equal(delivery.StoragePath, files[0].ObjectKey);
    }

    [Fact]
    public async Task Bundle_CarriesEveryFileAndTheirTotal()
    {
        var (client, _) = await AccountAsync();
        var deliveryId = await UploadAsync(client, "report.pdf", ["notes.txt", "data.csv"]);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var files = await db.DeliveryFiles.AsNoTracking()
            .Where(f => f.DeliveryId == deliveryId).OrderBy(f => f.SortOrder).ToListAsync();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == deliveryId);

        Assert.Equal(3, files.Count);
        Assert.Equal(["report.pdf", "notes.txt", "data.csv"], files.Select(f => f.FileName));
        Assert.Equal(3, delivery.FileCount);
        Assert.True(delivery.IsMultiFile);
        // FileSize still means file #1; TotalFileBytes means the bundle.
        Assert.True(delivery.TotalFileBytes > delivery.FileSize);
    }

    [Fact]
    public async Task Commit_RefusesAnObjectKeyFromAnotherDelivery()
    {
        var (client, _) = await AccountAsync();

        // A real delivery belonging to somebody else, with a real object key.
        var (victimClient, _) = await AccountAsync();
        var victimId = await UploadAsync(victimClient, "confidential.pdf");
        var victimKey = await ObjectKeyAsync(victimId);

        var start = await StartAsync(client, "innocent.pdf");

        var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new
        {
            deliveryId = start.DeliveryId,
            uploadId = start.UploadId,
            objectKey = start.ObjectKey,
            fileName = "innocent.pdf",
            contentType = ContentType,
            fileSize = FileSize,
            parts = new[] { new { partNumber = 1, eTag = "etag-1" } },
            expiryHours = 24,
            additionalFiles = new[]
            {
                new
                {
                    objectKey = victimKey,
                    uploadId = "borrowed",
                    fileName = "stolen.pdf",
                    contentType = ContentType,
                    fileSize = FileSize,
                    parts = new[] { new { partNumber = 1, eTag = "etag-1" } },
                },
            },
        });

        // If this ever returns 200, somebody else's file is downloadable from a share code the
        // attacker owns. That is the reason R2ObjectKey.BelongsTo exists.
        Assert.NotEqual(HttpStatusCode.OK, commit.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.DeliveryFiles.AsNoTracking().AnyAsync(f => f.ObjectKey == victimKey && f.DeliveryId == start.DeliveryId));
    }

    [Fact]
    public async Task AddFile_RefusesASessionTheCallerDoesNotOwn()
    {
        var (owner, _) = await AccountAsync();
        var (stranger, _) = await AccountAsync();

        var start = await StartAsync(owner, "mine.pdf");

        var response = await stranger.PostAsJsonAsync($"/api/uploads/{start.DeliveryId}/files", new
        {
            fileName = "theirs.pdf",
            contentType = ContentType,
            fileSize = FileSize,
            partNumbers = new[] { 1 },
        });

        // Otherwise anyone could staple a file onto somebody else's upload and have it delivered
        // under their name.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Zip_ContainsEveryFileOnce()
    {
        var (client, _) = await AccountAsync();
        var deliveryId = await UploadAsync(client, "a.txt", ["b.txt", "c.txt"]);
        var shareCode = await ShareCodeAsync(deliveryId);

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{shareCode}/zip");
        response.EnsureSuccessStatusCode();

        using var archive = await ReadArchiveAsync(response);

        Assert.Equal(["a.txt", "b.txt", "c.txt"], archive.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public async Task Zip_DeduplicatesRepeatedNames()
    {
        var (client, _) = await AccountAsync();
        // A sender can legitimately pick the same name twice — a zip with duplicate entries
        // extracts to one file in most tools, silently losing the rest.
        var deliveryId = await UploadAsync(client, "photo.jpg", ["photo.jpg"]);
        var shareCode = await ShareCodeAsync(deliveryId);

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{shareCode}/zip");
        response.EnsureSuccessStatusCode();

        using var archive = await ReadArchiveAsync(response);

        Assert.Equal(2, archive.Entries.Count);
        Assert.Equal(archive.Entries.Count, archive.Entries.Select(e => e.FullName).Distinct().Count());
    }

    [Fact]
    public async Task Zip_RebuildsAFolderStructure()
    {
        var (client, _) = await AccountAsync();
        var deliveryId = await UploadAsync(client, "trip/day1/beach.jpg", ["trip/day2/hike.jpg"]);
        var shareCode = await ShareCodeAsync(deliveryId);

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{shareCode}/zip");
        response.EnsureSuccessStatusCode();

        using var archive = await ReadArchiveAsync(response);

        // Folder upload (#43) is multi-file plus relative paths — the zip puts them back.
        Assert.Contains("trip/day2/hike.jpg", archive.Entries.Select(e => e.FullName));
    }

    [Fact]
    public async Task FileList_IsRefusedForAPasswordProtectedShare()
    {
        var (client, _) = await AccountAsync();
        var deliveryId = await UploadAsync(client, "secret.pdf", ["also-secret.pdf"], password: "hunter2hunter2");
        var shareCode = await ShareCodeAsync(deliveryId);

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{shareCode}/files");

        // Names, sizes and folder structure are exactly what the password was put there to
        // withhold — a file list is not metadata-free.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("../../etc/passwd", "etc/passwd")]
    [InlineData("/absolute/path.txt", "absolute/path.txt")]
    [InlineData("C:\\Users\\me\\file.txt", "Users/me/file.txt")]
    [InlineData("", "file-1")]
    [InlineData("..", "file-1")]
    public void SanitizeName_StripsTraversalAndRoots(string raw, string expected)
    {
        // Zip slip: an entry named ../../etc/passwd escapes the extraction directory in a
        // careless client. We cannot fix every client; we can refuse to ship the payload.
        Assert.Equal(expected, DeliveryFile.SanitizeName(raw, 0));
    }

    [Theory]
    [InlineData("uploads/{user}/{delivery}/file.txt", true)]
    [InlineData("uploads/eu/{user}/{delivery}/file.txt", true)]
    [InlineData("uploads/{user}/00000000000000000000000000000000/file.txt", false)]
    [InlineData("avatars/{user}.png", false)]
    [InlineData("", false)]
    public void BelongsTo_MatchesOnTheDeliverySegment(string template, bool expected)
    {
        var userId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var key = template
            .Replace("{user}", userId.ToString("N"), StringComparison.Ordinal)
            .Replace("{delivery}", deliveryId.ToString("N"), StringComparison.Ordinal);

        Assert.Equal(expected, R2ObjectKey.BelongsTo(key, deliveryId));
    }

    [Fact]
    public async Task DownloadPage_ListsTheBundleServerSide()
    {
        var (client, _) = await AccountAsync();
        var deliveryId = await UploadAsync(client, "brief.pdf", ["assets.zip"]);
        var shareCode = await ShareCodeAsync(deliveryId);

        var html = await _factory.CreateClient().GetStringAsync($"/d/{shareCode}");

        // Server-rendered for the same reason the comment thread is: the recipient page has to
        // work with scripting off, and a bundle whose contents only appear after a fetch is an
        // empty page to that reader.
        Assert.Contains("dl-files", html, StringComparison.Ordinal);
        Assert.Contains("assets.zip", html, StringComparison.Ordinal);
        Assert.Contains($"/api/downloads/{shareCode}/zip", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadPage_KeepsASingleFileDeliveryUnchanged()
    {
        var (client, _) = await AccountAsync();
        var deliveryId = await UploadAsync(client, "solo.pdf");
        var shareCode = await ShareCodeAsync(deliveryId);

        var html = await _factory.CreateClient().GetStringAsync($"/d/{shareCode}");

        // One file is not a bundle. Showing a "1 file" list and a "download all" button would be
        // new noise on the page every existing delivery already renders correctly.
        Assert.DoesNotContain("dl-files", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    /// <summary>
    /// Buffers the response before reading it as a zip.
    ///
    /// The endpoint streams — deliberately, so bundle size is not capped by server memory — and a
    /// streamed archive uses data descriptors, which <see cref="ZipArchive"/> can only read from
    /// a seekable stream. Every real client (browser, curl) writes to a file first, so this is
    /// the test catching up with how the archive is actually consumed, not a defect in it.
    /// </summary>
    private static async Task<ZipArchive> ReadArchiveAsync(HttpResponseMessage response)
    {
        var buffer = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private async Task<(HttpClient Client, Guid UserId)> AccountAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private static async Task<StartR2Response> StartAsync(HttpClient client, string fileName)
    {
        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = fileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartR2Response>())!;
    }

    private static async Task<Guid> UploadAsync(
        HttpClient client, string fileName, string[]? extras = null, string? password = null)
    {
        var start = await StartAsync(client, fileName);

        var additional = new List<object>();
        foreach (var extra in extras ?? [])
        {
            var added = await client.PostAsJsonAsync($"/api/uploads/{start.DeliveryId}/files", new
            {
                fileName = extra,
                contentType = ContentType,
                fileSize = FileSize,
                partNumbers = new[] { 1 },
            });
            added.EnsureSuccessStatusCode();
            var body = await added.Content.ReadFromJsonAsync<AddedFile>();

            additional.Add(new
            {
                objectKey = body!.ObjectKey,
                uploadId = body.UploadId,
                fileName = extra,
                contentType = ContentType,
                fileSize = FileSize,
                parts = new[] { new { partNumber = 1, eTag = "etag-1" } },
            });
        }

        var commit = await client.PostAsJsonAsync("/api/uploads/commit-r2", new
        {
            deliveryId = start.DeliveryId,
            uploadId = start.UploadId,
            objectKey = start.ObjectKey,
            fileName,
            contentType = ContentType,
            fileSize = FileSize,
            parts = new[] { new { partNumber = 1, eTag = "etag-1" } },
            expiryHours = 24,
            password,
            additionalFiles = additional,
        });
        commit.EnsureSuccessStatusCode();

        return start.DeliveryId;
    }

    private sealed record AddedFile(string UploadId, string ObjectKey);

    private async Task<string> ObjectKeyAsync(Guid deliveryId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Deliveries.AsNoTracking()
            .Where(d => d.Id == deliveryId).Select(d => d.StoragePath!).FirstAsync();
    }

    private async Task<string> ShareCodeAsync(Guid deliveryId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Deliveries.AsNoTracking()
            .Where(d => d.Id == deliveryId).Select(d => d.ShareCode).FirstAsync();
    }
}
