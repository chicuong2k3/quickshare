using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// ZIP content listing before download (issue #215).
/// </summary>
public sealed class ZipContentsTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public ZipContentsTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record ZipEntryResponse(string Name, long Size, long CompressedSize, bool IsDirectory);

    private static byte[] BuildZip()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var folder = zip.CreateEntry("folder/file1.txt");
            using (var w = new StreamWriter(folder.Open(), Encoding.UTF8))
            {
                w.Write("hello");
            }

            var file2 = zip.CreateEntry("file2.txt");
            using (var w = new StreamWriter(file2.Open(), Encoding.UTF8))
            {
                w.Write("world");
            }
        }
        return ms.ToArray();
    }

    private async Task<Delivery> SeedAsync(string fileName, string contentType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = null,
            FileName = fileName,
            ContentType = contentType,
            FileSize = 2048,
            ShareCode = Guid.NewGuid().ToString("N")[..10],
            MaxDownloads = 10,
            Status = DeliveryStatus.Active,
            StoragePath = "stub/archive.zip",
            ExpiresAt = DateTime.UtcNow.AddDays(2),
            CreatedAt = DateTime.UtcNow,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    [Fact]
    public async Task ZipContents_ListsEntries()
    {
        var delivery = await SeedAsync("archive.zip", "application/zip");
        var stub = (LocalDiskStorageProviderStub)_factory.Services.GetRequiredService<IStorageProvider>();
        stub.NextReadBytes = BuildZip();

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{delivery.ShareCode}/zip-contents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entries = await response.Content.ReadFromJsonAsync<List<ZipEntryResponse>>();
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "folder/file1.txt");
        Assert.Contains(entries, e => e.Name == "file2.txt");
    }

    [Fact]
    public async Task ZipContents_NonZip_Returns400()
    {
        var delivery = await SeedAsync("doc.pdf", "application/pdf");

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{delivery.ShareCode}/zip-contents");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
