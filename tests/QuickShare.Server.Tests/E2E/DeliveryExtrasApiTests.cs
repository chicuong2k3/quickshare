using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Remaining delivery-related endpoints: upload abort and P2P share creation via API.
/// </summary>
public sealed class DeliveryExtrasApiTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DeliveryExtrasApiTests(TestWebAppFactory factory) => _factory = factory;

    // ── Uploads: abort ──

    [Fact]
    public async Task AbortUpload_RemovesUploadSession()
    {
        var client = _factory.CreateAuthenticatedClient();
        var start = await (await client.PostAsJsonAsync("/api/uploads/start-r2", new
        {
            FileName = "abort-me.bin",
            FileSize = 100,
            ContentType = "application/octet-stream",
            PartNumbers = new[] { 1 },
        })).Content.ReadFromJsonAsync<StartR2Json>();
        Assert.NotNull(start);

        var abort = await client.PostAsJsonAsync("/api/uploads/abort-r2", new
        {
            start.DeliveryId,
            start.UploadId,
            start.ObjectKey,
        });
        Assert.Equal(HttpStatusCode.OK, abort.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UploadSessions.FindAsync(start.DeliveryId);
        Assert.Null(session);
    }

    // ── P2P share API ──

    [Fact]
    public async Task CreateP2PShare_ReturnsShareLink()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/shares/p2p", new
        {
            FileName = "p2p.bin",
            FileSize = 123,
            MimeType = "application/octet-stream",
            BaseUrl = "http://localhost",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("/p2p/d/", body);
    }

    [Fact]
    public async Task CreateP2PShare_MissingFileName_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/shares/p2p", new
        {
            FileName = "",
            FileSize = 123,
            MimeType = "application/octet-stream",
            BaseUrl = "http://localhost",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record StartR2Json(Guid DeliveryId, string UploadId, string ObjectKey);
}
