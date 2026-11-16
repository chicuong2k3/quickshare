using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Abuse/DMCA report flow (issue #53): anonymous report creation, admin inbox,
/// suspend makes the delivery undownloadable.
/// </summary>
public sealed class AbuseReportTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AbuseReportTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AnonymousReport_CreatesOpenReport()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/report",
            new CreateReportPayload("copyright", "This is my copyrighted video"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var delivery = db.Deliveries.Single(d => d.ShareCode == shareCode);
        var report = db.AbuseReports.Single(r => r.DeliveryId == delivery.Id);
        Assert.Equal(QuickShare.Data.Entities.AbuseReportStatus.Open, report.Status);
        Assert.Equal("copyright", report.Category);
        Assert.Equal("This is my copyrighted video", report.Note);
        Assert.NotEmpty(report.ReporterIpHash);
    }

    [Fact]
    public async Task InvalidCategory_IsRejected()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/report",
            new CreateReportPayload("nonsense", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReportOnMissingDelivery_Is404()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync("/api/deliveries/doesnotexist/report",
            new CreateReportPayload("abuse", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminSuspend_MakesDeliveryUndownloadable()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/report",
            new CreateReportPayload("malware", "Suspicious binary"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
            var delivery = db.Deliveries.Single(d => d.ShareCode == shareCode);
            var report = db.AbuseReports.Single(r => r.DeliveryId == delivery.Id);
            var admin = _factory.CreateAuthenticatedClient();
            var resolve = await admin.PostAsJsonAsync($"/api/admin/reports/{report.Id}/resolve",
                new ResolveReportPayload("suspend"));
            Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        }

        // Delivery is now suspended — the download page and the API both refuse.
        var page = await client.GetAsync($"/d/{shareCode}");
        var pageHtml = await page.Content.ReadAsStringAsync();
        Assert.Contains("download-phase-suspended", pageHtml);

        var download = await client.GetAsync($"/api/downloads/{shareCode}");
        Assert.Equal(HttpStatusCode.Gone, download.StatusCode);
    }

    [Fact]
    public async Task AdminList_ShowsOpenReports_ButNotResolved()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/report",
            new CreateReportPayload("abuse", null));

        var admin = _factory.CreateAuthenticatedClient();
        var list = await admin.GetFromJsonAsync<List<ReportListItem>>("/api/admin/reports");
        Assert.NotNull(list);
        Assert.Contains(list, r => r.Category == "abuse");
    }

    [Fact]
    public async Task NonAdmin_IsForbiddenFromReports()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/admin/reports");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> SeedDeliveryAsync()
    {
        var shareCode = $"rpt{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        db.Deliveries.Add(new QuickShare.Data.Entities.Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "report-test.bin",
            ContentType = "application/octet-stream",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 5,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    private sealed class CreateReportPayload(string category, string? note)
    {
        public string Category { get; } = category;
        public string? Note { get; } = note;
    }

    private sealed class ResolveReportPayload(string action)
    {
        public string Action { get; } = action;
    }

    private sealed class ReportListItem
    {
        public Guid Id { get; set; }
        public Guid DeliveryId { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
