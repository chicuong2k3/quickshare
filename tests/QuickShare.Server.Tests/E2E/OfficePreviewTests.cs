using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Office document preview (issue #94): the endpoint extracts docx/xlsx/pptx text as escaped
/// HTML, gated exactly like the media preview (no OTP/password/encrypted payloads).
/// </summary>
public sealed class OfficePreviewTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public OfficePreviewTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record OfficePreviewResponse(string Html, string Format);

    private async Task<string> SeedAsync(
        string contentType, string fileName, string? storagePath = "uploads/x/doc.docx", string? passwordHash = null)
    {
        var shareCode = "off" + Guid.NewGuid().ToString("N")[..8];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            FileName = fileName,
            ContentType = contentType,
            FileSize = 512,
            MaxDownloads = 10,
            ShareCode = shareCode,
            Status = DeliveryStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddHours(2),
            StoragePath = storagePath,
            PasswordHash = passwordHash,
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    [Fact]
    public async Task NonOffice_Returns400()
    {
        var code = await SeedAsync("application/pdf", "file.pdf");
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/downloads/{code}/office-preview");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GatedDelivery_Returns400()
    {
        var code = await SeedAsync(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "gated.docx",
            passwordHash: BCrypt.Net.BCrypt.HashPassword("pw"));
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/downloads/{code}/office-preview");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Docx_ReturnsEscapedHtml()
    {
        var code = await SeedAsync(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "report.docx");

        // Stub storage returns a minimal docx for OpenReadAsync.
        var storage = (LocalDiskStorageProviderStub)_factory.Services.GetRequiredService<IStorageProvider>();
        storage.NextReadBytes = BuildDocx();

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/downloads/{code}/office-preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<OfficePreviewResponse>();
        Assert.NotNull(result);
        Assert.Equal("docx", result.Format);
        Assert.Contains("Quarterly report", result.Html);
    }

    private static byte[] BuildDocx()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var sw = new StreamWriter(entry.Open());
            sw.Write(
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                "<w:p><w:r><w:t>Quarterly report</w:t></w:r></w:p>" +
                "</w:body></w:document>");
        }
        return ms.ToArray();
    }
}
