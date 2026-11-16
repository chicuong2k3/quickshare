using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Reports;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class AdminReportFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AdminReportFormTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData(ReportAction.Resolve)]
    [InlineData(ReportAction.Suspend)]
    public async Task NativeForm_ResolvesReportAndAppliesAction(string action)
    {
        var reportId = Guid.CreateVersion7();
        var deliveryId = Guid.CreateVersion7();
        var shareCode = $"rpt{Guid.NewGuid():N}"[..12];
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.Add(new Delivery
            {
                Id = deliveryId,
                SenderId = _factory.TestUserId,
                FileName = "report-test.bin",
                ContentType = "application/octet-stream",
                FileSize = 1024,
                TotalBytes = 1024,
                ChunkCount = 1,
                ShareCode = shareCode,
                MaxDownloads = 5,
                StoragePath = $"stub/{shareCode}",
                ExpiresAt = DateTime.UtcNow.AddDays(3),
            });
            db.AbuseReports.Add(new AbuseReport
            {
                Id = reportId,
                DeliveryId = deliveryId,
                Category = AbuseReportCategory.Abuse,
                ReporterIpHash = "test",
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateAuthenticatedClient();
        var html = await client.GetStringAsync(RoutePaths.AdminReports);
        Assert.Contains(reportId.ToString(), html);
        Assert.Contains($"name=\"{ResolveReportFormEndpoint.ActionField}\"", html);
        Assert.Contains($"value=\"{action}\"", html);
        var token = AntiforgeryHelper.ExtractToken(html);
        using var form = Form(token, reportId, action);
        using var response = await client.PostAsync(ResolveReportFormEndpoint.FormRoute, form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await verifyDb.AbuseReports.AsNoTracking().SingleAsync(r => r.Id == reportId);
        var delivery = await verifyDb.Deliveries.AsNoTracking().SingleAsync(d => d.Id == deliveryId);
        Assert.Equal(AbuseReportStatus.Resolved, report.Status);
        Assert.Equal(action, report.Resolution);
        Assert.Equal(action == ReportAction.Suspend, delivery.Status == DeliveryStatus.Suspended);
    }

    [Fact]
    public async Task NativeForm_RejectsMissingTokenAndInvalidAction()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var missingToken = Form(string.Empty, Guid.CreateVersion7(), ReportAction.Suspend);
        using var rejected = await client.PostAsync(ResolveReportFormEndpoint.FormRoute, missingToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var token = AntiforgeryHelper.ExtractToken(await client.GetStringAsync(RoutePaths.AdminFlags));
        using var invalid = Form(token, Guid.CreateVersion7(), "invalid");
        using var invalidResponse = await client.PostAsync(ResolveReportFormEndpoint.FormRoute, invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task NativeForm_RejectsNonAdmin()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, "nobody@example.com");
        using var form = Form(string.Empty, Guid.CreateVersion7(), ReportAction.Suspend);
        using var response = await client.PostAsync(ResolveReportFormEndpoint.FormRoute, form);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static FormUrlEncodedContent Form(string token, Guid reportId, string action) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        [ResolveReportFormEndpoint.ReportIdField] = reportId.ToString(),
        [ResolveReportFormEndpoint.ActionField] = action,
    });
}
