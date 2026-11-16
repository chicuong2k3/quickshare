using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Sender note on deliveries (issue #137): stored at commit, rendered escaped
/// on the download page, editable only before the first download.
/// </summary>
public sealed class DeliveryMessageTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DeliveryMessageTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DownloadPage_RendersMessageEscaped_NoXss()
    {
        var shareCode = await SeedDeliveryAsync("hello <script>alert(1)</script>");

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.Contains("Note from sender", html);
        Assert.Contains("hello &lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
    }

    [Fact]
    public async Task UpdateMessage_BeforeFirstDownload_Updates()
    {
        var shareCode = await SeedDeliveryAsync("original note");
        var id = await DeliveryIdAsync(shareCode);

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/deliveries/{id}/message", new { message = "updated note" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var info = await DownloadInfoAsync(shareCode);
        Assert.NotNull(info);
        Assert.Equal("updated note", info!.Message);
    }

    [Fact]
    public async Task UpdateMessage_AfterFirstDownload_Is409()
    {
        var shareCode = await SeedDeliveryAsync("note");
        var id = await DeliveryIdAsync(shareCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
            var delivery = (await db.Deliveries.FirstAsync(d => d.ShareCode == shareCode))!;
            delivery.DownloadCount = 1;
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/deliveries/{id}/message", new { message = "too late" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMessage_OtherSender_Is404()
    {
        var shareCode = await SeedDeliveryAsync("note");
        var id = await DeliveryIdAsync(shareCode);
        var other = await _factory.SeedUserAsync(Guid.NewGuid().ToString());

        var client = _factory.CreateAuthenticatedClient(other.Id.ToString());
        var response = await client.PatchAsJsonAsync($"/api/deliveries/{id}/message", new { message = "hijack" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMessage_TooLong_Is400()
    {
        var shareCode = await SeedDeliveryAsync("note");
        var id = await DeliveryIdAsync(shareCode);

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PatchAsJsonAsync($"/api/deliveries/{id}/message", new { message = new string('x', 501) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> SeedDeliveryAsync(string message)
    {
        var shareCode = $"msg{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        db.Deliveries.Add(new QuickShare.Data.Entities.Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "message-test.txt",
            ContentType = "text/plain",
            FileSize = 16,
            TotalBytes = 16,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 5,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            Message = message
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    private async Task<Guid> DeliveryIdAsync(string shareCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        return delivery.Id;
    }

    private async Task<InfoResult?> DownloadInfoAsync(string shareCode)
    {
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<Mediator.IMediator>();
        var result = await bus.Send<LightResults.Result<QuickShare.Features.Downloads.Info.DownloadInfoResponse>>(
            new QuickShare.Features.Downloads.Info.DownloadInfoQuery(shareCode), CancellationToken.None);
        return result.IsSuccess(out var value) ? new InfoResult(value.Message) : null;
    }

    private sealed record InfoResult(string? Message);
}
