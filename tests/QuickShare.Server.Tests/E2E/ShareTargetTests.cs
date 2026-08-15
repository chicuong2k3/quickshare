using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Android/Chrome Web Share Target (issue #14): POST stashes the file, /send
/// fetches it back, and stale temp uploads expire to 404.
/// </summary>
public sealed class ShareTargetTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public ShareTargetTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ShareTarget_StashesFile_AndSendFetchesIt()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("hello from share target"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "File", "shared-note.txt");

        var post = await client.PostAsync("/share-target", content);
        Assert.Equal(HttpStatusCode.Found, post.StatusCode);
        var location = post.Headers.Location;
        Assert.NotNull(location);

        var match = System.Text.RegularExpressions.Regex.Match(location!.ToString(), @"shared=([0-9a-f-]+)");
        Assert.True(match.Success);
        var id = match.Groups[1].Value;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
            var row = await db.TempUploads.AsNoTracking().FirstOrDefaultAsync(t => t.Id == Guid.Parse(id));
            Assert.NotNull(row);
            Assert.Equal("shared-note.txt", row!.FileName);
            Assert.True(System.IO.File.Exists(row.TempPath));
        }

        var get = await client.GetAsync($"/api/share-target/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadAsStringAsync();
        Assert.Equal("hello from share target", body);
    }

    [Fact]
    public async Task GetShareTarget_UnknownId_Is404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/share-target/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetShareTarget_ExpiredRow_Is404()
    {
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qs-test-expired.bin");
        await System.IO.File.WriteAllTextAsync(tempPath, "stale");

        Guid rowId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
            var row = new TempUpload
            {
                Id = Guid.CreateVersion7(),
                FileName = "stale.bin",
                ContentType = "application/octet-stream",
                Size = 5,
                TempPath = tempPath
            };
            db.TempUploads.Add(row);
            await db.SaveChangesAsync();

            // The audit interceptor stamps CreatedAt on add — force it into the past.
            await db.TempUploads
                .Where(t => t.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, DateTime.UtcNow.AddMinutes(-30)));
            rowId = row.Id;
        }

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/share-target/{rowId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
