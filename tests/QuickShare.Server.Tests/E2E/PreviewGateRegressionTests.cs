using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

public sealed class PreviewGateRegressionTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PreviewGateRegressionTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("preview")]
    [InlineData("office-preview")]
    [InlineData("zip-contents")]
    public async Task PendingApproval_IsIndistinguishableFromMissing(string endpoint)
    {
        var code = await SeedAsync(delivery => delivery.Status = DeliveryStatus.PendingApproval);
        using var client = _factory.CreateClient();

        var pending = await client.GetAsync($"/api/downloads/{code}/{endpoint}");
        var missing = await client.GetAsync($"/api/downloads/{Guid.NewGuid():N}/{endpoint}");

        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
        Assert.Equal(missing.StatusCode, pending.StatusCode);
    }

    [Theory]
    [InlineData("preview", true)]
    [InlineData("office-preview", true)]
    [InlineData("zip-contents", true)]
    [InlineData("preview", false)]
    [InlineData("office-preview", false)]
    [InlineData("zip-contents", false)]
    public async Task RecipientGates_BlockEveryPreview(string endpoint, bool emailOtp)
    {
        var code = await SeedAsync(delivery =>
        {
            delivery.EmailOtpEnabled = emailOtp;
            delivery.RequireRecipientEmail = !emailOtp;
        });
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/downloads/{code}/{endpoint}");

        Assert.Equal(endpoint == "office-preview" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("office-preview")]
    [InlineData("zip-contents")]
    public async Task FutureSchedule_BlocksEveryPreview(string endpoint)
    {
        var code = await SeedAsync(delivery =>
        {
            delivery.Status = DeliveryStatus.Scheduled;
            delivery.DeliverAt = DateTime.UtcNow.AddHours(1);
        });
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/downloads/{code}/{endpoint}");

        Assert.Equal(endpoint == "office-preview" ? HttpStatusCode.BadRequest : HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> SeedAsync(Action<Delivery> configure)
    {
        var code = $"pg{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "preview.png",
            ContentType = "image/png",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = code,
            MaxDownloads = 5,
            StoragePath = $"stub/{code}",
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        configure(delivery);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return code;
    }
}
